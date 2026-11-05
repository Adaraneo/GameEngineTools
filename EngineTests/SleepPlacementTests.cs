// SleepPlacementTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Sleep;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System.Linq;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>Bedtime sends a character to bed; only a depleted body, or a failed trip, makes it sleep where it stands.</summary>
    [TestClass]
    public sealed class SleepPlacementTests : TestBase
    {
        private static DefaultSleepCoordinator Coordinator()
            => new(new SleepConfig(), new BehaviorConfig(), LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));

        // Bedtime: NeedRest is saturated by the circadian push while the body itself is rested (energy 95).
        private static BehaviorContext Bedtime(SurfaceKind surface, double energy = 95, PlannedAction? plan = null, WDateTime? now = null)
        {
            var context = BehaviorComponentTestFactory.Context(now: now, surfaceKind: surface, energy: energy);
            return context with { State = context.State with { NeedRest = 100, CurrentPlan = plan } };
        }

        [TestMethod]
        public void AtBedtimeInAPublicPlace_TheCharacterGoesToBed_NotToSleepOnTheSpot()
        {
            var context = Bedtime(SurfaceKind.Public);

            var result = Coordinator().Tick(context);
            var events = context.Outbox.Drain();

            Assert.IsTrue(events.OfType<ActionCommitted>().Any(a => a.ActionName == MoveToRest), "The character heads for a bed.");
            Assert.IsFalse(events.OfType<SleepPromptRequested>().Any(), "It does not drop off in the pub.");
            Assert.AreEqual(MoveToRest, result.NewState.CurrentPlan?.Name);
        }

        [TestMethod]
        public void WhileTheTripToBedIsUnderway_NothingElseIsDecided()
        {
            var plan = new PlannedAction(MoveToRest, new WDateTime(0), WTimeSpan.FromMinutes(20), 100);
            var context = Bedtime(SurfaceKind.Public, plan: plan, now: new WDateTime(WTimeSpan.FromMinutes(5).Ticks));

            var result = Coordinator().Tick(context);

            Assert.IsTrue(result.ConsumedTick);
            Assert.AreEqual(0, context.Outbox.Drain().Count, "No second trip and no prompt while the first is running.");
        }

        [TestMethod]
        public void IfTheTripFailed_TheCharacterSleepsWhereItStands()
        {
            var plan = new PlannedAction(MoveToRest, new WDateTime(0), WTimeSpan.FromMinutes(20), 100);
            var context = Bedtime(SurfaceKind.Public, plan: plan, now: new WDateTime(WTimeSpan.FromMinutes(40).Ticks));

            Coordinator().Tick(context);

            Assert.IsTrue(context.Outbox.Drain().OfType<SleepPromptRequested>().Any(), "No bed was reached, so it sleeps in place rather than never.");
        }

        [TestMethod]
        public void ADepletedBody_SleepsOnTheSpot_WithoutWalkingHome()
        {
            var context = Bedtime(SurfaceKind.Public, energy: 3);

            Coordinator().Tick(context);
            var events = context.Outbox.Drain();

            Assert.IsTrue(events.OfType<SleepPromptRequested>().Any());
            Assert.IsFalse(events.OfType<ActionCommitted>().Any(a => a.ActionName == MoveToRest));
        }

        [TestMethod]
        public void InABedroom_TheCharacterSleepsAtOnce()
        {
            var context = Bedtime(SurfaceKind.Rest);

            Coordinator().Tick(context);

            Assert.IsTrue(context.Outbox.Drain().OfType<SleepPromptRequested>().Any());
        }
    }
}
