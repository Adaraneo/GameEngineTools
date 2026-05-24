// ObjectAffordanceGatingEngineTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using static GameEngineTools.Characters.Engines.ActionNames;

    [TestClass]
    public class ObjectAffordanceGatingEngineTests : TestBase
    {
        private ObjectAffordanceGatingEngine _engine = null!;

        [TestInitialize]
        public void Setup()
        {
            _engine = new ObjectAffordanceGatingEngine();
        }

        [TestMethod]
        public void Modify_EatWithoutFoodAvailable_RemoveEatCandidate()
        {
            var candidates = new List<BehaviorCandidate>
            {
                new BehaviorCandidate(Eat, 50.0, WTimeSpan.FromMinutes(30), BehaviorDomain.Physiological),
                new BehaviorCandidate(Idle, 30.0, WTimeSpan.FromMinutes(60), BehaviorDomain.Physiological)
            };
            var objects = new List<WorldObject> { MakeMockObject("water_jug", WorldObjectCategory.Drink) };

            _engine.Modify(BuildContext(objects), candidates);

            Assert.IsFalse(candidates.Any(c => c.Name == Eat));
            Assert.IsTrue(candidates.Any(c => c.Name == Idle));
        }

        [TestMethod]
        public void Modify_EatWithFoodAvailable_KeepEatCandidate()
        {
            var candidates = new List<BehaviorCandidate>
            {
                new BehaviorCandidate(Eat, 50.0, WTimeSpan.FromMinutes(30), BehaviorDomain.Physiological)
            };
            var objects = new List<WorldObject> { MakeMockObject("bread", WorldObjectCategory.Food) };

            _engine.Modify(BuildContext(objects), candidates);

            Assert.IsTrue(candidates.Any(c => c.Name == Eat));
            Assert.AreEqual(50.0, candidates.First(c => c.Name == Eat).Utility);
        }

        [TestMethod]
        public void Modify_WorkWithoutToolAvailable_RemoveWorkCandidate()
        {
            var candidates = new List<BehaviorCandidate>
            {
                new BehaviorCandidate(Work, 80.0, WTimeSpan.FromMinutes(60), BehaviorDomain.Competence),
                new BehaviorCandidate(Idle, 30.0, WTimeSpan.FromMinutes(60), BehaviorDomain.Physiological)
            };

            _engine.Modify(BuildContext(new List<WorldObject> { }), candidates);

            Assert.IsFalse(candidates.Any(c => c.Name == Work));
            Assert.IsTrue(candidates.Any(c => c.Name == Idle));
        }

        [TestMethod]
        public void Modify_SelfCareWithoutFurnitureAvailable_RemoveSelfCareCandidate()
        {
            var candidates = new List<BehaviorCandidate>
            {
                new BehaviorCandidate(SelfCare, 45.0, WTimeSpan.FromMinutes(15), BehaviorDomain.Physiological),
                new BehaviorCandidate(Idle, 30.0, WTimeSpan.FromMinutes(60), BehaviorDomain.Physiological)
            };

            _engine.Modify(BuildContext(new List<WorldObject> { }), candidates);

            Assert.IsFalse(candidates.Any(c => c.Name == SelfCare));
        }

        [TestMethod]
        public void Modify_SleepWithoutShelterAvailable_ZeroUtilityForSleepCandidate()
        {
            var candidates = new List<BehaviorCandidate>
            {
                new BehaviorCandidate(Sleep, 70.0, WTimeSpan.FromMinutes(480), BehaviorDomain.Physiological),
                new BehaviorCandidate(Idle, 30.0, WTimeSpan.FromMinutes(60), BehaviorDomain.Physiological)
            };

            _engine.Modify(BuildContext(new List<WorldObject> { }), candidates);

            var sleepCandidate = candidates.FirstOrDefault(c => c.Name == Sleep);
            Assert.IsNotNull(sleepCandidate);
            Assert.AreEqual(0.0, sleepCandidate.Utility, "SOFT gate: Sleep should be zeroed when Shelter unavailable");
        }

        [TestMethod]
        public void Modify_NoObjectsAvailable_RemoveAllHardGateCandidates()
        {
            var candidates = new List<BehaviorCandidate>
            {
                new BehaviorCandidate(Eat, 50.0, WTimeSpan.FromMinutes(30), BehaviorDomain.Physiological),
                new BehaviorCandidate(Drink, 40.0, WTimeSpan.FromMinutes(10), BehaviorDomain.Physiological),
                new BehaviorCandidate(Work, 80.0, WTimeSpan.FromMinutes(60), BehaviorDomain.Competence),
                new BehaviorCandidate(Create, 60.0, WTimeSpan.FromMinutes(45), BehaviorDomain.Competence),
                new BehaviorCandidate(SelfCare, 45.0, WTimeSpan.FromMinutes(15), BehaviorDomain.Physiological),
                new BehaviorCandidate(Idle, 30.0, WTimeSpan.FromMinutes(60), BehaviorDomain.Physiological),
                new BehaviorCandidate(ReachOut, 35.0, WTimeSpan.FromMinutes(20), BehaviorDomain.Social)
            };

            _engine.Modify(BuildContext(new List<WorldObject>()), candidates);

            Assert.IsFalse(candidates.Any(c => c.Name == Eat));
            Assert.IsFalse(candidates.Any(c => c.Name == Drink));
            Assert.IsFalse(candidates.Any(c => c.Name == Work));
            Assert.IsFalse(candidates.Any(c => c.Name == Create));
            Assert.IsFalse(candidates.Any(c => c.Name == SelfCare));
            Assert.IsTrue(candidates.Any(c => c.Name == Idle));
            Assert.IsTrue(candidates.Any(c => c.Name == ReachOut));
        }

        [TestMethod]
        public void Modify_UnrequiredActions_Unaffected()
        {
            var candidates = new List<BehaviorCandidate>
            {
                new BehaviorCandidate(ReachOut, 35.0, WTimeSpan.FromMinutes(20), BehaviorDomain.Social),
                new BehaviorCandidate(InviteIntimacy, 25.0, WTimeSpan.FromMinutes(30), BehaviorDomain.Social),
                new BehaviorCandidate(Idle, 30.0, WTimeSpan.FromMinutes(60), BehaviorDomain.Physiological)
            };

            _engine.Modify(BuildContext(new List<WorldObject> { }), candidates);

            Assert.AreEqual(3, candidates.Count);
        }

        private static WorldObject MakeMockObject(string id, WorldObjectCategory category)
        {
            return new WorldObject
            {
                Id = id,
                DisplayName = id,
                Category = category,
                LocationId = "test",
                IsAvailable = true,
                HeatSignature = 0,
                AmbientNoise = 0,
                BlocksLineOfSight = false,
                Affordances = [],
                IsPickable = false,
                WeightGrams = 0,
                ItemKind = PickupItemKind.None,
                HeldBy = null,
                ConsumedAt = null,
                Respawns = false,
                RespawnMinutes = 0
            };
        }

        private BehaviorContext BuildContext(IReadOnlyList<WorldObject>? availableObjects)
        {
            var behaviorState = new BehaviorState(
                NeedRest: 50, NeedFood: 50, NeedWater: 25, NeedBelonging: 50,
                NeedCompetence: 50, NeedIntimacy: 30, CurrentPlan: null,
                Cooldowns: new Dictionary<string, double>());

            var humanCtx = new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = null!,
                Snapshot = null!,
                Random = new AlwaysFalseRandom(),
                Logger = BuildLoggerFactory().CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };

            return new BehaviorContext(
                WDateTime.New(WDateOnly.New(100, 1, 1)),
                WTimeSpan.FromHours(1),
                humanCtx,
                new EventCollector(),
                behaviorState,
                new BehaviorConfig(),
                new Dictionary<string, double>(),
                null, null, availableObjects);
        }

        private static ILoggerFactory BuildLoggerFactory()
            => LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

        private sealed class AlwaysFalseRandom : IRandomSource
        {
            public int Next(int min, int max) => min;

            public double NextUnit() => 0.0;

            public bool Chance(double p) => false;
        }

        private sealed class NullEventBus : IEventBus
        {
            public void Publish(IDomainEvent @event)
            { }

            public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class, IDomainEvent
                => new NullDisposable();
        }

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

        private sealed class EventCollector : IEventCollector
        {
            private readonly List<IDomainEvent> _events = new();
            public IReadOnlyList<IDomainEvent> Events => _events.AsReadOnly();

            public void Add(IDomainEvent @event) => _events.Add(@event);

            public IReadOnlyList<IDomainEvent> Drain()
            {
                var result = _events.ToList();
                _events.Clear();
                return result;
            }
        }
    }
}
