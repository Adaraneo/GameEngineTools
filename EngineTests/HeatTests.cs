// HeatTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using GameEngineTools.World.Weather;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>Heat balance: sweat as thirst, stored heat, the heatstroke hazard and the damped air indoors.</summary>
    [TestClass]
    public sealed class HeatTests : TestBase
    {
        private static readonly PhysiologyConfig Cfg = new();

        private static double ThirstRate(string? action, double airC)
            => DefaultPhysiologyEngine.ComputeDrift(action, 1.0, Cfg, null, 1.0, airC).Thirst;

        [TestMethod]
        public void MildAir_KeepsTheOldWorkThirstRates()
        {
            var sweatPoints = (Cfg.PloughMet - 1.0) * Cfg.SweatLitresPerHourPerLoadUnit / Cfg.LitresPerThirstPoint;
            Assert.AreEqual(Cfg.ThirstDriftAwakePerHour + sweatPoints, ThirstRate(Plough, 20.0), 1e-9, "Work thirst is the ordinary loss plus the sweat of the task's MET.");
            Assert.AreEqual(Cfg.ThirstDriftAwakePerHour, ThirstRate(null, 24.0), 1e-9);
        }

        [TestMethod]
        public void Heat_MakesWorkAndEvenRestThirstier()
        {
            Assert.AreEqual(ThirstRate(Plough, 20.0) + Cfg.HeatLoadPerDegreeAboveComfort * 10.0 * Cfg.SweatLitresPerHourPerLoadUnit / Cfg.LitresPerThirstPoint, ThirstRate(Plough, 35.0), 1e-9, "Ploughing in 35 degC adds the sweat of the air heat to the sweat of the work.");
            Assert.IsTrue(ThirstRate(null, 35.0) > ThirstRate(null, 20.0), "Even rest sweats a little in the heat.");
            Assert.IsTrue(ThirstRate(Drink, 35.0) < 0.0, "Drinking still quenches.");
        }

        [TestMethod]
        public void AirHeatAddsTheSameAmountWhateverTheActivity()
        {
            foreach (var activity in new[] { 0.7, 1.0, 3.0 })
                Assert.AreEqual(Cfg.HeatLoadPerDegreeAboveComfort * 10.0, HeatMath.HeatProduction(activity, 35.0, Cfg) - HeatMath.HeatProduction(activity, 25.0, Cfg), 1e-12);
        }

        [TestMethod]
        public void Sleeper_StartsSweatingLaterThanSomeoneAwake_AndSweatsLess()
        {
            Assert.AreEqual(0.0, HeatMath.PotentialSweat(HeatMath.HeatProduction(Cfg.SleepActivityFactor, 27.5, Cfg), Cfg), 1e-12, "27.5 degC is still the sleeper's thermoneutral zone.");
            Assert.IsTrue(HeatMath.PotentialSweat(HeatMath.HeatProduction(Cfg.SleepActivityFactor, 31.0, Cfg), Cfg) > 0.0);
            Assert.IsTrue(HeatMath.PotentialSweat(HeatMath.HeatProduction(1.0, 31.0, Cfg), Cfg) > HeatMath.PotentialSweat(HeatMath.HeatProduction(Cfg.SleepActivityFactor, 31.0, Cfg), Cfg));
            Assert.IsTrue(ThirstRate(Sleep, 32.0) < ThirstRate(null, 32.0) - (Cfg.ThirstDriftAwakePerHour - Cfg.ThirstDriftSleepPerHour));
        }

        [TestMethod]
        public void SweatIsCapped()
        {
            var production = HeatMath.HeatProduction(3.0, 120.0, Cfg);

            Assert.AreEqual(Cfg.SweatMaxLitresPerHour, HeatMath.PotentialSweat(production, Cfg), 1e-12);
        }

        [TestMethod]
        public void StoredHeat_StaysAtZeroWithWater_AndGrowsOnceTheThroatIsDry()
        {
            var production = HeatMath.HeatProduction(3.0, 38.0, Cfg);

            Assert.AreEqual(0.0, HeatMath.HeatLoadTarget(production, 10.0, Cfg), 1e-9);
            Assert.IsTrue(HeatMath.HeatLoadTarget(production, 100.0, Cfg) > 3.0);
            Assert.IsTrue(HeatMath.HeatLoadTarget(production, 80.0, Cfg) > HeatMath.HeatLoadTarget(production, 65.0, Cfg));
        }

        [TestMethod]
        public void StoredHeat_IsZeroAtRestInTheCool_AndSmallWhenPloughingDryInMildAir()
        {
            Assert.AreEqual(0.0, HeatMath.HeatLoadTarget(HeatMath.HeatProduction(1.0, 15.0, Cfg), 100.0, Cfg), 1e-12);
            Assert.IsTrue(HeatMath.HeatLoadTarget(HeatMath.HeatProduction(3.0, 20.0, Cfg), 100.0, Cfg) < Cfg.HeatstrokeOnsetC);
        }

        [TestMethod]
        public void StoredHeat_FollowsItsTargetExponentially()
        {
            var one = HeatMath.AdvanceHeatLoad(0.0, 5.0, 1.0, Cfg);

            Assert.AreEqual(5.0 * (1.0 - Math.Exp(-1.0)), one, 1e-9);
            Assert.IsTrue(HeatMath.AdvanceHeatLoad(one, 0.0, 2.0, Cfg) < one);
        }

        [TestMethod]
        public void HeatstrokeRisk_StartsAtTheOnset_ExceedsTheGeneralCap_AndRisesWithAge()
        {
            Assert.AreEqual(0.0, HeatMath.HeatstrokeRisk(Cfg.HeatstrokeOnsetC, 30, Cfg), 1e-12);
            Assert.AreEqual(Cfg.HeatstrokeMaxRiskPerHour, HeatMath.HeatstrokeRisk(Cfg.HeatstrokeFullC, 30, Cfg), 1e-12);
            Assert.IsTrue(HeatMath.HeatstrokeRisk(Cfg.HeatstrokeFullC, 30, Cfg) > Cfg.NaturalMortalityMaxRiskPerHour);
            Assert.IsTrue(HeatMath.HeatstrokeRisk(4.0, 70, Cfg) > HeatMath.HeatstrokeRisk(4.0, 30, Cfg));
        }

        [TestMethod]
        public void Mortality_AddsHeatAboveTheGeneralCap_AndNamesTheCause()
        {
            var hot = Nominal() with { BodyTempDelta = 3.0, HeatLoad = 1.5 };
            var worst = hot with { AllostaticLoad = 100, ImmuneLoad = 100, Hunger = 100, Thirst = 100 };

            var cool = NaturalMortalityCalculator.ComputeHourlyRisk(worst with { BodyTempDelta = 0, HeatLoad = 0 }, 30, Cfg);

            Assert.AreEqual(cool + Cfg.HeatstrokeMaxRiskPerHour, NaturalMortalityCalculator.ComputeHourlyRisk(worst, 30, Cfg), 1e-9);
            Assert.IsTrue(NaturalMortalityCalculator.ComputeHourlyRisk(hot, 30, Cfg) > Cfg.NaturalMortalityMaxRiskPerHour, "Heat alone exceeds the general cap.");
            Assert.AreEqual(DeathCause.Heatstroke, NaturalMortalityCalculator.ResolveCause(hot, 30, Cfg));
        }

        [TestMethod]
        public void Indoor_DampsTheDaysSwing()
        {
            Assert.AreEqual(12.0, AmbientTemperatureModel.Indoor(10.0, 14.0, 0.5), 1e-12);
            Assert.AreEqual(10.0, AmbientTemperatureModel.Indoor(10.0, 14.0, 0.0), 1e-12);
            Assert.AreEqual(14.0, AmbientTemperatureModel.Indoor(10.0, 14.0, 1.0), 1e-12);
        }

        [TestMethod]
        public void Engine_ADryCharacterInScorchingAirStoresHeat_AWateredOneDoesNot()
        {
            var dry = Hours(thirst: 100, airC: 60, hours: 6, keepWatered: false);
            var watered = Hours(thirst: 10, airC: 60, hours: 6, keepWatered: true);

            Assert.IsTrue(dry.State.HeatLoad > 1.0, $"dry: {dry.State.HeatLoad}");
            Assert.AreEqual(0.0, watered.State.HeatLoad, 1e-6);
        }

        [TestMethod]
        public void Engine_HeatstrokeKillsAYoungCharacterAndIsReportedAsSuch()
        {
            var engine = Engine(30);
            engine.RestoreState(engine.State with { BodyTempDelta = 3.5, HeatLoad = 2.0 });
            var outbox = new EventCollector();

            engine.Tick(WDateOnly.New(116, 1, 1).ToDateTime(), WTimeSpan.FromHours(1), Context(20.0, new AlwaysTrue()), outbox);

            var died = outbox.Drain().OfType<CharacterDied>().Single();
            Assert.AreEqual(DeathCause.Heatstroke, died.Cause);
        }

        [TestMethod]
        public void Engine_NoDeathRollBelowTheOnset_ForAYoungCharacter()
        {
            var engine = Engine(30);
            engine.RestoreState(engine.State with { BodyTempDelta = 0.3, HeatLoad = 0.5 });
            var outbox = new EventCollector();

            engine.Tick(WDateOnly.New(116, 1, 1).ToDateTime(), WTimeSpan.FromHours(1), Context(20.0, new AlwaysTrue()), outbox);

            Assert.AreEqual(0, outbox.Drain().OfType<CharacterDied>().Count());
        }

        private static DefaultPhysiologyEngine Hours(double thirst, double airC, int hours, bool keepWatered)
        {
            var engine = Engine(30);
            engine.RestoreState(engine.State with { Thirst = thirst });
            var ctx = Context(airC, new ZeroRandom());
            var now = WDateOnly.New(116, 1, 1).ToDateTime();
            for (var i = 0; i < hours; i++)
            {
                if (keepWatered) engine.RestoreState(engine.State with { Thirst = thirst });
                engine.Tick(now, WTimeSpan.FromHours(1), ctx, new EventCollector());
            }

            return engine;
        }

        private static PhysiologyState Nominal() => new(
            Energy: 70, SleepDebtHours: 2, Hunger: 25, Thirst: 20, Pain: 5, ImmuneLoad: 10, BodyTempDelta: 0, Cycle: null, Aging: new PhysicalAgingState());

        private static DefaultPhysiologyEngine Engine(int ageYears)
        {
            var cfg = Options.Create(new PhysiologyConfig(EnableMenstrualCycle: false, EnableTestosteroneCycle: false));
            var engine = new DefaultPhysiologyEngine(
                cfg, Options.Create(new MenstrualCycleConfig()), LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)), new ZeroRandom(),
                biology: SexBiology.Female, birthDate: WDateOnly.New(116 - ageYears, 1, 1), now: WDateOnly.New(116, 1, 1));
            engine.RestoreState(Nominal() with { Aging = engine.State.Aging });
            return engine;
        }

        private static IHumanContext Context(double airC, IRandomSource random)
        {
            var snapshot = new EnginesSnapshot(
                Nominal(), new PsychologyState(0.1, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral),
                new BehaviorState(40, 20, 15, 40, 50, 30, null),
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()),
                AmbientTemperature: airC);
            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = new Personality(
                    new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), AttachmentProfile.Secure, CommunicationStyle.Direct,
                    new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4), Sociosexuality.Intermediate, Chronotype.Neutral),
                Snapshot = snapshot,
                Random = random,
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        private sealed class AlwaysTrue : IRandomSource
        {
            public int Next(int min, int max) => min;

            public double NextUnit() => 0.0;

            public bool Chance(double p) => true;
        }
    }
}
