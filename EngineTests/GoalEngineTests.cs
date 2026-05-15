// GoalEngineTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Goals;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using static GameEngineTools.Characters.Engines.ActionNames;

    [TestClass]
    public class GoalEngineTests : TestBase
    {
        // ── Section 1: Personality seeding ────────────────────────────────────

        [TestMethod]
        public void SeedFromPersonality_HighCompetence_SeedsMasterCraftGoal()
        {
            var engine = BuildEngine();
            var personality = BuildPersonality(competence: 0.9);
            engine.SeedFromPersonality(personality, new WDateTime(0));

            Assert.IsTrue(engine.State.Active.Any(g => g.Kind == PersistentGoalKind.MasterCraft));
        }

        [TestMethod]
        public void SeedFromPersonality_LowCompetence_DoesNotSeedMasterCraft()
        {
            var engine = BuildEngine();
            var personality = BuildPersonality(competence: 0.3);
            engine.SeedFromPersonality(personality, new WDateTime(0));

            Assert.IsFalse(engine.State.Active.Any(g => g.Kind == PersistentGoalKind.MasterCraft));
        }

        [TestMethod]
        public void SeedFromPersonality_HighAffiliation_SeedsPartnerGoal()
        {
            var engine = BuildEngine();
            var personality = BuildPersonality(affiliation: 0.9);
            engine.SeedFromPersonality(personality, new WDateTime(0));

            Assert.IsTrue(engine.State.Active.Any(g => g.Kind == PersistentGoalKind.FindPartner));
        }

        [TestMethod]
        public void SeedFromPersonality_HighOpenness_SeedsFindMeaning()
        {
            var engine = BuildEngine();
            var personality = BuildPersonality(openness: 0.9);
            engine.SeedFromPersonality(personality, new WDateTime(0));

            Assert.IsTrue(engine.State.Active.Any(g => g.Kind == PersistentGoalKind.FindMeaning));
        }

        [TestMethod]
        public void SeedFromPersonality_MultipleThresholds_SeedsMultipleGoals()
        {
            var engine = BuildEngine();
            var personality = BuildPersonality(competence: 0.9, affiliation: 0.9, openness: 0.9);
            engine.SeedFromPersonality(personality, new WDateTime(0));

            var active = engine.State.Active.ToList();
            Assert.IsTrue(active.Any(g => g.Kind == PersistentGoalKind.MasterCraft));
            Assert.IsTrue(active.Any(g => g.Kind == PersistentGoalKind.FindPartner));
            Assert.IsTrue(active.Any(g => g.Kind == PersistentGoalKind.FindMeaning));
        }

        // ── Section 2: Salience decay ──────────────────────────────────────────

        [TestMethod]
        public void Tick_InactiveGoal_SalienceDecaysEachTick()
        {
            var engine = BuildEngine();
            var personality = BuildPersonality(competence: 0.9);
            var now = new WDateTime(0);
            engine.SeedFromPersonality(personality, now);

            var before = engine.State.Active.First(g => g.Kind == PersistentGoalKind.MasterCraft).Salience;

            var dt = WTimeSpan.FromHours(24);
            engine.Tick(now + dt, dt, BuildContext(new HumanId(Guid.NewGuid())), new EventCollector());

            var after = engine.State.Active.FirstOrDefault(g => g.Kind == PersistentGoalKind.MasterCraft)?.Salience ?? before;
            Assert.IsTrue(after < before, $"Expected salience to decay: before={before}, after={after}");
        }

        [TestMethod]
        public void Tick_NeglectedGoal_SalienceDecaysFasterAfterThreshold()
        {
            var config = new GoalConfig(NegligenceThresholdDays: 0.0, NegligenceDecayMultiplier: 5.0);
            var engine = BuildEngine(config);
            var now = new WDateTime(0);
            var goal = new PersistentGoal(Guid.NewGuid(), PersistentGoalKind.MasterCraft, GoalOrigin.Personality,
                0.5, 0.0, 0.0, now, now - WTimeSpan.FromHours(1));
            engine.RestoreState(new GoalState(new[] { goal }));

            var normalEngine = BuildEngine();
            normalEngine.RestoreState(new GoalState(new[] { goal }));

            var dt = WTimeSpan.FromHours(24);
            var ctx = BuildContext(new HumanId(Guid.NewGuid()));
            engine.Tick(now + dt, dt, ctx, new EventCollector());
            normalEngine.Tick(now + dt, dt, ctx, new EventCollector());

            var neglectedSalience = engine.State.Goals.First().Salience;
            var normalSalience = normalEngine.State.Goals.First().Salience;
            Assert.IsTrue(neglectedSalience < normalSalience,
                $"Neglected goal should decay faster: neglected={neglectedSalience:F4} normal={normalSalience:F4}");
        }

        [TestMethod]
        public void Tick_ActiveGoal_SalienceGrowsOnRelevantAction()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var now = new WDateTime(0);
            var goal = new PersistentGoal(Guid.NewGuid(), PersistentGoalKind.MasterCraft, GoalOrigin.Personality,
                0.3, 0.0, 0.0, now, now);
            engine.RestoreState(new GoalState(new[] { goal }));

            var before = engine.State.Goals.First().Salience;
            var outbox = new EventCollector();
            engine.Handle(new ActionCommitted(now, self, Work, WTimeSpan.FromHours(2)), BuildContext(self), outbox);

            var after = engine.State.Goals.First().Salience;
            Assert.IsTrue(after > before, $"Expected salience to grow: before={before}, after={after}");
        }

        // ── Section 3: Progress tracking ──────────────────────────────────────

        [TestMethod]
        public void Handle_WorkActionCommitted_MasterCraftProgressGrows()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var now = new WDateTime(0);
            var goal = new PersistentGoal(Guid.NewGuid(), PersistentGoalKind.MasterCraft, GoalOrigin.Personality,
                0.5, 0.0, 0.0, now, now);
            engine.RestoreState(new GoalState(new[] { goal }));

            engine.Handle(new ActionCommitted(now, self, Work, WTimeSpan.FromHours(2)), BuildContext(self), new EventCollector());

            var updated = engine.State.Goals.First();
            Assert.IsTrue(updated.Progress > 0.0, $"Progress should grow after Work: {updated.Progress}");
        }

        [TestMethod]
        public void Handle_InviteIntimacyCommitted_FindPartnerProgressGrows()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var now = new WDateTime(0);
            var goal = new PersistentGoal(Guid.NewGuid(), PersistentGoalKind.FindPartner, GoalOrigin.Personality,
                0.5, 0.0, 0.0, now, now);
            engine.RestoreState(new GoalState(new[] { goal }));

            engine.Handle(new ActionCommitted(now, self, InviteIntimacy, WTimeSpan.FromHours(1)), BuildContext(self), new EventCollector());

            var updated = engine.State.Goals.First();
            Assert.IsTrue(updated.Progress > 0.0, $"Progress should grow after InviteIntimacy: {updated.Progress}");
        }

        [TestMethod]
        public void Handle_ReachOutCommitted_RepairRelationshipProgressGrowsOnlyForTarget()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var target = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var now = new WDateTime(0);

            var goal = new PersistentGoal(Guid.NewGuid(), PersistentGoalKind.RepairRelationship, GoalOrigin.Event,
                0.5, 0.0, 0.0, now, now, TargetHuman: target);
            engine.RestoreState(new GoalState(new[] { goal }));

            // ReachOut to wrong person — should not progress
            engine.Handle(new ActionCommitted(now, self, ReachOut, WTimeSpan.FromHours(1), TargetHuman: other), BuildContext(self), new EventCollector());
            var afterWrong = engine.State.Goals.First().Progress;
            Assert.AreEqual(0.0, afterWrong, 0.001, "Wrong target should not advance goal");

            // ReachOut to correct target — should progress
            engine.Handle(new ActionCommitted(now, self, ReachOut, WTimeSpan.FromHours(1), TargetHuman: target), BuildContext(self), new EventCollector());
            var afterCorrect = engine.State.Goals.First().Progress;
            Assert.IsTrue(afterCorrect > 0.0, $"Correct target should advance goal: {afterCorrect}");
        }

        // ── Section 4: Resolutions ─────────────────────────────────────────────

        [TestMethod]
        public void Tick_ProgressReachesOne_GoalResolvesAsCompleted()
        {
            var engine = BuildEngine();
            var now = new WDateTime(0);
            var goal = new PersistentGoal(Guid.NewGuid(), PersistentGoalKind.MasterCraft, GoalOrigin.Personality,
                0.5, 0.99, 0.0, now, now);
            engine.RestoreState(new GoalState(new[] { goal }));

            var self = new HumanId(Guid.NewGuid());
            engine.Handle(new ActionCommitted(now, self, Work, WTimeSpan.FromHours(2)), BuildContext(self), new EventCollector());

            var resolved = engine.State.Goals.First();
            Assert.AreEqual(GoalResolution.Completed, resolved.Resolution);
        }

        [TestMethod]
        public void Tick_FrustrationExceedsThreshold_GoalResolvesAsAbandoned()
        {
            var config = new GoalConfig(AbandonmentFrustrationThreshold: 0.5);
            var engine = BuildEngine(config);
            var now = new WDateTime(0);
            var goal = new PersistentGoal(Guid.NewGuid(), PersistentGoalKind.FindPartner, GoalOrigin.Personality,
                0.5, 0.0, 0.51, now, now);
            engine.RestoreState(new GoalState(new[] { goal }));

            var dt = WTimeSpan.FromHours(1);
            engine.Tick(now + dt, dt, BuildContext(new HumanId(Guid.NewGuid())), new EventCollector());

            var resolved = engine.State.Goals.First();
            Assert.AreEqual(GoalResolution.Abandoned, resolved.Resolution);
        }

        [TestMethod]
        public void Tick_SalienceBelowFloor_GoalResolvesAsFaded()
        {
            var config = new GoalConfig(FadedSalienceThreshold: 0.5, SalienceDecayPerDay: 0.0);
            var engine = BuildEngine(config);
            var now = new WDateTime(0);
            var goal = new PersistentGoal(Guid.NewGuid(), PersistentGoalKind.FindMeaning, GoalOrigin.Personality,
                0.49, 0.0, 0.0, now, now);
            engine.RestoreState(new GoalState(new[] { goal }));

            var dt = WTimeSpan.FromHours(1);
            engine.Tick(now + dt, dt, BuildContext(new HumanId(Guid.NewGuid())), new EventCollector());

            var resolved = engine.State.Goals.First();
            Assert.AreEqual(GoalResolution.Faded, resolved.Resolution);
        }

        [TestMethod]
        public void Handle_GoalInjected_AddsNewActiveGoal()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var now = new WDateTime(0);
            var outbox = new EventCollector();

            engine.Handle(new GoalInjected(now, self, PersistentGoalKind.EscapeDanger, 0.7), BuildContext(self), outbox);

            Assert.IsTrue(engine.State.Active.Any(g => g.Kind == PersistentGoalKind.EscapeDanger));
            Assert.IsTrue(outbox.Drain().Any(e => e is GoalActivated));
        }

        // ── Section 5: Bias ────────────────────────────────────────────────────

        [TestMethod]
        public void GoalBehaviorModifier_MasterCraftGoal_BoostsWorkUtility()
        {
            var modifier = new GoalBehaviorModifier();
            var now = new WDateTime(0);
            var goal = new PersistentGoal(Guid.NewGuid(), PersistentGoalKind.MasterCraft, GoalOrigin.Personality,
                1.0, 0.0, 0.0, now, now);
            var goalState = new GoalState(new[] { goal });
            var context = BuildBehaviorContext(goalState);

            var candidates = new List<BehaviorCandidate>
            {
                new(Work, 50.0, WTimeSpan.FromHours(2), BehaviorDomain.Competence),
                new(Idle, 30.0, WTimeSpan.FromMinutes(30), BehaviorDomain.Physiological)
            };

            modifier.Modify(context, candidates);

            var workCandidate = candidates.First(c => c.Name == Work);
            var idleCandidate = candidates.First(c => c.Name == Idle);
            Assert.IsTrue(workCandidate.Utility > 50.0, $"Work utility should be boosted: {workCandidate.Utility}");
            Assert.AreEqual(30.0, idleCandidate.Utility, 0.001, "Idle utility should be unchanged");
        }

        [TestMethod]
        public void GoalBehaviorModifier_FindPartnerGoal_BoostsInviteIntimacyUtility()
        {
            var modifier = new GoalBehaviorModifier();
            var now = new WDateTime(0);
            var goal = new PersistentGoal(Guid.NewGuid(), PersistentGoalKind.FindPartner, GoalOrigin.Personality,
                1.0, 0.0, 0.0, now, now);
            var context = BuildBehaviorContext(new GoalState(new[] { goal }));

            var candidates = new List<BehaviorCandidate>
            {
                new(InviteIntimacy, 40.0, WTimeSpan.FromHours(1), BehaviorDomain.Social),
                new(Work, 50.0, WTimeSpan.FromHours(2), BehaviorDomain.Competence)
            };

            modifier.Modify(context, candidates);

            var intimacy = candidates.First(c => c.Name == InviteIntimacy);
            Assert.IsTrue(intimacy.Utility > 40.0, $"InviteIntimacy should be boosted: {intimacy.Utility}");
        }

        [TestMethod]
        public void GoalBehaviorModifier_TargetedGoal_OnlyBoostsMatchingTarget()
        {
            var modifier = new GoalBehaviorModifier();
            var now = new WDateTime(0);
            var target = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var goal = new PersistentGoal(Guid.NewGuid(), PersistentGoalKind.RepairRelationship, GoalOrigin.Event,
                1.0, 0.0, 0.0, now, now, TargetHuman: target);
            var context = BuildBehaviorContext(new GoalState(new[] { goal }));

            var targetCandidate = new BehaviorCandidate(ReachOut, 40.0, WTimeSpan.FromHours(1), BehaviorDomain.Social,
                SocialTargeting: new SocialTargetingData(target, SpeechAct.SmallTalk, 0.7, 0.8, 0.2));
            var otherCandidate = new BehaviorCandidate(ReachOut, 40.0, WTimeSpan.FromHours(1), BehaviorDomain.Social,
                SocialTargeting: new SocialTargetingData(other, SpeechAct.SmallTalk, 0.7, 0.8, 0.2));

            var candidates = new List<BehaviorCandidate> { targetCandidate, otherCandidate };
            modifier.Modify(context, candidates);

            Assert.IsTrue(candidates[0].Utility > 40.0, "Target candidate should be boosted");
            Assert.AreEqual(40.0, candidates[1].Utility, 0.001, "Non-target candidate should not be boosted");
        }

        [TestMethod]
        public void GoalBehaviorModifier_NoActiveGoals_NoBiasApplied()
        {
            var modifier = new GoalBehaviorModifier();
            var context = BuildBehaviorContext(GoalState.Empty);

            var candidates = new List<BehaviorCandidate>
            {
                new(Work, 50.0, WTimeSpan.FromHours(2), BehaviorDomain.Competence)
            };
            var before = candidates[0].Utility;

            modifier.Modify(context, candidates);

            Assert.AreEqual(before, candidates[0].Utility, 0.001, "No bias should be applied with empty goals");
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static DefaultGoalEngine BuildEngine(GoalConfig? config = null)
        {
            var cfg = Options.Create(config ?? new GoalConfig());
            var log = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger<DefaultGoalEngine>();
            return new DefaultGoalEngine(cfg, log);
        }

        private static Personality BuildPersonality(
            double openness = 0.5,
            double competence = 0.5,
            double affiliation = 0.5)
            => new(
                new BigFive(openness, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(affiliation, 0.5, 0.3, 0.4, competence, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

        private static IHumanContext BuildContext(HumanId self, GoalState? goals = null)
        {
            var personality = BuildPersonality();
            return new HumanContext
            {
                Id = self,
                Identity = new Identity(
                    new Name { Original = "A", Familiar = new[] { "A" } },
                    new Surname { Male = "B", Female = "B" },
                    WDateOnly.New(100, 1, 1)),
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = new EnginesSnapshot(
                    new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null),
                    new PsychologyState(0, 0.5, 0.5, 10, 0, DiscreteEmotion.Neutral),
                    new BehaviorState(10, 5, 5, 20, 50, 30, null),
                    new InteractionSurface("test", false, 0.2, 0.2, SurfaceKind.Social),
                    new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                    new MemoryIndex(new List<EpisodicMemory>()),
                    SemanticMemoryState.Empty,
                    Goals: goals),
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("GoalTests"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        private static BehaviorContext BuildBehaviorContext(GoalState goals)
        {
            var human = BuildContext(new HumanId(Guid.NewGuid()), goals);
            var state = new BehaviorState(10, 5, 5, 20, 50, 30, null);
            return new BehaviorContext(
                new WDateTime(0),
                WTimeSpan.FromHours(1),
                human,
                new EventCollector(),
                state,
                new BehaviorConfig(),
                new Dictionary<string, double>());
        }
    }
}
