// ToolReturnNeedEngineTests.cs
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
    using GameEngineTools.World.Utils.Time;
    using GameEngineTools.World.Work;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>Off the work shift a character who carries a tool brings it back to its shed and puts it down there.</summary>
    [TestClass]
    public sealed class ToolReturnNeedEngineTests : TestBase
    {
        private const string Loc = "test"; // BehaviorComponentTestFactory surface location

        private static readonly HumanId Self = new(Guid.NewGuid());

        private static BehaviorContext Ctx(bool onShift, params WorldObject[] held)
        {
            var ctx = BehaviorComponentTestFactory.Context(selfId: Self, hunger: 10, thirst: 5, now: new WDateTime(0));
            if (onShift)
            {
                var slot = new ScheduleSlot("work", 5, Work);
                ((HumanContext)ctx.HumanContext).Snapshot = ctx.HumanContext.Snapshot with { Schedule = new DailyScheduleState(new[] { slot }, slot, 0, "farmer") };
            }

            return ctx with { AvailableObjects = new List<WorldObject>(), HeldObjects = held.ToList() };
        }

        private static WorldObject Spade(string? storedAt)
        {
            return WorkObjects.Spawn(TestWork.Catalog, "spade", "spade_1", Loc) with { HeldBy = Self, StoredAt = storedAt };
        }

        [TestMethod]
        public void AToolFromAShedElsewhere_IsWalkedBack_AndPutDownOnceThere()
        {
            var engine = new ToolReturnNeedEngine();

            var walk = engine.Evaluate(Ctx(false, Spade("farm"))).Candidates.Single();
            var putDown = engine.Evaluate(Ctx(false, Spade(Loc))).Candidates.Single();

            Assert.AreEqual(MoveToStorage, walk.Name);
            Assert.AreEqual(InteractWithObject, putDown.Name);
            Assert.AreEqual(ObjectInteractionKind.Drop, putDown.ObjectInteraction!.Kind);
            Assert.IsTrue(putDown.Utility > walk.Utility, "Once at the shed, putting it down wins over walking on.");
        }

        [TestMethod]
        public void OnShift_ATooWithNoShed_OrNothingCarried_NothingIsReturned()
        {
            var engine = new ToolReturnNeedEngine();

            Assert.AreEqual(0, engine.Evaluate(Ctx(true, Spade("farm"))).Candidates.Count, "On shift the tool is kept.");
            Assert.AreEqual(0, engine.Evaluate(Ctx(false, Spade(null))).Candidates.Count, "A tool with no shed has nowhere to go back to.");
            Assert.AreEqual(0, engine.Evaluate(Ctx(false)).Candidates.Count, "Nothing carried.");
        }
    }
}
