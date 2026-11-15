// WaterTests.cs
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
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;

    /// <summary>Body water as litres: conservation, the thirst view, the lethal deficit and the days it takes to die of thirst.</summary>
    [TestClass]
    public sealed class WaterTests : TestBase
    {
        private static readonly PhysiologyConfig Cfg = new();

        private static double Hours(double fromThirst, double untilDeficitL)
        {
            var deficit = fromThirst * Cfg.LitresPerThirstPoint;
            var hours = 0.0;
            while (deficit < untilDeficitL && hours < 2000)
            {
                deficit += WaterMath.LossPointsPerHour(false, deficit, Cfg) * Cfg.LitresPerThirstPoint;
                hours += 1.0;
            }

            return hours;
        }

        [TestMethod]
        public void Conservation_IsOneUntilTheStart_ZeroFromTheFullLevel_AndFalls()
        {
            Assert.AreEqual(1.0, WaterMath.Conservation(0.0, Cfg), 1e-12);
            Assert.AreEqual(1.0, WaterMath.Conservation(Cfg.WaterConservationStartLitres, Cfg), 1e-12);
            Assert.AreEqual(0.0, WaterMath.Conservation(Cfg.WaterConservationFullLitres, Cfg), 1e-12);
            Assert.IsTrue(WaterMath.Conservation(2.2, Cfg) > WaterMath.Conservation(2.8, Cfg));
        }

        [TestMethod]
        public void AnOrdinaryDay_KeepsTheOldRates_AndDeprivationFallsToTheObligatoryLoss()
        {
            Assert.AreEqual(Cfg.ThirstDriftAwakePerHour, WaterMath.LossPointsPerHour(false, 1.0, Cfg), 1e-9);
            Assert.AreEqual(Cfg.ThirstDriftSleepPerHour, WaterMath.LossPointsPerHour(true, 1.0, Cfg), 1e-9);
            Assert.AreEqual(Cfg.WaterObligatoryLossLitresPerHour / Cfg.LitresPerThirstPoint, WaterMath.LossPointsPerHour(false, 3.5, Cfg), 1e-9);
        }

        [TestMethod]
        public void Deprivation_TakesDaysNotHours_ToTheThirstScaleLimitAndToDeath()
        {
            var toScaleTop = Hours(fromThirst: 20.0, untilDeficitL: Cfg.WaterConservationFullLitres);
            var toLethal = Hours(fromThirst: 20.0, untilDeficitL: WaterMath.LethalDeficitL(Cfg));

            Assert.IsTrue(toScaleTop is > 20.0 and < 40.0, $"to thirst 95: {toScaleTop} h (19 h before conservation)");
            Assert.IsTrue(toLethal is > 72.0 and < 168.0, $"to the lethal deficit: {toLethal} h");
        }

        [TestMethod]
        public void LethalDeficit_IsAShareOfBodyMass_AndThirstViewSaturates()
        {
            Assert.AreEqual(7.0, WaterMath.LethalDeficitL(Cfg), 1e-9);
            Assert.AreEqual(90.0, WaterMath.LethalDeficitL(new PhysiologyConfig(BodyMassKg: 90.0)) * 10.0, 1e-9);

            var deep = Nominal() with { WaterDeficitL = 5.0, Thirst = 100 };
            Assert.IsFalse(WaterMath.IsLethal(deep, Cfg), "100 on the scale is 3.5 L, far below the lethal deficit.");
            Assert.IsTrue(WaterMath.IsLethal(deep with { WaterDeficitL = 7.0 }, Cfg));
            Assert.AreEqual(100.0, WaterMath.ViewThirst(5.0, Cfg), 1e-12);
        }

        [TestMethod]
        public void AnotherWriterOfThirst_Wins_OverAStaleDeficit()
        {
            var changed = Nominal() with { WaterDeficitL = 5.0, Thirst = 10.0 };

            Assert.AreEqual(10.0 * Cfg.LitresPerThirstPoint, WaterMath.Deficit(changed, Cfg), 1e-12);
            Assert.AreEqual(0.7, WaterMath.Deficit(Nominal(), Cfg), 1e-12, "An old save without the litres reads the score as litres.");
        }

        [TestMethod]
        public void Engine_KeepsTheLitresAndTheScoreInStep()
        {
            var engine = Engine();
            var outbox = new EventCollector();
            var now = WDateOnly.New(116, 1, 1).ToDateTime();
            var ctx = Context();

            for (var i = 0; i < 6; i++)
                engine.Tick(now, WTimeSpan.FromHours(1), ctx, outbox);

            var s = engine.State;
            Assert.IsNotNull(s.WaterDeficitL);
            Assert.AreEqual(s.WaterDeficitL!.Value / Cfg.LitresPerThirstPoint, s.Thirst, 1e-6);
            Assert.IsTrue(s.WaterDeficitL > 0.7 + 0.5, "Six awake hours at the free rate add about 1 L.");
        }

        [TestMethod]
        public void ABodyOfAnotherMass_ScalesTheLitres_ButNotTheScoreOfOrdinaryWater_AndMakesALightPersonSweatMoreForTheSameWork()
        {
            var light = Cfg.ForBody(50.0);
            var heavy = Cfg.ForBody(90.0);

            Assert.AreEqual(5.0, WaterMath.LethalDeficitL(light), 1e-9);
            Assert.AreEqual(9.0, WaterMath.LethalDeficitL(heavy), 1e-9);
            Assert.AreEqual(0.05 * 50.0, light.LitresPerThirstPoint * 100.0, 1e-9, "Thirst 100 is the same share of the body (5 percent) for everyone.");
            Assert.AreEqual(WaterMath.LossPointsPerHour(false, 0.5, light), WaterMath.LossPointsPerHour(false, 0.5 * 90.0 / 50.0, heavy), 1e-9, "At the same share of the body the ordinary loss in points is the same.");

            var lightSweat = HeatMath.SweatThirstPerHour(HeatMath.PotentialSweat(6.0, light), light);
            var heavySweat = HeatMath.SweatThirstPerHour(HeatMath.PotentialSweat(6.0, heavy), heavy);
            Assert.IsTrue(lightSweat > heavySweat, $"The same work takes more of a light body's water: {lightSweat:F1} against {heavySweat:F1} points per hour.");
        }

        [TestMethod]
        public void Engine_UsesTheCharactersOwnBodyMass()
        {
            double Lethal(double kg)
            {
                var engine = Engine();
                var ctx = (HumanContext)Context();
                ctx.BodyMassKg = kg;
                engine.Tick(WDateOnly.New(116, 1, 1).ToDateTime(), WTimeSpan.FromHours(1), ctx, new EventCollector());
                return WaterMath.LethalDeficitL(Cfg.ForBody(kg));
            }

            Assert.AreEqual(5.0, Lethal(50.0), 1e-9);
            var light = Engine();
            var heavy = Engine();
            var lightCtx = (HumanContext)Context();
            var heavyCtx = (HumanContext)Context();
            lightCtx.BodyMassKg = 50.0;
            heavyCtx.BodyMassKg = 90.0;
            var outbox = new EventCollector();
            var now = WDateOnly.New(116, 1, 1).ToDateTime();
            for (var i = 0; i < 6; i++)
            {
                light.Tick(now, WTimeSpan.FromHours(1), lightCtx, outbox);
                heavy.Tick(now, WTimeSpan.FromHours(1), heavyCtx, outbox);
            }

            Assert.AreEqual(heavy.State.Thirst, light.State.Thirst, 0.5, "Six ordinary hours raise the score the same for both bodies.");
            Assert.IsTrue(heavy.State.WaterDeficitL > light.State.WaterDeficitL * 1.5, "But the heavy body has lost more litres.");
        }

        private static PhysiologyState Nominal() => new(
            Energy: 70, SleepDebtHours: 2, Hunger: 25, Thirst: 20, Pain: 5, ImmuneLoad: 10, BodyTempDelta: 0, Cycle: null, Aging: new PhysicalAgingState());

        private static DefaultPhysiologyEngine Engine()
        {
            var cfg = Options.Create(new PhysiologyConfig(EnableMenstrualCycle: false, EnableTestosteroneCycle: false));
            var engine = new DefaultPhysiologyEngine(
                cfg, Options.Create(new MenstrualCycleConfig()), LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)), new ZeroRandom(),
                biology: SexBiology.Female, birthDate: WDateOnly.New(86, 1, 1), now: WDateOnly.New(116, 1, 1));
            engine.RestoreState(Nominal() with { Aging = engine.State.Aging });
            return engine;
        }

        private static IHumanContext Context()
        {
            var snapshot = new EnginesSnapshot(
                Nominal(), new PsychologyState(0.1, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral),
                new BehaviorState(40, 20, 15, 40, 50, 30, null),
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()),
                AmbientTemperature: 20.0);
            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = new Personality(
                    new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), AttachmentProfile.Secure, CommunicationStyle.Direct,
                    new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4), Sociosexuality.Intermediate, Chronotype.Neutral),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }
    }
}
