// EmergencyNeedsTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Arbitration;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging.Abstractions;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System.Collections.Generic;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>When hunger and thirst are both critical the one nearer to death is answered first.</summary>
    [TestClass]
    public sealed class EmergencyNeedsTests : TestBase
    {
        private static List<BehaviorCandidate> Candidates() => new()
        {
            new BehaviorCandidate(Eat, 144.5, WTimeSpan.FromMinutes(30), BehaviorDomain.Physiological),
            new BehaviorCandidate(Drink, 144.0, WTimeSpan.FromMinutes(10), BehaviorDomain.Physiological),
            new BehaviorCandidate(Work, 60.0, WTimeSpan.FromHours(1), BehaviorDomain.Competence),
        };

        [TestMethod]
        public void MostUrgent_IsTheNeedWithTheLeastTimeToTheLethalLevel_ButOnlyWhenBothAreCritical()
        {
            Assert.AreEqual(EmergencyNeeds.Need.Thirst, EmergencyNeeds.MostUrgent(BehaviorComponentTestFactory.Context(hunger: 85, thirst: 90)), "1.0 h against 1.7 h.");
            Assert.AreEqual(EmergencyNeeds.Need.Hunger, EmergencyNeeds.MostUrgent(BehaviorComponentTestFactory.Context(hunger: 92, thirst: 80)), "0.5 h against 3.0 h.");
            Assert.AreEqual(EmergencyNeeds.Need.None, EmergencyNeeds.MostUrgent(BehaviorComponentTestFactory.Context(hunger: 40, thirst: 90)), "Only one need is critical: no ranking.");
            Assert.AreEqual(EmergencyNeeds.Need.None, EmergencyNeeds.MostUrgent(BehaviorComponentTestFactory.Context(hunger: 20, thirst: 20)));
        }

        [TestMethod]
        public void Arbitrate_WithBothNeedsCritical_ChoosesTheMoreUrgentEvenAgainstTheHigherUtility()
        {
            var engine = new DefaultActionArbitrationEngine(NullLogger.Instance);

            var thirstier = engine.Arbitrate(BehaviorComponentTestFactory.Context(hunger: 85, thirst: 90), Candidates());
            Assert.AreEqual(Drink, thirstier.SelectedCandidate!.Name, "Thirst 90 is nearer to death than hunger 85, although Eat scores slightly higher.");

            var hungrier = engine.Arbitrate(BehaviorComponentTestFactory.Context(hunger: 92, thirst: 80), Candidates());
            Assert.AreEqual(Eat, hungrier.SelectedCandidate!.Name);
        }

        [TestMethod]
        public void Arbitrate_WithOneNeedCritical_KeepsTheOrdinaryUtilityOrder()
        {
            var engine = new DefaultActionArbitrationEngine(NullLogger.Instance);

            var result = engine.Arbitrate(BehaviorComponentTestFactory.Context(hunger: 40, thirst: 90), Candidates());

            Assert.AreNotEqual("urgent:Thirst", result.ConflictReason, "The ranking applies only when both needs are critical.");
        }

        [TestMethod]
        public void Arbitrate_ARunningMealIsInterrupted_WhenThirstIsTheNearerDeath()
        {
            var engine = new DefaultActionArbitrationEngine(NullLogger.Instance);
            var ctx = BehaviorComponentTestFactory.Context(hunger: 85, thirst: 90);
            var running = new PlannedAction(Eat, ctx.Now, WTimeSpan.FromMinutes(30), 144.5);
            ctx = ctx with { State = ctx.State with { CurrentPlan = running } };

            var result = engine.Arbitrate(ctx, Candidates());

            Assert.AreEqual(Drink, result.SelectedCandidate!.Name);
        }

        [TestMethod]
        public void TheRankingRates_AreTheSameAsPhysiologyUses()
        {
            var behavior = new BehaviorConfig();
            var physiology = new PhysiologyConfig();

            Assert.AreEqual(physiology.HungerDriftAwakePerHour, behavior.HungerRisePerHour);
            Assert.AreEqual(physiology.ThirstDriftAwakePerHour, behavior.ThirstRisePerHour);
            Assert.AreEqual(physiology.NaturalMortalityStarvationThreshold, behavior.LethalNeedLevel);
            Assert.AreEqual(physiology.NaturalMortalityDehydrationThreshold, behavior.LethalNeedLevel);
        }
    }
}
