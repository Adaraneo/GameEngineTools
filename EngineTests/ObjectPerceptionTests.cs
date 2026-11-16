// ObjectPerceptionTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Objects;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>A character remembers the tools and workstations they saw, with their type, and forgets those they see are gone.</summary>
    [TestClass]
    public sealed class ObjectPerceptionTests : TestBase
    {
        private static readonly HumanId Self = new(Guid.NewGuid());

        private static DefaultMemoryEngine BuildEngine()
        {
            var sp = new ServiceCollection()
                .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
                .AddSingleton(Options.Create(new MemoryConfig()))
                .BuildServiceProvider();
            return new DefaultMemoryEngine(sp.GetRequiredService<IOptions<MemoryConfig>>(), sp.GetRequiredService<ILoggerFactory>());
        }

        private static IHumanContext BuildContext()
        {
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), AttachmentProfile.Secure, CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4), Sociosexuality.Intermediate, Chronotype.Neutral);
            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                new PsychologyState(0.1, 0.4, 0.5, 10, 10, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.2, 0.2, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));
            return new HumanContext
            {
                Id = Self,
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler(),
            };
        }

        [TestMethod]
        public void LookingAround_RemembersEachToolAndWorkstationWithItsType_AndForgetsWhatIsGone()
        {
            var engine = BuildEngine();
            var ctx = BuildContext();
            var now = new WDateTime(0);
            var seen = new List<PerceivedObject>
            {
                new("spade_1", "spade", WorldObjectCategory.Tool),
                new("mill_1", "mill", WorldObjectCategory.Ambient),
            };

            engine.Handle(new ObjectsPerceived(now, Self, "farm", seen), ctx, new EventCollector());

            var facts = engine.State.KnownObjects;
            Assert.AreEqual(2, facts.Count);
            Assert.AreEqual("spade", facts.Single(f => f.ObjectId == "spade_1").TypeId);
            Assert.AreEqual("farm", facts.Single(f => f.ObjectId == "mill_1").LocationId);

            engine.Handle(new ObjectsPerceived(now, Self, "farm", new[] { seen[1] }), ctx, new EventCollector());

            Assert.IsFalse(engine.State.KnownObjects.Any(f => f.ObjectId == "spade_1"), "The spade is no longer in sight at the farm: it was taken away.");
            Assert.IsTrue(engine.State.KnownObjects.Any(f => f.ObjectId == "mill_1"));
        }

        [TestMethod]
        public void TheBehaviorEngineLooksAround_OnArrival_NotesToolsAndWorkstations_AndNotAgainAtOnce()
        {
            using var db = new GameEngineTools.World.Data.SqliteWorldDatabase(":memory:");
            GameEngineTools.World.Data.WorldDatabaseSeeder.Initialize(db);
            db.ExecuteScript("INSERT INTO Locations (Id, DisplayName, Type) VALUES ('test', 'Test', 'Social');");
            var provider = new SqliteWorldObjectProvider(db);
            provider.AddObject(GameEngineTools.World.Work.WorkObjects.Spawn(EngineTests.Utils.TestWork.Catalog, "spade", "spade_1", "test"));
            provider.AddObject(GameEngineTools.World.Work.WorkObjects.Spawn(EngineTests.Utils.TestWork.Catalog, "mill", "mill_1", "test"));
            provider.AddObject(new WorldObject { Id = "stone_1", DisplayName = "Kámen", Category = WorldObjectCategory.Ambient, LocationId = "test", IsAvailable = true });
            var engine = new DefaultBehaviorEngine(
                Options.Create(new BehaviorConfig()),
                Options.Create(new GameEngineTools.Characters.Engines.Sleep.SleepConfig()),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                EngineTests.Utils.TestWork.Catalog,
                new EngineTests.Utils.PermissiveDevelopmentPolicy(),
                objectProvider: provider);
            var human = BehaviorComponentTestFactory.Context(selfId: Self, hunger: 10, thirst: 5, now: new WDateTime(0)).HumanContext;

            var first = new EventCollector();
            engine.Tick(new WDateTime(0), WTimeSpan.FromMinutes(10), human, first);
            var second = new EventCollector();
            engine.Tick(new WDateTime(0) + WTimeSpan.FromMinutes(10), WTimeSpan.FromMinutes(10), human, second);

            var seen = first.Drain().OfType<ObjectsPerceived>().Single();
            CollectionAssert.AreEquivalent(new[] { "spade_1", "mill_1" }, seen.Objects.Select(o => o.ObjectId).ToArray(), "Tools and workstations are noted; a stone is not.");
            Assert.AreEqual("test", seen.LocationId);
            Assert.IsFalse(second.Drain().OfType<ObjectsPerceived>().Any(), "The same place is not looked at again ten minutes later.");
        }

        [TestMethod]
        public void SeeingAPlaceLeavesWhatIsRememberedElsewhereAlone_AndOthersPerceptionsAreNotMine()
        {
            var engine = BuildEngine();
            var ctx = BuildContext();
            var now = new WDateTime(0);
            engine.Handle(new ObjectsPerceived(now, Self, "farm", new[] { new PerceivedObject("spade_1", "spade", WorldObjectCategory.Tool) }), ctx, new EventCollector());

            engine.Handle(new ObjectsPerceived(now, Self, "tavern", Array.Empty<PerceivedObject>()), ctx, new EventCollector());
            engine.Handle(new ObjectsPerceived(now, new HumanId(Guid.NewGuid()), "farm", new[] { new PerceivedObject("sickle_9", "sickle", WorldObjectCategory.Tool) }), ctx, new EventCollector());

            Assert.IsTrue(engine.State.KnownObjects.Any(f => f.ObjectId == "spade_1" && f.LocationId == "farm"));
            Assert.IsFalse(engine.State.KnownObjects.Any(f => f.ObjectId == "sickle_9"));
        }

        [TestMethod]
        public void TakingATool_KeepsItsType_AndDroppingItElsewhereMovesTheMemory()
        {
            var engine = BuildEngine();
            var ctx = BuildContext();
            var now = new WDateTime(0);
            engine.Handle(new ObjectsPerceived(now, Self, "farm", new[] { new PerceivedObject("spade_1", "spade", WorldObjectCategory.Tool) }), ctx, new EventCollector());

            engine.Handle(new ObjectTaken(now, Self, "spade_1", "farm", "spade"), ctx, new EventCollector());
            Assert.AreEqual(string.Empty, engine.State.KnownObjects.Single().LocationId, "Held: no place.");
            Assert.AreEqual("spade", engine.State.KnownObjects.Single().TypeId);

            engine.Handle(new ObjectDropped(now, Self, "spade_1", "home", "spade"), ctx, new EventCollector());
            Assert.AreEqual("home", engine.State.KnownObjects.Single().LocationId);
            Assert.AreEqual(WorldObjectCategory.Tool, engine.State.KnownObjects.Single().Category, "What it is stays known.");
        }
    }
}
