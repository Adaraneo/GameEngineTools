// ToolLendingTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Needs;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using GameEngineTools.World.Work;
    using EngineTests.Utils;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>A tool that belongs to someone else can be used only after its owner has lent it.</summary>
    [TestClass]
    public sealed class ToolLendingTests : TestBase
    {
        private static readonly HumanId Owner = new(Guid.NewGuid());
        private static readonly HumanId Neighbour = new(Guid.NewGuid());
        private static readonly HumanId Stranger = new(Guid.NewGuid());

        private static (SqliteWorldDatabase Db, SqliteWorldObjectProvider Provider) NewWorld()
        {
            var db = new SqliteWorldDatabase(":memory:");
            WorldDatabaseSeeder.Initialize(db);
            db.ExecuteScript("INSERT INTO Locations (Id, DisplayName, Type) VALUES ('farm', 'Farm', 'Social');");
            var provider = new SqliteWorldObjectProvider(db);
            provider.AddObject(WorkObjects.Spawn(TestWork.Catalog, "spade", "spade_1", "farm", Owner));
            return (db, provider);
        }

        [TestMethod]
        public void ATool_IsUsableByItsOwnerAndByAnyoneWhenItBelongsToNobody_ButNotByOthers()
        {
            var shared = WorkObjects.Spawn(TestWork.Catalog, "spade", "s", "farm");
            var owned = WorkObjects.Spawn(TestWork.Catalog, "spade", "o", "farm", Owner);

            Assert.IsTrue(WorkObjects.CanUse(shared, Stranger), "Nobody's tool is anyone's.");
            Assert.IsTrue(WorkObjects.CanUse(owned, Owner));
            Assert.IsFalse(WorkObjects.CanUse(owned, Neighbour), "Someone else's tool is not for the taking.");
            Assert.IsFalse(WorkObjects.CanUse(owned, null));
            Assert.IsTrue(WorkObjects.CanUse(owned with { LentTo = Neighbour }, Neighbour), "Once lent, the borrower may use it.");
            Assert.IsFalse(WorkObjects.CanUse(owned with { LentTo = Neighbour }, Stranger), "A loan is to one person.");
        }

        [TestMethod]
        public void ToolsAreCarriable_WorkstationsAreNot()
        {
            Assert.IsTrue(WorkObjects.Spawn(TestWork.Catalog, "spade", "spade_1", "farm").IsPickable, "A spade can be picked up and carried to the field.");
            Assert.IsFalse(WorkObjects.Spawn(TestWork.Catalog, "mill", "mill_1", "farm").IsPickable);
        }

        [TestMethod]
        public void TakingATool_IsRefusedForSomeoneElsesTool_UntilItIsLent()
        {
            var policy = new GameEngineTools.Characters.Engines.Objects.DefaultObjectInteractionPolicy();
            var place = new GameEngineTools.World.Location.LocationDescriptor(
                Id: "farm", DisplayName: "farm", Type: GameEngineTools.World.Location.LocationType.Public, BaseNoise: 0.1, NoisePerPerson: 0.01, Capacity: 20, AllowsPrivacy: false);
            var spade = WorkObjects.Spawn(TestWork.Catalog, "spade", "spade_1", "farm", Owner);
            IHumanContext As(HumanId id) => BehaviorComponentTestFactory.Context(selfId: id).HumanContext;

            Assert.IsTrue(policy.Evaluate(As(Owner), spade, ObjectInteractionKind.Take, place).IsAllowed, "The owner takes their own tool.");
            var refused = policy.Evaluate(As(Neighbour), spade, ObjectInteractionKind.Take, place);
            Assert.IsFalse(refused.IsAllowed, "Someone else's tool is not taken without a loan.");
            Assert.IsTrue(refused.IsSocial);
            Assert.IsTrue(policy.Evaluate(As(Neighbour), spade with { LentTo = Neighbour }, ObjectInteractionKind.Take, place).IsAllowed, "Lent to them: they may take it.");
            Assert.IsTrue(policy.Evaluate(As(Stranger), WorkObjects.Spawn(TestWork.Catalog, "spade", "shared", "farm"), ObjectInteractionKind.Take, place).IsAllowed, "A tool that belongs to nobody is free to take.");
        }

        [TestMethod]
        public void OnlyTheOwnerLends_OnceAtATime_AndEitherSideEndsTheLoan_AndItSurvivesTheDatabase()
        {
            var (db, provider) = NewWorld();
            using (db)
            {
                Assert.IsFalse(ToolLending.Lend(provider, "spade_1", Neighbour, Stranger), "Only the owner can lend.");
                Assert.IsFalse(ToolLending.Lend(provider, "spade_1", Owner, Owner), "Lending to oneself is no loan.");
                Assert.IsFalse(ToolLending.Lend(provider, "missing", Owner, Neighbour));

                Assert.IsTrue(ToolLending.Lend(provider, "spade_1", Owner, Neighbour));
                Assert.AreEqual(Neighbour, provider.FindObject("spade_1")!.LentTo, "The loan is kept in the world database.");
                Assert.AreEqual(Owner, provider.FindObject("spade_1")!.OwnerId, "Lending does not change the owner.");
                Assert.IsFalse(ToolLending.Lend(provider, "spade_1", Owner, Stranger), "A lent tool is not lent twice.");

                Assert.IsFalse(ToolLending.Return(provider, "spade_1", Stranger), "Neither owner nor borrower: no say.");
                Assert.IsTrue(ToolLending.Return(provider, "spade_1", Neighbour));
                Assert.IsNull(provider.FindObject("spade_1")!.LentTo);
                Assert.IsFalse(ToolLending.Return(provider, "spade_1", Owner), "Nothing to take back.");

                Assert.IsTrue(ToolLending.Lend(provider, "spade_1", Owner, Stranger));
                Assert.IsTrue(ToolLending.Return(provider, "spade_1", Owner), "The owner can ask for it back.");
            }
        }

        [TestMethod]
        public void FieldWork_NeedsAToolTheCharacterMayUse_AnOwnedSpadeCountsOnlyWhenLent()
        {
            var engine = new FieldWorkNeedEngine(catalog: TestWork.Catalog);
            var self = Neighbour;
            var field = new WorldObject { Id = "field_1", DisplayName = "Pole", Category = WorldObjectCategory.Ambient, LocationId = Loc, ItemTypeId = "grain", Field = new GameEngineTools.World.Objects.Crops.FieldState(GameEngineTools.World.Objects.Crops.FieldStage.Fallow, 1.0) };
            var spade = WorkObjects.Spawn(TestWork.Catalog, "spade", "spade_1", Loc, Owner);

            BehaviorContext Ctx(WorldObject tool)
            {
                var ctx = BehaviorComponentTestFactory.Context(selfId: self, hunger: 20, thirst: 5, now: new WDateTime(0));
                var human = (HumanContext)ctx.HumanContext;
                human.Snapshot = human.Snapshot with
                {
                    AmbientTemperature = 15.0,
                    Celestial = new GameEngineTools.World.Core.Astro.CelestialContext(0.5, 12.0, 6.0, 18.0, 12.0, 0.5, 0.0, true, 15.0, TemperatureTrendPerDay: 0.1),
                };
                return ctx with { AvailableObjects = new List<WorldObject> { field, tool }, HeldObjects = new List<WorldObject>() };
            }

            Assert.AreEqual(0, engine.Evaluate(Ctx(spade)).Candidates.Count, "The spade belongs to someone else: no ploughing.");
            Assert.AreEqual(Plough, engine.Evaluate(Ctx(spade with { LentTo = self })).Candidates.Single().Name, "Lent to this character: they plough.");
            Assert.AreEqual(0, engine.Evaluate(Ctx(spade with { LentTo = Stranger })).Candidates.Count, "Lent to somebody else.");
        }

        private const string Loc = "test"; // BehaviorComponentTestFactory surface location
    }
}
