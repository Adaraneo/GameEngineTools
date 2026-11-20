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
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Objects;
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
            return Ctx(onShift, null, held);
        }

        private static BehaviorContext Ctx(bool onShift, double? owedStrength, params WorldObject[] held)
        {
            var ctx = BehaviorComponentTestFactory.Context(selfId: Self, hunger: 10, thirst: 5, now: new WDateTime(0));
            if (onShift)
            {
                var slot = new ScheduleSlot("work", 5, Work);
                ((HumanContext)ctx.HumanContext).Snapshot = ctx.HumanContext.Snapshot with { Schedule = new DailyScheduleState(new[] { slot }, slot, 0, "farmer") };
            }

            if (owedStrength is { } strength)
            {
                var episode = new EpisodicMemory(Guid.NewGuid(), new WDateTime(0), ToolObligation.What("spade_1"), 0.6, EmotionalTag.Negative, strength);
                ((HumanContext)ctx.HumanContext).Snapshot = ctx.HumanContext.Snapshot with { Memory = new MemoryIndex(new List<EpisodicMemory> { episode }) };
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

        [TestMethod]
        public void OnShift_AStronglyRememberedLoan_IsBroughtBack_AWeakOneIsNot()
        {
            var engine = new ToolReturnNeedEngine();

            Assert.AreEqual(MoveToStorage, engine.Evaluate(Ctx(true, 0.8, Spade("farm"))).Candidates.Single().Name);
            Assert.AreEqual(0, engine.Evaluate(Ctx(true, 0.4, Spade("farm"))).Candidates.Count, "A faint memory does not pull the borrower off the shift.");
        }

        [TestMethod]
        public void AWalkBackThatWasGivenUp_IsNotOfferedAgainBeforeTheRetryTime_ButPuttingDownAtTheShedStillIs()
        {
            var engine = new ToolReturnNeedEngine();
            var waiting = Ctx(false, Spade("farm"));
            waiting = waiting with { State = waiting.State with { ToolReturnRetryAt = new WDateTime(0) + WTimeSpan.FromHours(6) } };
            var atShed = Ctx(false, Spade(Loc));
            atShed = atShed with { State = atShed.State with { ToolReturnRetryAt = new WDateTime(0) + WTimeSpan.FromHours(6) } };

            Assert.AreEqual(0, engine.Evaluate(waiting).Candidates.Count, "Not before the retry time.");
            Assert.AreEqual(InteractWithObject, engine.Evaluate(atShed).Candidates.Single().Name, "Already at the shed: put it down regardless.");
        }

        private static WorldObject Borrowed(string? storedAt, WDateTime lentAt)
        {
            return Spade(storedAt) with { OwnerId = new HumanId(Guid.NewGuid()), LentTo = Self, LentAt = lentAt };
        }

        [TestMethod]
        public void ABorrowedTool_IsWalkedToItsOwner_NotToTheShed_AndNotPutDownEvenAtTheShed()
        {
            var engine = new ToolReturnNeedEngine();
            var later = new WDateTime(0) + WTimeSpan.FromHours(5);

            var elsewhere = engine.Evaluate(Ctx(false, Borrowed("farm", new WDateTime(0))) with { Now = later }).Candidates.Single();
            var atTheShed = engine.Evaluate(Ctx(false, Borrowed(Loc, new WDateTime(0))) with { Now = later }).Candidates.Single();

            Assert.AreEqual(MoveToOwner, elsewhere.Name);
            Assert.AreEqual(MoveToOwner, atTheShed.Name, "A borrowed tool is not put down at the shed: it goes back to who lent it.");
        }

        [TestMethod]
        public void ABorrowedTool_IsKeptAWhile_AndFollowsTheSameShiftAndRetryRulesAsAWalkToTheShed()
        {
            var engine = new ToolReturnNeedEngine();
            var later = new WDateTime(0) + WTimeSpan.FromHours(5);

            Assert.AreEqual(0, engine.Evaluate(Ctx(false, Borrowed("farm", new WDateTime(0)))).Candidates.Count, "Just lent: kept for a while.");
            Assert.AreEqual(0, engine.Evaluate(Ctx(true, Borrowed("farm", new WDateTime(0))) with { Now = later }).Candidates.Count, "On shift with no strong memory: kept.");

            var waiting = Ctx(false, Borrowed("farm", new WDateTime(0))) with { Now = later };
            waiting = waiting with { State = waiting.State with { ToolReturnRetryAt = later + WTimeSpan.FromHours(6) } };
            Assert.AreEqual(0, engine.Evaluate(waiting).Candidates.Count, "A walk given up is not offered before the retry time.");
        }

        [TestMethod]
        public void AnOwnerHoldingAHandedBackTool_KeepsItAWhile_ThenCarriesItToTheShedEvenOnShift()
        {
            var engine = new ToolReturnNeedEngine();
            var later = new WDateTime(0) + WTimeSpan.FromHours(5);
            // A person's held objects come without HeldBy, so the holder must not be read from it.
            WorldObject HandedBack(string shed) => Spade(shed) with { OwnerId = Self, LentAt = new WDateTime(0), HeldBy = null };

            Assert.AreEqual(0, engine.Evaluate(Ctx(false, HandedBack("farm"))).Candidates.Count, "Just handed back: held for a while.");
            Assert.AreEqual(MoveToStorage, engine.Evaluate(Ctx(true, HandedBack("farm")) with { Now = later }).Candidates.Single().Name, "On shift too: the tool is not his to keep in hand.");
            Assert.AreEqual(InteractWithObject, engine.Evaluate(Ctx(true, HandedBack(Loc)) with { Now = later }).Candidates.Single().Name, "At the shed it is put down.");
        }

        [TestMethod]
        public void ACarriedToolInFront_DoesNotHideAHandedBackOneBehindIt_AndAToolWhoseShedIsHereGoesFirst()
        {
            var engine = new ToolReturnNeedEngine();
            var later = new WDateTime(0) + WTimeSpan.FromHours(5);
            var carried = Spade("farm");
            var handedBack = WorkObjects.Spawn(TestWork.Catalog, "spade", "spade_2", Loc) with { StoredAt = "far", OwnerId = Self, LentAt = new WDateTime(0), HeldBy = null };
            var shedHere = WorkObjects.Spawn(TestWork.Catalog, "spade", "spade_3", Loc) with { StoredAt = Loc, OwnerId = Self, HeldBy = null };

            Assert.AreEqual(MoveToStorage, engine.Evaluate(Ctx(true, carried, handedBack) with { Now = later }).Candidates.Single().Name, "On shift the carried tool is kept, but the handed-back one is still carried on.");
            Assert.AreEqual("spade_3", engine.Evaluate(Ctx(false, carried, handedBack, shedHere) with { Now = later }).Candidates.Single().ObjectInteraction!.ObjectId, "A tool whose shed is here is put down first.");
        }

        [TestMethod]
        public void TheWalkBack_IsMoreUrgentTheStrongerTheMemoryThatTheToolIsOwed()
        {
            var engine = new ToolReturnNeedEngine();

            var forgotten = engine.Evaluate(Ctx(false, Spade("farm"))).Candidates.Single().Utility;
            var faint = engine.Evaluate(Ctx(false, 0.3, Spade("farm"))).Candidates.Single().Utility;
            var vivid = engine.Evaluate(Ctx(false, 0.9, Spade("farm"))).Candidates.Single().Utility;

            Assert.IsTrue(forgotten < faint && faint < vivid, $"{forgotten:F1} < {faint:F1} < {vivid:F1}");
        }
    }
}
