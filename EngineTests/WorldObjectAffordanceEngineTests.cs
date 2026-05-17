// WorldObjectAffordanceEngineTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Unit tests for <see cref="WorldObjectAffordanceEngine"/> and
    /// <see cref="AffordanceCandidateMap"/>.
    /// </summary>
    /// <remarks>
    /// KEY LESSON — what is tested here:<br/>
    /// The modifier reads <see cref="BehaviorContext.AvailableObjects"/> and nudges
    /// candidate utility. It does NOT resolve objects from the provider — that happens
    /// upstream in DefaultBehaviorEngine.Tick(). These tests cover only the modifier logic.
    /// </remarks>
    [TestClass]
    public class WorldObjectAffordanceEngineTests : TestBase
    {
        #region Private state

        private static readonly WDateTime Now = new WDateTime(10000);

        #endregion Private state

        #region Tests — no-op when AvailableObjects is null

        /// <summary>
        /// Engine must be a pure no-op when no objects are provided.
        /// This is the common case in headless / unit-test simulations without a world provider.
        /// </summary>
        [TestMethod]
        public void Modify_NullObjects_LeavesUtilityUnchanged()
        {
            var sut = new WorldObjectAffordanceEngine();
            var context = BuildContext(availableObjects: null);
            var candidates = BuildCandidates(Eat, ReachOut, Work);

            sut.Modify(context, candidates);

            foreach (var c in candidates)
                Assert.AreEqual(30.0, c.Utility, 0.001,
                    $"Candidate '{c.Name}' should be unchanged when AvailableObjects is null.");
        }

        /// <summary>
        /// Engine must be a no-op for an empty object list.
        /// </summary>
        [TestMethod]
        public void Modify_EmptyObjectList_LeavesUtilityUnchanged()
        {
            var sut = new WorldObjectAffordanceEngine();
            var context = BuildContext(availableObjects: new List<WorldObject>());
            var candidates = BuildCandidates(Eat, ReachOut);

            sut.Modify(context, candidates);

            foreach (var c in candidates)
                Assert.AreEqual(30.0, c.Utility, 0.001,
                    $"Candidate '{c.Name}' should be unchanged for empty object list.");
        }

        #endregion Tests — no-op when AvailableObjects is null

        #region Tests — Hunger affordance

        /// <summary>
        /// A food object must boost Eat more when the character is starving (NeedFood=100)
        /// than when they are well-fed (NeedFood=20).
        /// Need-scaling: delta = satisfaction × cap × (needFood / 100).
        /// </summary>
        [TestMethod]
        public void Modify_FoodObject_BoostsEat_ScaledByNeedFood()
        {
            var sut = new WorldObjectAffordanceEngine();
            var obj = MakeSingleAffordanceObject(AffordanceType.Hunger, satisfaction: 1.0);

            var candidatesStarving = BuildCandidates(Eat);
            sut.Modify(BuildContext([obj], needFood: 100), candidatesStarving);

            var candidatesWellFed = BuildCandidates(Eat);
            sut.Modify(BuildContext([obj], needFood: 20), candidatesWellFed);

            // Both should be boosted.
            Assert.IsTrue(candidatesStarving[0].Utility > 30.0,
                "Starving character must see a utility increase from food object.");
            Assert.IsTrue(candidatesWellFed[0].Utility > 30.0,
                "Well-fed character still has NeedFood=20, so delta must be > 0.");

            // Starving character must benefit more.
            Assert.IsTrue(candidatesStarving[0].Utility > candidatesWellFed[0].Utility,
                "Starving character must benefit more from food than a well-fed one.");
        }

        /// <summary>
        /// A Hunger affordance must not modify unrelated candidates.
        /// </summary>
        [TestMethod]
        public void Modify_FoodObject_DoesNotBoostWorkOrReachOut()
        {
            var sut = new WorldObjectAffordanceEngine();
            var obj = MakeSingleAffordanceObject(AffordanceType.Hunger, satisfaction: 1.0);
            var candidates = BuildCandidates(Work, Create, ReachOut);

            sut.Modify(BuildContext([obj], needFood: 100), candidates);

            foreach (var c in candidates)
                Assert.AreEqual(30.0, c.Utility, 0.001,
                    $"Food affordance must not affect candidate '{c.Name}'.");
        }

        #endregion Tests — Hunger affordance

        #region Tests — StressRaise penalty

        /// <summary>
        /// A hazard (StressRaise) must penalise ReachOut, Work, and Create,
        /// but must leave Eat and Drink untouched.
        /// Biological regulation must be stress-resistant.
        /// </summary>
        [TestMethod]
        public void Modify_Hazard_PenalisesSensitiveActions_SparesBiologicalRegulation()
        {
            var sut = new WorldObjectAffordanceEngine();
            var hazard = MakeSingleAffordanceObject(AffordanceType.StressRaise, satisfaction: 0.8);

            var candidates = BuildCandidates(ReachOut, Work, Create, Eat, Drink);
            sut.Modify(BuildContext([hazard]), candidates);

            // Sensitive actions must be penalised.
            var reachOut = candidates.First(c => c.Name == ReachOut);
            var work = candidates.First(c => c.Name == Work);
            var create = candidates.First(c => c.Name == Create);

            Assert.IsTrue(reachOut.Utility < 30.0, $"ReachOut must be penalised. Actual: {reachOut.Utility}");
            Assert.IsTrue(work.Utility < 30.0, $"Work must be penalised. Actual: {work.Utility}");
            Assert.IsTrue(create.Utility < 30.0, $"Create must be penalised. Actual: {create.Utility}");

            // Biological regulation must be immune.
            var eat = candidates.First(c => c.Name == Eat);
            var drink = candidates.First(c => c.Name == Drink);

            Assert.AreEqual(30.0, eat.Utility, 0.001, "Eat must NOT be penalised by a hazard.");
            Assert.AreEqual(30.0, drink.Utility, 0.001, "Drink must NOT be penalised by a hazard.");
        }

        #endregion Tests — StressRaise penalty

        #region Tests — delta cap

        /// <summary>
        /// Many food objects in one room must not push Eat beyond base + MaxTotalDeltaPerCandidate (20).
        /// </summary>
        [TestMethod]
        public void Modify_ManyFoodObjects_DeltaIsCappedAt20()
        {
            var sut = new WorldObjectAffordanceEngine();

            // Ten food objects at full satisfaction.
            var objects = Enumerable
                .Range(0, 10)
                .Select(i => new WorldObject
                {
                    Id = $"food_{i}",
                    DisplayName = "Food",
                    Category = WorldObjectCategory.Food,
                    LocationId = "loc",
                    IsAvailable = true,
                    Affordances = ImmutableArray.Create(
                        new WorldObjectAffordance(AffordanceType.Hunger, 1.0))
                })
                .ToList<WorldObject>();

            var candidates = BuildCandidates(Eat);
            sut.Modify(BuildContext(objects, needFood: 100), candidates);

            // 30 (base) + 20 (cap) = 50 maximum.
            Assert.IsTrue(
                candidates[0].Utility <= 30.0 + 20.0 + 0.001,
                $"Utility must be capped at base+20. Actual: {candidates[0].Utility:F4}");
        }

        #endregion Tests — delta cap

        #region Tests — AffordanceCandidateMap coverage

        /// <summary>
        /// Every AffordanceType defined in the enum must have an entry in the map.
        /// This test protects against silent regressions when a new type is added.
        /// If this test fails, open AffordanceCandidateMap and add the new type.
        /// </summary>
        [TestMethod]
        public void AffordanceCandidateMap_CoversAllAffordanceTypes()
        {
            foreach (AffordanceType type in Enum.GetValues<AffordanceType>())
            {
                // Must not throw and must return non-null.
                var targets = AffordanceCandidateMap.TargetsFor(type);

                Assert.IsNotNull(targets,
                    $"AffordanceCandidateMap.TargetsFor must return non-null for AffordanceType.{type}.");
            }
        }

        #endregion Tests — AffordanceCandidateMap coverage

        #region Private factory helpers

        /// <summary>
        /// Builds a minimal <see cref="BehaviorContext"/> with the given objects and need values.
        /// Uses the same construction pattern as <c>SleepTests.BuildFakeContext</c> and
        /// <c>MemoryBehaviorTests.BuildContext</c>.
        /// </summary>
        private static BehaviorContext BuildContext(
            IReadOnlyList<WorldObject>? availableObjects,
            double needFood = 50,
            double needRest = 50,
            double needBel = 50,
            double needComp = 50)
        {
            var physio = new PhysiologyState(
                Energy: 80,
                SleepDebtHours: 0,
                Hunger: needFood,   // engine reads from physio, but we also set BehaviorState below
                Thirst: 25,
                Pain: 0,
                ImmuneLoad: 0,
                BodyTempDelta: 0,
                Cycle: null);

            var psych = new PsychologyState(
                Valence: 0.0,
                Arousal: 0.5,
                Dominance: 0.5,
                Stress: 0,
                CognitiveLoad: 0,
                DominantEmotion: DiscreteEmotion.Neutral);

            // Pre-set BehaviorState with the requested need values so the modifier
            // can read them directly from context.State without a full engine Tick().
            var behaviorState = new BehaviorState(
                NeedRest: needRest,
                NeedFood: needFood,
                NeedWater: 25,
                NeedBelonging: needBel,
                NeedCompetence: needComp,
                NeedIntimacy: 30,
                CurrentPlan: null,
                Cooldowns: new Dictionary<string, double>());

            var snapshot = new EnginesSnapshot(
                physio, psych, behaviorState,
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(
                    new System.Collections.Generic.List<EpisodicMemory>()));

            var personality = new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(
                    Affiliation: 0.5, Achievement: 0.5, Power: 0.3,
                    Altruism: 0.4, Competence: 0.5, Autonomy: 0.5,
                    Curiosity: 0.5, Rest: 0.6, Sexuality: 0.4),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral);

            var humanCtx = new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = personality,
                Snapshot = snapshot,
                Random = new AlwaysFalseRandom(),
                Logger = BuildLoggerFactory().CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };

            return new BehaviorContext(
                Now: Now,
                Dt: WTimeSpan.FromHours(1),
                HumanContext: humanCtx,
                Outbox: new EventCollector(),
                State: behaviorState,
                Config: new BehaviorConfig(),
                Cooldowns: new Dictionary<string, double>(),
                DecisionWorkingSets: null,
                HabitApplicabilityModulator: null,
                AvailableObjects: availableObjects);
        }

        /// <summary>
        /// Builds a list of candidates, each with a base utility of 30.
        /// </summary>
        private static List<BehaviorCandidate> BuildCandidates(params string[] names)
            => names
                .Select(n => new BehaviorCandidate(n, 30.0, WTimeSpan.FromHours(1), BehaviorDomain.Physiological))
                .ToList();

        /// <summary>
        /// Builds a minimal <see cref="WorldObject"/> with exactly one affordance.
        /// LocationId is arbitrary — the modifier only reads context.AvailableObjects,
        /// not the provider.
        /// </summary>
        private static WorldObject MakeSingleAffordanceObject(AffordanceType type, double satisfaction)
            => new()
            {
                Id = "test_obj",
                DisplayName = "Test Object",
                Category = WorldObjectCategory.Furniture,
                LocationId = "test_location",
                IsAvailable = true,
                Affordances = ImmutableArray.Create(new WorldObjectAffordance(type, satisfaction))
            };

        private static ILoggerFactory BuildLoggerFactory()
            => LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

        #endregion Private factory helpers

        #region Stub implementations

        /// <summary>IRandomSource that always returns the minimum — disables random events.</summary>
        private sealed class AlwaysFalseRandom : IRandomSource
        {
            public int Next(int min, int max) => min;

            public double NextUnit() => 0.0;

            public bool Chance(double p) => false;
        }

        /// <summary>IEventBus that discards all published events.</summary>
        private sealed class NullEventBus : IEventBus
        {
            public void Publish(IDomainEvent @event)
            { }

            public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class, IDomainEvent
                => new NullDisposable();
        }

        /// <summary>IScheduler that does nothing.</summary>
        private sealed class NullScheduler : IScheduler
        {
            public ScheduledId ScheduleAt(WDateTime when, ScheduledAction action, string? tag = null)
                => new ScheduledId(Guid.NewGuid());

            public ScheduledId ScheduleAfter(WDateTime now, WTimeSpan delay, ScheduledAction action, string? tag = null)
                => new ScheduledId(Guid.NewGuid());

            public bool Cancel(ScheduledId id) => false;

            public IEnumerable<(ScheduledId id, ScheduledAction action)> Due(WDateTime now)
                => Array.Empty<(ScheduledId, ScheduledAction)>();
        }

        private sealed class NullDisposable : IDisposable
        {
            public void Dispose()
            { }
        }

        #endregion Stub implementations
    }
}
