// NaturalMortalityTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    [TestClass]
    public class NaturalMortalityTests : TestBase
    {
        private static readonly PhysiologyConfig DefaultCfg = new PhysiologyConfig();

        #region Section 1 — ComputeHourlyRisk unit tests

        [TestMethod]
        public void ComputeHourlyRisk_Healthy30YearOld_RiskIsNearZero()
        {
            var state = NominalState();
            var risk = NaturalMortalityCalculator.ComputeHourlyRisk(state, ageYears: 30, cfg: DefaultCfg);

            Assert.AreEqual(0.0, risk, delta: 1e-6,
                $"Healthy 30yo must have near-zero mortality risk. Got {risk:F8}");
        }

        [TestMethod]
        public void ComputeHourlyRisk_OldWithHighLoad_FarExceedsAgeOnlyRisk()
        {
            var ageOnly = NaturalMortalityCalculator.ComputeHourlyRisk(
                NominalState(), ageYears: 80, cfg: DefaultCfg);
            var withLoad = NaturalMortalityCalculator.ComputeHourlyRisk(
                NominalState() with { AllostaticLoad = 95, ImmuneLoad = 80 }, ageYears: 80, cfg: DefaultCfg);

            Assert.IsTrue(withLoad > ageOnly * 5,
                $"80yo with high allostatic + immune load must carry far higher risk than age alone. age={ageOnly:E2}, load={withLoad:E2}");

            var annual = 1.0 - Math.Pow(1.0 - withLoad, 9360);
            Assert.IsTrue(annual > 0.30,
                $"80yo with allo=95 + immune=80 must have >30% annual mortality. Got {annual:P1}");
        }

        [TestMethod]
        public void ComputeHourlyRisk_StarvationStateYoungCharacter_RiskAboveZero()
        {
            // Hunger=100, Thirst=100 → starvation contribution should make risk > 0
            var state = NominalState() with { Hunger = 100, Thirst = 100 };
            var risk = NaturalMortalityCalculator.ComputeHourlyRisk(state, ageYears: 25, cfg: DefaultCfg);

            Assert.IsTrue(risk > 0,
                $"Young character in terminal starvation must have risk > 0. Got {risk:F8}");
        }

        [TestMethod]
        public void ComputeHourlyRisk_AllostaticLoad_SpikeAcceleratesAboveThreshold()
        {
            // Below the allostatic threshold (80) → no contribution.
            var below = NaturalMortalityCalculator.ComputeHourlyRisk(
                NominalState() with { AllostaticLoad = 72 }, ageYears: 30, cfg: DefaultCfg);
            // Linear band (80–90).
            var linear = NaturalMortalityCalculator.ComputeHourlyRisk(
                NominalState() with { AllostaticLoad = 88 }, ageYears: 30, cfg: DefaultCfg);
            // Above the spike threshold (90) — acute decompensation.
            var spiked = NaturalMortalityCalculator.ComputeHourlyRisk(
                NominalState() with { AllostaticLoad = 98 }, ageYears: 30, cfg: DefaultCfg);

            Assert.AreEqual(0.0, below, delta: 1e-12,
                $"AlloLoad below threshold must not contribute. Got {below:E2}");
            Assert.IsTrue(linear > 0, $"AlloLoad in the linear band must contribute. Got {linear:E2}");
            Assert.IsTrue(spiked - linear > linear - below,
                $"Decompensation spike above 90 must accelerate risk faster than the linear band. below={below:E2}, linear={linear:E2}, spiked={spiked:E2}");
        }

        [TestMethod]
        public void ComputeHourlyRisk_IsCappedAtMaxRiskPerHour()
        {
            // Worst-case state: everything at maximum stress
            var state = NominalState() with
            {
                AllostaticLoad = 100,
                ImmuneLoad = 100,
                Hunger = 100,
                Thirst = 100,
                Energy = 0,
                SleepDebtHours = 72,
                Aging = new PhysicalAgingState(BoneDensity: 0.1, MuscleMassFraction: 0.2)
            };
            var risk = NaturalMortalityCalculator.ComputeHourlyRisk(state, ageYears: 100, cfg: DefaultCfg);

            Assert.AreEqual(DefaultCfg.NaturalMortalityMaxRiskPerHour, risk, delta: 1e-10,
                $"Risk must be capped at MaxRiskPerHour={DefaultCfg.NaturalMortalityMaxRiskPerHour}. Got {risk}");
        }

        #endregion Section 1 — ComputeHourlyRisk unit tests

        #region Section 2 — ResolveCause priority order

        [TestMethod]
        public void ResolveCause_HungerTerminal_ReturnsStarvation()
        {
            var state = NominalState() with { Hunger = 100, Thirst = 100 };
            var cause = NaturalMortalityCalculator.ResolveCause(state, ageYears: 30, DefaultCfg);

            Assert.AreEqual(DeathCause.Starvation, cause,
                "Hunger=100 + Thirst=100 must resolve to Starvation.");
        }

        [TestMethod]
        public void ResolveCause_ExhaustionTerminal_ReturnsExhaustion()
        {
            var state = NominalState() with { Energy = 0, SleepDebtHours = 60 };
            var cause = NaturalMortalityCalculator.ResolveCause(state, ageYears: 30, DefaultCfg);

            Assert.AreEqual(DeathCause.Exhaustion, cause,
                "Energy=0 + SleepDebt=60h must resolve to Exhaustion.");
        }

        [TestMethod]
        public void ResolveCause_SystemicOverload_ReturnsSystemicFailure()
        {
            var state = NominalState() with { AllostaticLoad = 100, ImmuneLoad = 90 };
            var cause = NaturalMortalityCalculator.ResolveCause(state, ageYears: 40, DefaultCfg);

            Assert.AreEqual(DeathCause.SystemicFailure, cause,
                "AlloLoad=100 + ImmuneLoad=90 must resolve to SystemicFailure.");
        }

        [TestMethod]
        public void ResolveCause_VeryOldOtherwiseHealthy_ReturnsOldAge()
        {
            var state = NominalState(); // nominal vitals, no extreme load
            var cause = NaturalMortalityCalculator.ResolveCause(state, ageYears: 105, DefaultCfg);

            Assert.AreEqual(DeathCause.OldAge, cause,
                "Very old character with nominal vitals must resolve to OldAge.");
        }

        [TestMethod]
        public void ResolveCause_StarvationBeatsExhaustion_WhenBothPresent()
        {
            // Both starvation AND exhaustion conditions true — Starvation has higher priority
            var state = NominalState() with { Hunger = 100, Thirst = 100, Energy = 0, SleepDebtHours = 60 };
            var cause = NaturalMortalityCalculator.ResolveCause(state, ageYears: 30, DefaultCfg);

            Assert.AreEqual(DeathCause.Starvation, cause,
                "Starvation must take priority over Exhaustion when both conditions are met.");
        }

        #endregion Section 2 — ResolveCause priority order

        #region Section 3 — DefaultPhysiologyEngine integration

        [TestMethod]
        public void Tick_ForcedDeathRoll_OldCharacter_EmitsCharacterDied()
        {
            // AlwaysTrueRandom.Chance() returns true unconditionally — forces the death roll.
            // Age 80 (birthYear 36, todayYear 116) places the character past GompertzStart=60.
            // now must be year 116 so that ageYears = 116 - 36 = 80 (not year 0 - 36 = -36).
            var engine = BuildEngineWithAge(ageYears: 80);
            var ctx = BuildContext(random: new AlwaysTrueRandom());
            var outbox = new EventCollector();
            var now = WDateOnly.New(116, 1, 1).ToDateTime();

            engine.Tick(now, WTimeSpan.FromHours(1), ctx, outbox);

            var events = outbox.Drain();
            Assert.IsTrue(events.OfType<CharacterDied>().Any(),
                "An 80yo character with AlwaysTrueRandom must emit CharacterDied on every tick.");
        }

        [TestMethod]
        public void Tick_HealthyYoungCharacter_DoesNotEmitCharacterDied()
        {
            // Age 30, all nominal — risk = 0, so the mortality block doesn't run at all.
            // ZeroRandom.Chance() = false, but risk=0 means tickRisk=0 → Chance(0) is moot.
            var engine = BuildEngineWithAge(ageYears: 30);
            var ctx = BuildContext(random: new ZeroRandom());
            var outbox = new EventCollector();
            var now = WDateOnly.New(116, 1, 1).ToDateTime();

            engine.Tick(now, WTimeSpan.FromHours(1), ctx, outbox);

            Assert.IsFalse(outbox.Drain().OfType<CharacterDied>().Any(),
                "A healthy 30yo must not emit CharacterDied under nominal conditions.");
        }

        [TestMethod]
        public void Tick_CharacterDied_HasCorrectCause()
        {
            // OldAge scenario: age 90, nominal vitals → cause must be OldAge.
            var engine = BuildEngineWithAge(ageYears: 90);
            var ctx = BuildContext(random: new AlwaysTrueRandom());
            var outbox = new EventCollector();
            var now = WDateOnly.New(116, 1, 1).ToDateTime();

            engine.Tick(now, WTimeSpan.FromHours(1), ctx, outbox);

            var death = outbox.Drain().OfType<CharacterDied>().FirstOrDefault();
            Assert.IsNotNull(death, "CharacterDied must be emitted for 90yo with AlwaysTrueRandom.");
            Assert.AreEqual(DeathCause.OldAge, death!.Cause,
                "A 90yo with nominal vitals must die of OldAge.");
            Assert.AreEqual(0.0, death.FinalDamageTaken, delta: 1e-10,
                "Natural death must have FinalDamageTaken = 0.");
        }

        #endregion Section 3 — DefaultPhysiologyEngine integration

        #region Section 4 — Calibration (annual mortality over a 9360-hour game year)

        // VIWorld calendar: 10 months × 36 days × 26 hours.
        private const int HoursPerGameYear = 10 * 36 * 26;
        private const int HoursPerGameDay = 26;

        private static double AnnualMortality(PhysiologyState s, int age) =>
            1.0 - Math.Pow(1.0 - NaturalMortalityCalculator.ComputeHourlyRisk(s, age, DefaultCfg), HoursPerGameYear);

        [TestMethod]
        public void Calibration_AgeMortality_FollowsRealisticGompertz()
        {
            Assert.IsTrue(AnnualMortality(NominalState(), 30) < 0.005,
                $"30yo annual mortality must be <0.5%. Got {AnnualMortality(NominalState(), 30):P3}");
            Assert.IsTrue(AnnualMortality(NominalState(), 60) is > 0.003 and < 0.03,
                $"60yo annual mortality must be ~1% (0.3–3%). Got {AnnualMortality(NominalState(), 60):P3}");
            Assert.IsTrue(AnnualMortality(NominalState(), 80) is > 0.02 and < 0.12,
                $"80yo annual mortality must be ~5% (2–12%). Got {AnnualMortality(NominalState(), 80):P3}");
        }

        [TestMethod]
        public void Calibration_AgeMortality_RisesMonotonically()
        {
            var a50 = AnnualMortality(NominalState(), 50);
            var a60 = AnnualMortality(NominalState(), 60);
            var a70 = AnnualMortality(NominalState(), 70);
            var a85 = AnnualMortality(NominalState(), 85);

            Assert.IsTrue(a60 > a50 && a70 > a60 && a85 > a70,
                $"Mortality must rise with age. 50={a50:P3} 60={a60:P3} 70={a70:P3} 85={a85:P3}");
        }

        [TestMethod]
        public void Calibration_NoDeathCliff_55YearOldStaysLowRisk()
        {
            // Regression: the old curve started at 60 with ~61%/yr — a death cliff.
            Assert.IsTrue(AnnualMortality(NominalState(), 55) < 0.01,
                $"55yo annual mortality must stay <1% (no cliff). Got {AnnualMortality(NominalState(), 55):P3}");
        }

        [TestMethod]
        public void Calibration_TerminalDehydration_KillsWithinDays()
        {
            var hourly = NaturalMortalityCalculator.ComputeHourlyRisk(
                NominalState() with { Thirst = 100 }, ageYears: 30, cfg: DefaultCfg);
            var survive5Days = Math.Pow(1.0 - hourly, 5 * HoursPerGameDay);

            Assert.IsTrue(survive5Days < 0.5,
                $"Terminal thirst must kill >50% within 5 days. 5-day survival={survive5Days:P1}");
        }

        [TestMethod]
        public void Calibration_TerminalStarvation_KillsSlowerThanDehydration()
        {
            var hunger = NaturalMortalityCalculator.ComputeHourlyRisk(
                NominalState() with { Hunger = 100 }, 30, DefaultCfg);
            var thirst = NaturalMortalityCalculator.ComputeHourlyRisk(
                NominalState() with { Thirst = 100 }, 30, DefaultCfg);

            Assert.IsTrue(hunger > 0, "Terminal hunger must contribute mortality.");
            Assert.IsTrue(hunger < thirst,
                $"Starvation (weeks) must be slower than dehydration (days). hunger={hunger:E2}, thirst={thirst:E2}");

            var survive21Days = Math.Pow(1.0 - hunger, 21 * HoursPerGameDay);
            Assert.IsTrue(survive21Days < 0.6,
                $"Terminal hunger must kill the majority within ~3 weeks. 21-day survival={survive21Days:P1}");
        }

        [TestMethod]
        public void Calibration_PureThirst_IsLethalWithoutHunger()
        {
            // Regression: the old model required Hunger>=95 AND Thirst>=95, so pure thirst never killed.
            var pureThirst = NaturalMortalityCalculator.ComputeHourlyRisk(
                NominalState() with { Thirst = 100, Hunger = 20 }, ageYears: 25, cfg: DefaultCfg);
            Assert.IsTrue(pureThirst > 0,
                $"Pure dehydration (thirst high, hunger low) must be lethal on its own. Got {pureThirst:E2}");

            var cause = NaturalMortalityCalculator.ResolveCause(
                NominalState() with { Thirst = 100, Hunger = 20 }, ageYears: 25, DefaultCfg);
            Assert.AreEqual(DeathCause.Dehydration, cause, "Pure thirst death must resolve to Dehydration.");
        }

        #endregion Section 4 — Calibration

        #region Helpers

        private static PhysiologyState NominalState() =>
            new PhysiologyState(
                Energy: 70,
                SleepDebtHours: 2,
                Hunger: 25,
                Thirst: 20,
                Pain: 5,
                ImmuneLoad: 10,
                BodyTempDelta: 0,
                Cycle: null,
                Aging: new PhysicalAgingState());

        private static DefaultPhysiologyEngine BuildEngineWithAge(int ageYears)
        {
            var cfg = Options.Create(new PhysiologyConfig(
                EnableMenstrualCycle: false,
                EnableTestosteroneCycle: false));
            var cycleCfg = Options.Create(new MenstrualCycleConfig());
            var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            const int todayYear = 116;

            var engine = new DefaultPhysiologyEngine(
                cfg, cycleCfg, factory, new ZeroRandom(),
                biology: SexBiology.Female,
                birthDate: WDateOnly.New(todayYear - ageYears, 1, 1),
                now: WDateOnly.New(todayYear, 1, 1));

            engine.RestoreState(NominalState() with { Aging = engine.State.Aging });
            return engine;
        }

        private static IHumanContext BuildContext(IRandomSource? random = null)
        {
            var physio = NominalState();
            var psych = new PsychologyState(0.1, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral);

            var snapshot = new EnginesSnapshot(
                physio, psych,
                new BehaviorState(40, 20, 15, 40, 50, 30, null),
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = new Personality(
                    new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                    AttachmentProfile.Secure,
                    CommunicationStyle.Direct,
                    new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                    Sociosexuality.Intermediate,
                    Chronotype.Neutral),
                Snapshot = snapshot,
                Random = random ?? new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new TestBase.NullEventBus(),
                Scheduler = new TestBase.NullScheduler()
            };
        }

        /// <summary>Random source whose <see cref="IRandomSource.Chance"/> always returns true.</summary>
        private sealed class AlwaysTrueRandom : IRandomSource
        {
            public int Next(int min, int max) => min;

            public double NextUnit() => 0.0;

            public bool Chance(double p) => true;
        }

        #endregion Helpers
    }
}
