// ToolCarryNeedEngineTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using EngineTests.Utils;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Needs;
    using GameEngineTools.Characters.Engines.Schedule;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Objects.Crops;
    using GameEngineTools.World.Utils.Time;
    using GameEngineTools.World.Work;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>A farmer on shift picks up a tool he may use when it lies away from any field, and not when he already carries one or stands at a field.</summary>
    [TestClass]
    public sealed class ToolCarryNeedEngineTests : TestBase
    {
        private const string Loc = "test"; // BehaviorComponentTestFactory surface location

        private static readonly HumanId Self = new(Guid.NewGuid());
        private static readonly HumanId Owner = new(Guid.NewGuid());

        private static BehaviorContext Ctx(string? occupation, IEnumerable<WorldObject> here, IEnumerable<WorldObject>? held = null)
        {
            var ctx = BehaviorComponentTestFactory.Context(selfId: Self, hunger: 10, thirst: 5, now: new WDateTime(0));
            if (occupation is not null)
            {
                var slot = new ScheduleSlot("work", 5, Work);
                ((HumanContext)ctx.HumanContext).Snapshot = ctx.HumanContext.Snapshot with { Schedule = new DailyScheduleState(new[] { slot }, slot, 0, occupation) };
            }

            return ctx with { AvailableObjects = here.ToList(), HeldObjects = (held ?? Array.Empty<WorldObject>()).ToList() };
        }

        private static WorldObject Spade(string id, HumanId? owner = null)
        {
            return WorkObjects.Spawn(TestWork.Catalog, "spade", id, Loc, owner);
        }

        [TestMethod]
        public void AFarmerOnShift_TakesAUsableSpadeThatLiesAwayFromTheFields()
        {
            var engine = new ToolCarryNeedEngine(TestWork.Catalog);

            var take = engine.Evaluate(Ctx("farmer", new[] { Spade("spade_1") })).Candidates.Single();

            Assert.AreEqual(InteractWithObject, take.Name);
            Assert.AreEqual(ObjectInteractionKind.Take, take.ObjectInteraction!.Kind);
            Assert.AreEqual("spade_1", take.ObjectInteraction.ObjectId);
        }

        [TestMethod]
        public void ASpadeOfSomeoneElse_IsTakenOnlyOnceItIsLentToTheFarmer()
        {
            var engine = new ToolCarryNeedEngine(TestWork.Catalog);

            Assert.AreEqual(0, engine.Evaluate(Ctx("farmer", new[] { Spade("theirs", Owner) })).Candidates.Count, "Not his to take.");
            Assert.AreEqual(1, engine.Evaluate(Ctx("farmer", new[] { Spade("theirs", Owner) with { LentTo = Self } })).Candidates.Count, "Lent to him: he takes it.");
        }

        [TestMethod]
        public void NothingIsTaken_AtAField_OffShift_ByAnOccupationWithoutFieldTasks_OrWhenOneIsAlreadyCarried()
        {
            var engine = new ToolCarryNeedEngine(TestWork.Catalog);
            var field = new WorldObject { Id = "field_1", DisplayName = "Pole", Category = WorldObjectCategory.Ambient, LocationId = Loc, ItemTypeId = "grain", Field = new FieldState(FieldStage.Fallow, 1.0) };

            Assert.AreEqual(0, engine.Evaluate(Ctx("farmer", new[] { Spade("spade_1"), field })).Candidates.Count, "At a field the spade is used where it lies.");
            Assert.AreEqual(0, engine.Evaluate(Ctx(null, new[] { Spade("spade_1") })).Candidates.Count, "Off shift there is no work to carry it to.");
            Assert.AreEqual(0, engine.Evaluate(Ctx("healer", new[] { Spade("spade_1") })).Candidates.Count, "A healer's tasks need no spade.");
            Assert.AreEqual(0, engine.Evaluate(Ctx("farmer", new[] { Spade("spade_2") }, new[] { Spade("spade_1") with { HeldBy = Self } })).Candidates.Count, "Already carrying one.");
        }
    }
}
