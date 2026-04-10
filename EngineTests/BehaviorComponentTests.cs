// BehaviorComponentTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Arbitration;
    using GameEngineTools.Characters.Engines.Behavior.Intent;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Behavior.Needs;
    using GameEngineTools.Characters.Engines.Behavior.Sleep;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using static GameEngineTools.Characters.Engines.ActionNames;

    internal static class BehaviorComponentTestFactory
    {
        internal static BehaviorContext Context(
            WDateTime? now = null,
            BehaviorState? state = null,
            MemoryIndex? memory = null,
            SurfaceKind surfaceKind = SurfaceKind.Unknown,
            double noise = 0.3,
            double crowding = 0.3,
            double stress = 0,
            double valence = 0,
            double hunger = 5,
            double thirst = 5,
            double energy = 95,
            double competence = 0.5,
            double curiosity = 0.5,
            double affiliation = 0.5,
            Chronotype chronotype = Chronotype.Neutral)
        {
            var ctx = Human(memory, surfaceKind, noise, crowding, stress, valence, hunger, thirst, energy, competence, curiosity, affiliation, chronotype);
            var s = BehaviorMath.ComputeNeedState(ctx, new Dictionary<string, double>(), state ?? new BehaviorState(10, 5, 5, 20, 50, 30, null));
            return new BehaviorContext(now ?? new WDateTime(0), WTimeSpan.FromHours(1), ctx, new EventCollector(), s, new BehaviorConfig(), new Dictionary<string, double>());
        }

        internal static IHumanContext Human(MemoryIndex? memory, SurfaceKind surfaceKind, double noise, double crowding, double stress, double valence, double hunger, double thirst, double energy, double competence, double curiosity, double affiliation, Chronotype chronotype)
        {
            var snapshot = new EnginesSnapshot(
                new PhysiologyState(energy, 0, hunger, thirst, 0, 0, 0, null),
                new PsychologyState(valence, 0.5, 0.5, stress, 0, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface("test", false, noise, crowding, surfaceKind),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                memory ?? new MemoryIndex(new List<EpisodicMemory>(), new Dictionary<string, SemanticFact>()));

            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = new Personality(new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), AttachmentStyle.Secure, CommunicationStyle.Direct,
                    new MotivationWeights(affiliation, 0.5, 0.3, 0.4, competence, 0.5, curiosity, 0.6, 0.3), Sociosexuality.Intermediate, chronotype),
                Snapshot = snapshot,
                Random = new LocalZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new LocalNullEventBus(),
                Scheduler = new LocalNullScheduler()
            };
        }
    }

    internal sealed class LocalZeroRandom : IRandomSource { public int Next(int min, int max) => min; public double NextUnit() => 0; public bool Chance(double p) => false; }
    internal sealed class LocalConflictRandom : IRandomSource { public int Next(int min, int max) => min; public double NextUnit() => 0; public bool Chance(double p) => p > 0; }
    internal sealed class LocalNullEventBus : IEventBus { public void Publish(IDomainEvent @event) { } public IDisposable Subscribe<TEvent>(Action<TEvent> h) where TEvent : class, IDomainEvent => new LocalDisposable(); }
    internal sealed class LocalNullScheduler : IScheduler { public ScheduledId ScheduleAt(WDateTime w, ScheduledAction a, string? t = null) => new(Guid.NewGuid()); public ScheduledId ScheduleAfter(WDateTime n, WTimeSpan d, ScheduledAction a, string? t = null) => new(Guid.NewGuid()); public bool Cancel(ScheduledId id) => true; public IEnumerable<(ScheduledId, ScheduledAction)> Due(WDateTime n) => Enumerable.Empty<(ScheduledId, ScheduledAction)>(); }
    internal sealed class LocalDisposable : IDisposable { public void Dispose() { } }

    [TestClass] public class PhysiologicalNeedsEngineTests : TestBase { [TestMethod] public void Evaluate_HungerAndThirst_CreatesFoodAndWaterCandidates() { var output = new PhysiologicalNeedsEngine().Evaluate(BehaviorComponentTestFactory.Context(hunger: 80, thirst: 70)); Assert.IsTrue(output.Candidates.Any(c => c.Name == Eat)); Assert.IsTrue(output.Candidates.Any(c => c.Name == Drink)); } }
    [TestClass] public class SocialNeedsEngineTests : TestBase { [TestMethod] public void Evaluate_Belonging_CreatesReachOutCandidate() { var output = new SocialNeedsEngine().Evaluate(BehaviorComponentTestFactory.Context(affiliation: 1)); Assert.IsTrue(output.Candidates.Any(c => c.Name == ReachOut)); } }
    [TestClass] public class CompetenceNeedsEngineTests : TestBase { [TestMethod] public void Evaluate_Competence_CreatesWorkCandidate() { var output = new CompetenceNeedsEngine().Evaluate(BehaviorComponentTestFactory.Context(competence: 1)); Assert.IsTrue(output.Candidates.Any(c => c.Name == Work)); } }
    [TestClass] public class AutonomyExplorationNeedsEngineTests : TestBase { [TestMethod] public void Evaluate_Curiosity_CreatesPublicMovementCandidate() { var output = new AutonomyExplorationNeedsEngine().Evaluate(BehaviorComponentTestFactory.Context(curiosity: 1)); Assert.IsTrue(output.Candidates.Any(c => c.Name == MoveToPublic)); } }
    [TestClass] public class SleepCoordinatorTests : TestBase { [TestMethod] public void Tick_HighRestNeed_RequestsSleepPrompt() { var cfg = new SleepConfig() with { SleepPromptThreshold = 20 }; var engine = new DefaultSleepCoordinator(cfg, new BehaviorConfig(), LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning))); var context = BehaviorComponentTestFactory.Context(state: new BehaviorState(90, 5, 5, 20, 50, 30, null), surfaceKind: SurfaceKind.Private); var result = engine.Tick(context); Assert.IsTrue(result.ConsumedTick); Assert.IsTrue(context.Outbox.Drain().OfType<SleepPromptRequested>().Any()); } }
    [TestClass] public class TraitBiasEngineTests : TestBase { [TestMethod] public void Modify_IsCurrentlyNoOp() { var candidates = new List<BehaviorCandidate> { new(Work, 10, WTimeSpan.FromHours(1), BehaviorDomain.Competence) }; new TraitBiasEngine().Modify(BehaviorComponentTestFactory.Context(), candidates); Assert.AreEqual(10, candidates[0].Utility); } }
    [TestClass] public class AffectiveStateEngineTests : TestBase { [TestMethod] public void Modify_HighStress_BoostsSelfCare() { var candidates = new List<BehaviorCandidate> { new(SelfCare, 10, WTimeSpan.FromHours(1), BehaviorDomain.Physiological) }; new AffectiveStateEngine().Modify(BehaviorComponentTestFactory.Context(stress: 100), candidates); Assert.IsTrue(candidates[0].Utility > 10); } }
    [TestClass] public class CircadianArousalEngineTests : TestBase { [TestMethod] public void Modify_ChronotypePeak_BoostsMovement() { var candidates = new List<BehaviorCandidate> { new(MoveToPublic, 0, WTimeSpan.FromHours(1), BehaviorDomain.Exploration) }; new CircadianArousalEngine().Modify(BehaviorComponentTestFactory.Context(now: new WDateTime(WTimeSpan.FromHours(8).Ticks), chronotype: Chronotype.Lark), candidates); Assert.IsTrue(candidates[0].Utility > 0); } }
    [TestClass] public class HabitRoutineEngineTests : TestBase { [TestMethod] public void Modify_PreviousWorkPlan_BoostsWork() { var state = new BehaviorState(10, 5, 5, 20, 50, 30, new PlannedAction(Work, new WDateTime(0), WTimeSpan.FromMinutes(1), 1)); var candidates = new List<BehaviorCandidate> { new(Work, 10, WTimeSpan.FromHours(1), BehaviorDomain.Competence) }; new HabitRoutineEngine().Modify(BehaviorComponentTestFactory.Context(state: state), candidates); Assert.IsTrue(candidates[0].Utility > 10); } }
    [TestClass] public class MemoryInfluenceEngineTests : TestBase { [TestMethod] public void Modify_NegativeInteraction_EmitsMemoryRecallAndPenalizesReachOut() { var memory = new MemoryIndex(new List<EpisodicMemory> { new(Guid.NewGuid(), new WDateTime(0), "Interaction:A", 0.5, EmotionalTag.Negative, 0.7) }, new Dictionary<string, SemanticFact>()); var context = BehaviorComponentTestFactory.Context(memory: memory); var candidates = new List<BehaviorCandidate> { new(ReachOut, 10, WTimeSpan.FromHours(1), BehaviorDomain.Social) }; new MemoryInfluenceEngine().Modify(context, candidates); Assert.IsTrue(candidates[0].Utility < 10); Assert.IsTrue(context.Outbox.Drain().OfType<MemoryRecalled>().Any()); } }
    [TestClass] public class EnvironmentalAffordanceEngineTests : TestBase { [TestMethod] public void Modify_SocialSurface_PenalizesWorkHereAndBoostsMoveToWork() { var candidates = new List<BehaviorCandidate> { new(Work, 100, WTimeSpan.FromHours(1), BehaviorDomain.Competence), new(MoveToWork, 0, WTimeSpan.FromHours(1), BehaviorDomain.Competence) }; new EnvironmentalAffordanceEngine().Modify(BehaviorComponentTestFactory.Context(surfaceKind: SurfaceKind.Social, competence: 1), candidates); Assert.IsTrue(candidates.Single(c => c.Name == Work).Utility < 100); Assert.IsTrue(candidates.Single(c => c.Name == MoveToWork).Utility > 0); } }
    [TestClass] public class ActionArbitrationEngineTests : TestBase { [TestMethod] public void Arbitrate_SelectsHighestUtilityCandidate() { var result = new DefaultActionArbitrationEngine(LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger<DefaultActionArbitrationEngine>()).Arbitrate(BehaviorComponentTestFactory.Context(), new List<BehaviorCandidate> { new(Idle, 1, WTimeSpan.FromHours(1), BehaviorDomain.Physiological), new(Work, 10, WTimeSpan.FromHours(1), BehaviorDomain.Competence) }); Assert.AreEqual(Work, result.SelectedCandidate?.Name); } }
    [TestClass] public class HumanInconsistencyArbitrationTests : TestBase { [TestMethod] public void Arbitrate_WhenIdentityAndCopingConflict_CanPickNonUtilityLeader() { var personality = new Personality(new BigFive(0.4, 1.0, 0.2, 0.3, 0.95), AttachmentStyle.Avoidant, CommunicationStyle.Direct, new MotivationWeights(0.2, 0.8, 0.6, 0.2, 0.9, 0.3, 0.2, 0.4, 0.2), Sociosexuality.Intermediate, Chronotype.Neutral); var snapshot = new EnginesSnapshot(new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null), new PsychologyState(0, 0.5, 0.5, 95, 0, DiscreteEmotion.Neutral), new BehaviorState(10, 5, 5, 20, 50, 30, null), new InteractionSurface("test", false, 0.3, 0.3, SurfaceKind.Unknown), new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()), new MemoryIndex(new List<EpisodicMemory>(), new Dictionary<string, SemanticFact>())); var context = new BehaviorContext(new WDateTime(0), WTimeSpan.FromHours(1), new HumanContext { Id = new HumanId(Guid.NewGuid()), Biology = SexBiology.Female, Personality = personality, PsychologyProfile = PsychologicalProfile.FromPersonality(personality), Snapshot = snapshot, Random = new LocalConflictRandom(), Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"), EventBus = new LocalNullEventBus(), Scheduler = new LocalNullScheduler() }, new EventCollector(), new BehaviorState(10, 5, 5, 20, 50, 30, null), new BehaviorConfig(), new Dictionary<string, double>()); var result = new DefaultActionArbitrationEngine(LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger<DefaultActionArbitrationEngine>()).Arbitrate(context, new List<BehaviorCandidate> { new(ReachOut, 10, WTimeSpan.FromHours(1), BehaviorDomain.Social), new(Work, 9, WTimeSpan.FromHours(1), BehaviorDomain.Competence) }); Assert.AreEqual(ReachOut, result.IntendedCandidate?.Name); Assert.AreEqual(Work, result.SelectedCandidate?.Name); Assert.IsFalse(string.IsNullOrWhiteSpace(result.ConflictReason)); } }

    [TestClass]
    public class IntentManagementEngineTests : TestBase
    {
        private static DefaultIntentManagementEngine BuildEngine() => new(LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger<DefaultIntentManagementEngine>());

        [TestMethod]
        public void SelectIntentFromCandidates()
        {
            var context = BehaviorComponentTestFactory.Context();
            var state = BuildEngine().UpdateIntent(context, new List<BehaviorCandidate> { new(Work, 30, WTimeSpan.FromHours(1), BehaviorDomain.Competence), new(MoveToPublic, 5, WTimeSpan.FromHours(1), BehaviorDomain.Exploration) });
            Assert.AreEqual(BehaviorIntentKind.WorkSession, state.ActiveIntent?.Kind);
        }

        [TestMethod]
        public void RetainIntentUnderThreshold()
        {
            var intent = new ActiveIntent(BehaviorIntentKind.WorkSession, Work, new WDateTime(0), new WDateTime(0), 20, 1, new WDateTime(WTimeSpan.FromHours(2).Ticks));
            var context = BehaviorComponentTestFactory.Context(state: new BehaviorState(10, 5, 5, 20, 50, 30, null, ActiveIntent: intent));
            var state = BuildEngine().UpdateIntent(context, new List<BehaviorCandidate> { new(Work, 20, WTimeSpan.FromHours(1), BehaviorDomain.Competence), new(MoveToSocial, 25, WTimeSpan.FromHours(1), BehaviorDomain.Social) });
            Assert.AreEqual(BehaviorIntentKind.WorkSession, state.ActiveIntent?.Kind);
        }

        [TestMethod]
        public void SwitchIntentWhenDominanceExceeded()
        {
            var intent = new ActiveIntent(BehaviorIntentKind.WorkSession, Work, new WDateTime(0), new WDateTime(0), 20, 1, new WDateTime(WTimeSpan.FromHours(2).Ticks));
            var context = BehaviorComponentTestFactory.Context(state: new BehaviorState(10, 5, 5, 20, 50, 30, null, ActiveIntent: intent));
            var state = BuildEngine().UpdateIntent(context, new List<BehaviorCandidate> { new(Work, 20, WTimeSpan.FromHours(1), BehaviorDomain.Competence), new(MoveToSocial, 40, WTimeSpan.FromHours(1), BehaviorDomain.Social) });
            Assert.AreEqual(BehaviorIntentKind.SocialSeeking, state.ActiveIntent?.Kind);
        }

        [TestMethod]
        public void ApplyBiasToMatchingCandidates()
        {
            var intent = new ActiveIntent(BehaviorIntentKind.WorkSession, Work, new WDateTime(0), new WDateTime(0), 20, 2, new WDateTime(WTimeSpan.FromHours(2).Ticks));
            var context = BehaviorComponentTestFactory.Context(state: new BehaviorState(10, 5, 5, 20, 50, 30, null, ActiveIntent: intent));
            var candidates = new List<BehaviorCandidate> { new(Work, 10, WTimeSpan.FromHours(1), BehaviorDomain.Competence), new(MoveToSocial, 10, WTimeSpan.FromHours(1), BehaviorDomain.Social) };
            BuildEngine().ApplyBias(context, candidates);
            Assert.IsTrue(candidates.Single(c => c.Name == Work).Utility > candidates.Single(c => c.Name == MoveToSocial).Utility);
        }

        [TestMethod]
        public void CommitmentDoesNotGoBelowZero()
        {
            Assert.AreEqual(0, DefaultIntentManagementEngine.ClampCommitment(-5));
            Assert.AreEqual(0, DefaultIntentManagementEngine.ClampCommitment(0));
        }

        [TestMethod]
        public void ApplyBias_NeverProducesNegativeBias()
        {
            var intent = new ActiveIntent(BehaviorIntentKind.WorkSession, Work, new WDateTime(0), new WDateTime(0), 20, -50, new WDateTime(WTimeSpan.FromHours(2).Ticks));
            var context = BehaviorComponentTestFactory.Context(
                state: new BehaviorState(10, 5, 5, 20, 50, 30, null),
                now: new WDateTime(0)) with
            {
                State = new BehaviorState(10, 5, 5, 20, 50, 30, null, ActiveIntent: intent)
            };
            var candidates = new List<BehaviorCandidate> { new(Work, 10, WTimeSpan.FromHours(1), BehaviorDomain.Competence) };
            BuildEngine().ApplyBias(context, candidates);
            Assert.IsTrue(candidates[0].Utility >= 10);
        }

        [TestMethod]
        public void EmergencyOverridesIntent()
        {
            var intent = new ActiveIntent(BehaviorIntentKind.WorkSession, Work, new WDateTime(0), new WDateTime(0), 20, 1, new WDateTime(WTimeSpan.FromHours(2).Ticks));
            var context = BehaviorComponentTestFactory.Context(state: new BehaviorState(10, 5, 5, 20, 50, 30, null, ActiveIntent: intent));
            var state = BuildEngine().UpdateIntent(context, new List<BehaviorCandidate> { new(Work, 10, WTimeSpan.FromHours(1), BehaviorDomain.Competence), new(SelfCare, 80, WTimeSpan.FromHours(1), BehaviorDomain.Physiological) });
            Assert.AreEqual(BehaviorIntentKind.SelfCare, state.ActiveIntent?.Kind);
        }

        [TestMethod]
        public void TimeoutClearsIntent()
        {
            var intent = new ActiveIntent(BehaviorIntentKind.WorkSession, Work, new WDateTime(0), new WDateTime(0), 20, 1, new WDateTime(0));
            var context = BehaviorComponentTestFactory.Context(now: new WDateTime(WTimeSpan.FromHours(3).Ticks), state: new BehaviorState(10, 5, 5, 20, 50, 30, null, ActiveIntent: intent));
            var state = BuildEngine().UpdateIntent(context, new List<BehaviorCandidate> { new(MoveToPublic, 15, WTimeSpan.FromHours(1), BehaviorDomain.Exploration) });
            Assert.AreEqual(BehaviorIntentKind.Exploration, state.ActiveIntent?.Kind);
        }

        [TestMethod]
        public void UpdateIntent_SelectsStrongestTargetActionWithinWinningIntent()
        {
            var context = BehaviorComponentTestFactory.Context();
            var state = BuildEngine().UpdateIntent(
                context,
                new List<BehaviorCandidate>
                {
                    new(MoveToWork, 20, WTimeSpan.FromHours(1), BehaviorDomain.Competence),
                    new(Work, 30, WTimeSpan.FromHours(1), BehaviorDomain.Competence),
                    new(MoveToSocial, 5, WTimeSpan.FromHours(1), BehaviorDomain.Social)
                });
            Assert.AreEqual(BehaviorIntentKind.WorkSession, state.ActiveIntent?.Kind);
            Assert.AreEqual(Work, state.ActiveIntent?.TargetAction);
        }

        [TestMethod]
        public void UpdateIntent_WinningNoneIntent_DoesNotCreateActiveIntent()
        {
            var context = BehaviorComponentTestFactory.Context();
            var state = BuildEngine().UpdateIntent(
                context,
                new List<BehaviorCandidate>
                {
                    new(Idle, 25, WTimeSpan.FromHours(1), BehaviorDomain.Physiological),
                    new(MoveToPublic, 5, WTimeSpan.FromHours(1), BehaviorDomain.Exploration)
                });
            Assert.IsNull(state.ActiveIntent);
        }

        [TestMethod]
        public void UpdateIntent_GroupScoring_UsesTop1PlusWeightedTop2()
        {
            var context = BehaviorComponentTestFactory.Context();
            var state = BuildEngine().UpdateIntent(
                context,
                new List<BehaviorCandidate>
                {
                    new(Work, 20, WTimeSpan.FromHours(1), BehaviorDomain.Competence),
                    new(Create, 19, WTimeSpan.FromHours(1), BehaviorDomain.Competence),
                    new(MoveToSocial, 24, WTimeSpan.FromHours(1), BehaviorDomain.Social)
                });
            Assert.AreEqual(BehaviorIntentKind.WorkSession, state.ActiveIntent?.Kind);
        }

        [TestMethod]
        public void UpdateIntent_RetainedIntent_RefreshesTargetAction()
        {
            var intent = new ActiveIntent(BehaviorIntentKind.WorkSession, MoveToWork, new WDateTime(0), new WDateTime(0), 20, 1, new WDateTime(WTimeSpan.FromHours(2).Ticks));
            var context = BehaviorComponentTestFactory.Context(state: new BehaviorState(10, 5, 5, 20, 50, 30, null, ActiveIntent: intent));
            var state = BuildEngine().UpdateIntent(
                context,
                new List<BehaviorCandidate>
                {
                    new(MoveToWork, 15, WTimeSpan.FromHours(1), BehaviorDomain.Competence),
                    new(Work, 18, WTimeSpan.FromHours(1), BehaviorDomain.Competence),
                    new(MoveToSocial, 20, WTimeSpan.FromHours(1), BehaviorDomain.Social)
                });
            Assert.AreEqual(BehaviorIntentKind.WorkSession, state.ActiveIntent?.Kind);
            Assert.AreEqual(Work, state.ActiveIntent?.TargetAction);
        }

        [TestMethod]
        public void WorksWithPendingPreparedAction()
        {
            var intent = new ActiveIntent(BehaviorIntentKind.WorkSession, MoveToWork, new WDateTime(0), new WDateTime(0), 20, 1, new WDateTime(WTimeSpan.FromHours(2).Ticks));
            var context = BehaviorComponentTestFactory.Context(state: new BehaviorState(10, 5, 5, 20, 50, 30, new PlannedAction(MoveToWork, new WDateTime(0), WTimeSpan.FromHours(2), 20), ActiveIntent: intent));
            var candidates = new List<BehaviorCandidate> { new(MoveToWork, 10, WTimeSpan.FromHours(1), BehaviorDomain.Competence), new(Work, 10, WTimeSpan.FromHours(1), BehaviorDomain.Competence) };
            BuildEngine().ApplyBias(context, candidates);
            Assert.IsTrue(candidates.All(c => c.Utility >= 10));
        }
    }
}
