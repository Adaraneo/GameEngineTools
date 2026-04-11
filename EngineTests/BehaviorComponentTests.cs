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
    using GameEngineTools.Characters.Engines.SemanticMemory;
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
            SemanticMemoryState? semanticMemory = null,
            RelationshipState? relationships = null,
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
            Chronotype chronotype = Chronotype.Neutral,
            Personality? personality = null,
            IRandomSource? random = null,
            HumanId? selfId = null,
            SexBiology biology = SexBiology.Female,
            AttractionProfile? attractionProfile = null)
        {
            var ctx = Human(memory, semanticMemory, relationships, surfaceKind, noise, crowding, stress, valence, hunger, thirst, energy, competence, curiosity, affiliation, chronotype, personality, random, selfId, biology, attractionProfile);
            var s = BehaviorMath.ComputeNeedState(ctx, new Dictionary<string, double>(), state ?? new BehaviorState(10, 5, 5, 20, 50, 30, null));
            return new BehaviorContext(now ?? new WDateTime(0), WTimeSpan.FromHours(1), ctx, new EventCollector(), s, new BehaviorConfig(), new Dictionary<string, double>(), new Dictionary<string, DecisionWorkingSet>());
        }

        internal static IHumanContext Human(MemoryIndex? memory, SemanticMemoryState? semanticMemory, RelationshipState? relationships, SurfaceKind surfaceKind, double noise, double crowding, double stress, double valence, double hunger, double thirst, double energy, double competence, double curiosity, double affiliation, Chronotype chronotype, Personality? personality = null, IRandomSource? random = null, HumanId? selfId = null, SexBiology biology = SexBiology.Female, AttractionProfile? attractionProfile = null)
        {
            var effectivePersonality = personality ?? new Personality(new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), AttachmentStyle.Secure, CommunicationStyle.Direct,
                new MotivationWeights(affiliation, 0.5, 0.3, 0.4, competence, 0.5, curiosity, 0.6, 0.3), Sociosexuality.Intermediate, chronotype);
            var snapshot = new EnginesSnapshot(
                new PhysiologyState(energy, 0, hunger, thirst, 0, 0, 0, null),
                new PsychologyState(valence, 0.5, 0.5, stress, 0, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface("test", false, noise, crowding, surfaceKind),
                relationships ?? new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                memory ?? new MemoryIndex(new List<EpisodicMemory>()),
                semanticMemory ?? SemanticMemoryState.Empty);

            return new HumanContext
            {
                Id = selfId ?? new HumanId(Guid.NewGuid()),
                Biology = biology,
                Personality = effectivePersonality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(effectivePersonality),
                AttractionProfile = attractionProfile,
                Snapshot = snapshot,
                Random = random ?? new LocalZeroRandom(),
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
    [TestClass]
    public class SocialNeedsEngineTests : TestBase
    {
        [TestMethod]
        public void Evaluate_Belonging_CreatesTargetedReachOutCandidate()
        {
            var selfId = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var semantic = new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet>
            {
                [other] = new(other, new Dictionary<PersonBeliefKind, PersonBelief>
                {
                    [PersonBeliefKind.Warm] = new(other, PersonBeliefKind.Warm, 0.8, 0.5, 2, new WDateTime(0)),
                    [PersonBeliefKind.EmotionallySafe] = new(other, PersonBeliefKind.EmotionallySafe, 0.75, 0.5, 2, new WDateTime(0))
                })
            });
            var relationships = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new(selfId, other, 60, 65, 55, 40, 40, 20, 20, 55, 50, 60, new DomainBreakdown(0, 0, 0, 0, 0))
            });

            var output = new SocialNeedsEngine().Evaluate(BehaviorComponentTestFactory.Context(affiliation: 1, selfId: selfId, semanticMemory: semantic, relationships: relationships));
            var reachOut = output.Candidates.Single(c => c.Name == ReachOut);
            Assert.AreEqual(other, reachOut.SocialTargeting?.TargetHuman);
        }
    }
    [TestClass] public class CompetenceNeedsEngineTests : TestBase { [TestMethod] public void Evaluate_Competence_CreatesWorkCandidate() { var output = new CompetenceNeedsEngine().Evaluate(BehaviorComponentTestFactory.Context(competence: 1)); Assert.IsTrue(output.Candidates.Any(c => c.Name == Work)); } }
    [TestClass] public class AutonomyExplorationNeedsEngineTests : TestBase { [TestMethod] public void Evaluate_Curiosity_CreatesPublicMovementCandidate() { var output = new AutonomyExplorationNeedsEngine().Evaluate(BehaviorComponentTestFactory.Context(curiosity: 1)); Assert.IsTrue(output.Candidates.Any(c => c.Name == MoveToPublic)); } }
    [TestClass] public class SleepCoordinatorTests : TestBase { [TestMethod] public void Tick_HighRestNeed_RequestsSleepPrompt() { var cfg = new SleepConfig() with { SleepPromptThreshold = 20 }; var engine = new DefaultSleepCoordinator(cfg, new BehaviorConfig(), LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning))); var context = BehaviorComponentTestFactory.Context(state: new BehaviorState(90, 5, 5, 20, 50, 30, null), surfaceKind: SurfaceKind.Private); var result = engine.Tick(context); Assert.IsTrue(result.ConsumedTick); Assert.IsTrue(context.Outbox.Drain().OfType<SleepPromptRequested>().Any()); } }
    [TestClass] public class TraitBiasEngineTests : TestBase
    {
        [TestMethod]
        public void Modify_HighConscientiousness_BoostsWork()
        {
            var personality = new Personality(new BigFive(0.5, 0.95, 0.4, 0.5, 0.2), AttachmentStyle.Secure, CommunicationStyle.Direct, new MotivationWeights(0.3, 0.8, 0.3, 0.4, 0.9, 0.5, 0.4, 0.4, 0.3), Sociosexuality.Intermediate, Chronotype.Neutral);
            var candidates = new List<BehaviorCandidate> { new(Work, 10, WTimeSpan.FromHours(1), BehaviorDomain.Competence) };
            new TraitBiasEngine().Modify(BehaviorComponentTestFactory.Context(personality: personality), candidates);
            Assert.IsTrue(candidates[0].Utility > 10);
        }

        [TestMethod]
        public void Modify_HighAffiliationAndExtraversion_BoostsReachOut()
        {
            var personality = new Personality(new BigFive(0.6, 0.4, 0.95, 0.8, 0.2), AttachmentStyle.Secure, CommunicationStyle.Direct, new MotivationWeights(1.0, 0.4, 0.2, 0.4, 0.5, 0.5, 0.4, 0.4, 0.3), Sociosexuality.Intermediate, Chronotype.Neutral);
            var candidates = new List<BehaviorCandidate> { new(ReachOut, 10, WTimeSpan.FromHours(1), BehaviorDomain.Social) };
            new TraitBiasEngine().Modify(BehaviorComponentTestFactory.Context(personality: personality), candidates);
            Assert.IsTrue(candidates[0].Utility > 10);
        }
    }
    [TestClass] public class AffectiveStateEngineTests : TestBase { [TestMethod] public void Modify_HighStress_BoostsSelfCare() { var candidates = new List<BehaviorCandidate> { new(SelfCare, 10, WTimeSpan.FromHours(1), BehaviorDomain.Physiological) }; new AffectiveStateEngine().Modify(BehaviorComponentTestFactory.Context(stress: 100), candidates); Assert.IsTrue(candidates[0].Utility > 10); } }
    [TestClass] public class CircadianArousalEngineTests : TestBase { [TestMethod] public void Modify_ChronotypePeak_BoostsMovement() { var candidates = new List<BehaviorCandidate> { new(MoveToPublic, 0, WTimeSpan.FromHours(1), BehaviorDomain.Exploration) }; new CircadianArousalEngine().Modify(BehaviorComponentTestFactory.Context(now: new WDateTime(WTimeSpan.FromHours(8).Ticks), chronotype: Chronotype.Lark), candidates); Assert.IsTrue(candidates[0].Utility > 0); } }
    [TestClass] public class HabitRoutineEngineTests : TestBase { [TestMethod] public void Modify_PreviousWorkPlan_BoostsWork() { var state = new BehaviorState(10, 5, 5, 20, 50, 30, new PlannedAction(Work, new WDateTime(0), WTimeSpan.FromMinutes(1), 1)); var candidates = new List<BehaviorCandidate> { new(Work, 10, WTimeSpan.FromHours(1), BehaviorDomain.Competence) }; new HabitRoutineEngine().Modify(BehaviorComponentTestFactory.Context(state: state), candidates); Assert.IsTrue(candidates[0].Utility > 10); } }
    [TestClass] public class MemoryInfluenceEngineTests : TestBase { [TestMethod] public void Modify_NegativeInteraction_EmitsMemoryRecallAndPenalizesReachOut() { var memory = new MemoryIndex(new List<EpisodicMemory> { new(Guid.NewGuid(), new WDateTime(0), "Interaction:A", 0.5, EmotionalTag.Negative, 0.7) }); var context = BehaviorComponentTestFactory.Context(memory: memory); var candidates = new List<BehaviorCandidate> { new(ReachOut, 10, WTimeSpan.FromHours(1), BehaviorDomain.Social) }; new MemoryInfluenceEngine().Modify(context, candidates); Assert.IsTrue(candidates[0].Utility < 10); Assert.IsTrue(context.Outbox.Drain().OfType<MemoryRecalled>().Any()); } }
    [TestClass] public class EnvironmentalAffordanceEngineTests : TestBase { [TestMethod] public void Modify_SocialSurface_PenalizesWorkHereAndBoostsMoveToWork() { var candidates = new List<BehaviorCandidate> { new(Work, 100, WTimeSpan.FromHours(1), BehaviorDomain.Competence), new(MoveToWork, 0, WTimeSpan.FromHours(1), BehaviorDomain.Competence) }; new EnvironmentalAffordanceEngine().Modify(BehaviorComponentTestFactory.Context(surfaceKind: SurfaceKind.Social, competence: 1), candidates); Assert.IsTrue(candidates.Single(c => c.Name == Work).Utility < 100); Assert.IsTrue(candidates.Single(c => c.Name == MoveToWork).Utility > 0); } }
    [TestClass] public class ActionArbitrationEngineTests : TestBase { [TestMethod] public void Arbitrate_SelectsHighestUtilityCandidate() { var result = new DefaultActionArbitrationEngine(LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger<DefaultActionArbitrationEngine>()).Arbitrate(BehaviorComponentTestFactory.Context(), new List<BehaviorCandidate> { new(Idle, 1, WTimeSpan.FromHours(1), BehaviorDomain.Physiological), new(Work, 10, WTimeSpan.FromHours(1), BehaviorDomain.Competence) }); Assert.AreEqual(Work, result.SelectedCandidate?.Name); } }
    [TestClass] public class HumanInconsistencyArbitrationTests : TestBase { [TestMethod] public void Arbitrate_WhenIdentityAndCopingConflict_CanPickNonUtilityLeader() { var personality = new Personality(new BigFive(0.4, 1.0, 0.2, 0.3, 0.95), AttachmentStyle.Avoidant, CommunicationStyle.Direct, new MotivationWeights(0.2, 0.8, 0.6, 0.2, 0.9, 0.3, 0.2, 0.4, 0.2), Sociosexuality.Intermediate, Chronotype.Neutral); var snapshot = new EnginesSnapshot(new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null), new PsychologyState(0, 0.5, 0.5, 95, 0, DiscreteEmotion.Neutral), new BehaviorState(10, 5, 5, 20, 50, 30, null), new InteractionSurface("test", false, 0.3, 0.3, SurfaceKind.Unknown), new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()), new MemoryIndex(new List<EpisodicMemory>())); var context = new BehaviorContext(new WDateTime(0), WTimeSpan.FromHours(1), new HumanContext { Id = new HumanId(Guid.NewGuid()), Biology = SexBiology.Female, Personality = personality, PsychologyProfile = PsychologicalProfile.FromPersonality(personality), Snapshot = snapshot, Random = new LocalConflictRandom(), Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"), EventBus = new LocalNullEventBus(), Scheduler = new LocalNullScheduler() }, new EventCollector(), new BehaviorState(10, 5, 5, 20, 50, 30, null), new BehaviorConfig(), new Dictionary<string, double>()); var result = new DefaultActionArbitrationEngine(LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger<DefaultActionArbitrationEngine>()).Arbitrate(context, new List<BehaviorCandidate> { new(ReachOut, 10, WTimeSpan.FromHours(1), BehaviorDomain.Social), new(Work, 9, WTimeSpan.FromHours(1), BehaviorDomain.Competence) }); Assert.AreEqual(ReachOut, result.IntendedCandidate?.Name); Assert.AreEqual(Work, result.SelectedCandidate?.Name); Assert.IsFalse(string.IsNullOrWhiteSpace(result.ConflictReason)); } }

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

    [TestClass]
    public class BehaviorScenarioTests : TestBase
    {
        private static DefaultBehaviorEngine BuildBehaviorEngine(BehaviorConfig? config = null, SleepConfig? sleepConfig = null)
            => new(Microsoft.Extensions.Options.Options.Create(config ?? new BehaviorConfig()), Microsoft.Extensions.Options.Options.Create(sleepConfig ?? new SleepConfig() with { SleepPromptThreshold = 95 }), LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));

        private static PersonBeliefSet Beliefs(HumanId other, double warm = 0, double safe = 0, double rejecting = 0, double critical = 0, double reliable = 0)
        {
            var beliefs = new Dictionary<PersonBeliefKind, PersonBelief>();
            if (warm > 0) beliefs[PersonBeliefKind.Warm] = new(other, PersonBeliefKind.Warm, warm, 0.5, 2, new WDateTime(0));
            if (safe > 0) beliefs[PersonBeliefKind.EmotionallySafe] = new(other, PersonBeliefKind.EmotionallySafe, safe, 0.5, 2, new WDateTime(0));
            if (rejecting > 0) beliefs[PersonBeliefKind.Rejecting] = new(other, PersonBeliefKind.Rejecting, rejecting, 0.5, 2, new WDateTime(0));
            if (critical > 0) beliefs[PersonBeliefKind.Critical] = new(other, PersonBeliefKind.Critical, critical, 0.5, 2, new WDateTime(0));
            if (reliable > 0) beliefs[PersonBeliefKind.Reliable] = new(other, PersonBeliefKind.Reliable, reliable, 0.5, 2, new WDateTime(0));
            return new PersonBeliefSet(other, beliefs);
        }

        private static RelationshipEdge Relationship(HumanId self, HumanId other, double trust, double familiarity, double closeness, double comfort)
            => new(self, other, 60, trust, familiarity, 40, 40, 30, 30, closeness, 50, comfort, new DomainBreakdown(0, 0, 0, 0, 0));

        [TestMethod]
        public void Tick_WorkFocusedCharacter_RetainsWorkIntentAcrossTicks()
        {
            var personality = new Personality(new BigFive(0.5, 0.95, 0.3, 0.4, 0.2), AttachmentStyle.Secure, CommunicationStyle.Direct, new MotivationWeights(0.2, 0.8, 0.3, 0.4, 1.0, 0.5, 0.3, 0.3, 0.2), Sociosexuality.Intermediate, Chronotype.Neutral);
            var engine = BuildBehaviorEngine();
            var human = BehaviorComponentTestFactory.Human(null, null, null, SurfaceKind.Work, 0.2, 0.2, 10, 0, 5, 5, 95, 1.0, 0.3, 0.2, Chronotype.Neutral, personality);
            var outbox = new EventCollector();

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), human, outbox);
            engine.Tick(new WDateTime(WTimeSpan.FromHours(1).Ticks), WTimeSpan.FromHours(1), human, outbox);

            Assert.AreEqual(BehaviorIntentKind.WorkSession, engine.State.ActiveIntent?.Kind);
            Assert.AreEqual(Work, engine.State.CurrentPlan?.Name);
        }

        [TestMethod]
        public void Tick_HighStressAndNoise_CommitsRetreatMovement()
        {
            var engine = BuildBehaviorEngine();
            var human = BehaviorComponentTestFactory.Human(null, null, null, SurfaceKind.Social, 1.0, 0.9, 100, -0.8, 5, 5, 95, 0.3, 0.3, 0.2, Chronotype.Neutral);
            var outbox = new EventCollector();

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), human, outbox);

            Assert.AreEqual(MoveToRest, engine.State.CurrentPlan?.Name);
        }

        [TestMethod]
        public void Tick_PositiveSocialMemory_CommitsReachOut()
        {
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var engine = BuildBehaviorEngine();
            var memory = new MemoryIndex(new List<EpisodicMemory> { new(Guid.NewGuid(), new WDateTime(0), "Interaction:Friendly", 0.5, EmotionalTag.Positive, 0.8, OtherPerson: other) });
            var semantic = new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet> { [other] = Beliefs(other, warm: 0.8, safe: 0.7) });
            var relationships = new RelationshipState(new Dictionary<HumanId, RelationshipEdge> { [other] = Relationship(self, other, 68, 60, 55, 62) });
            var personality = new Personality(new BigFive(0.5, 0.4, 0.8, 0.8, 0.2), AttachmentStyle.Secure, CommunicationStyle.Direct, new MotivationWeights(1.0, 0.4, 0.2, 0.4, 0.4, 0.5, 0.3, 0.3, 0.2), Sociosexuality.Intermediate, Chronotype.Neutral);
            var human = BehaviorComponentTestFactory.Human(memory, semantic, relationships, SurfaceKind.Social, 0.2, 0.2, 10, 0.2, 5, 5, 95, 0.4, 0.3, 1.0, Chronotype.Neutral, personality, selfId: self);
            var outbox = new EventCollector();

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), human, outbox);

            Assert.AreEqual(ReachOut, engine.State.CurrentPlan?.Name);
            Assert.AreEqual(other, engine.State.CurrentPlan?.TargetHuman);
        }

        [TestMethod]
        public void Tick_HighRestNeed_RequestsSleepPromptThroughBehaviorEngine()
        {
            var engine = BuildBehaviorEngine(sleepConfig: new SleepConfig() with { SleepPromptThreshold = 20 });
            engine.RestoreState(new BehaviorState(90, 5, 5, 20, 50, 30, null));
            var human = BehaviorComponentTestFactory.Human(null, null, null, SurfaceKind.Private, 0.2, 0.2, 0, 0, 5, 5, 10, 0.5, 0.5, 0.5, Chronotype.Neutral);
            var outbox = new EventCollector();

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), human, outbox);

            Assert.IsTrue(outbox.Drain().OfType<SleepPromptRequested>().Any());
        }

        [TestMethod]
        public void Tick_TwoTargetsWithDifferentSemanticHistory_SelectsSaferPerson()
        {
            var self = new HumanId(Guid.NewGuid());
            var personA = new HumanId(Guid.NewGuid());
            var personB = new HumanId(Guid.NewGuid());
            var semantic = new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet>
            {
                [personA] = Beliefs(personA, warm: 0.8, safe: 0.8, reliable: 0.6),
                [personB] = Beliefs(personB, warm: 0.2, safe: 0.1, rejecting: 0.8, critical: 0.6)
            });
            var relationships = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [personA] = Relationship(self, personA, 70, 65, 60, 68),
                [personB] = Relationship(self, personB, 55, 65, 62, 50)
            });
            var engine = BuildBehaviorEngine();
            var personality = new Personality(new BigFive(0.6, 0.5, 0.8, 0.8, 0.2), AttachmentStyle.Secure, CommunicationStyle.Direct, new MotivationWeights(1.0, 0.4, 0.2, 0.4, 0.4, 0.5, 0.3, 0.3, 0.3), Sociosexuality.Intermediate, Chronotype.Neutral);
            var human = BehaviorComponentTestFactory.Human(new MemoryIndex(new List<EpisodicMemory>()), semantic, relationships, SurfaceKind.Social, 0.2, 0.2, 10, 0.1, 5, 5, 95, 0.4, 0.3, 1.0, Chronotype.Neutral, personality, new LocalZeroRandom(), self);
            var outbox = new EventCollector();

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), human, outbox);

            var events = outbox.Drain();
            var committed = events.OfType<ActionCommitted>().Single();
            var proposedInteraction = events.OfType<InteractionProposed>().Single();
            Assert.AreEqual(ReachOut, committed.ActionName);
            Assert.AreEqual(personA, committed.TargetHuman);
            Assert.AreEqual(personA, proposedInteraction.To);
        }

        [TestMethod]
        public void Evaluate_IntimacyGating_BlocksUnsafeTargetButAllowsSafeTarget()
        {
            var self = new HumanId(Guid.NewGuid());
            var safeTarget = new HumanId(Guid.NewGuid());
            var unsafeTarget = new HumanId(Guid.NewGuid());
            var semantic = new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet>
            {
                [safeTarget] = Beliefs(safeTarget, warm: 0.9, safe: 0.85),
                [unsafeTarget] = Beliefs(unsafeTarget, rejecting: 0.9, critical: 0.7)
            });
            var relationships = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [safeTarget] = Relationship(self, safeTarget, 80, 70, 70, 75),
                [unsafeTarget] = Relationship(self, unsafeTarget, 30, 45, 20, 25)
            });
            var personality = new Personality(new BigFive(0.5, 0.4, 0.7, 0.5, 0.6), AttachmentStyle.Avoidant, CommunicationStyle.Direct, new MotivationWeights(0.7, 0.4, 0.2, 0.4, 0.4, 0.5, 0.3, 0.3, 1.0), Sociosexuality.Intermediate, Chronotype.Neutral);
            var output = new SocialNeedsEngine().Evaluate(BehaviorComponentTestFactory.Context(selfId: self, semanticMemory: semantic, relationships: relationships, personality: personality));

            var intimacyTargets = output.Candidates.Where(c => c.Name == InviteIntimacy).Select(c => c.SocialTargeting?.TargetHuman).ToList();
            CollectionAssert.Contains(intimacyTargets, safeTarget);
            CollectionAssert.DoesNotContain(intimacyTargets, unsafeTarget);
        }

        [TestMethod]
        public void Evaluate_TargetScoring_PrefersSemanticExpectationOverRawCloseness()
        {
            var self = new HumanId(Guid.NewGuid());
            var closeButUnsafe = new HumanId(Guid.NewGuid());
            var saferButLessClose = new HumanId(Guid.NewGuid());
            var semantic = new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet>
            {
                [closeButUnsafe] = Beliefs(closeButUnsafe, rejecting: 0.8, critical: 0.5),
                [saferButLessClose] = Beliefs(saferButLessClose, warm: 0.8, safe: 0.7, reliable: 0.5)
            });
            var relationships = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [closeButUnsafe] = Relationship(self, closeButUnsafe, 55, 80, 85, 50),
                [saferButLessClose] = Relationship(self, saferButLessClose, 65, 45, 40, 65)
            });
            var output = new SocialNeedsEngine().Evaluate(BehaviorComponentTestFactory.Context(selfId: self, semanticMemory: semantic, relationships: relationships, affiliation: 1.0));
            var reachOut = output.Candidates.Where(c => c.Name == ReachOut).OrderByDescending(c => c.Utility).First();

            Assert.AreEqual(saferButLessClose, reachOut.SocialTargeting?.TargetHuman);
        }

        [TestMethod]
        public void Evaluate_TargetGeneration_IsDeterministicForSameInputs()
        {
            var self = new HumanId(Guid.NewGuid());
            var a = new HumanId(Guid.NewGuid());
            var b = new HumanId(Guid.NewGuid());
            var semantic = new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet>
            {
                [a] = Beliefs(a, warm: 0.7, safe: 0.65),
                [b] = Beliefs(b, warm: 0.6, safe: 0.55)
            });
            var relationships = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [a] = Relationship(self, a, 60, 55, 50, 58),
                [b] = Relationship(self, b, 60, 55, 50, 58)
            });
            var contextA = BehaviorComponentTestFactory.Context(selfId: self, semanticMemory: semantic, relationships: relationships, affiliation: 1.0);
            var contextB = BehaviorComponentTestFactory.Context(selfId: self, semanticMemory: semantic, relationships: relationships, affiliation: 1.0);

            var resultA = new SocialNeedsEngine().Evaluate(contextA).Candidates.Where(c => c.Name == ReachOut).Select(c => c.SocialTargeting?.TargetHuman).ToList();
            var resultB = new SocialNeedsEngine().Evaluate(contextB).Candidates.Where(c => c.Name == ReachOut).Select(c => c.SocialTargeting?.TargetHuman).ToList();

            CollectionAssert.AreEqual(resultA, resultB);
        }
    }
}
