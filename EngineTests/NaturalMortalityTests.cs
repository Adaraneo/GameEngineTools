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
        public void ComputeHourlyRisk_OldWithHighLoad_RiskNearMaximum()
        {
            var state = NominalState() with { AllostaticLoad = 95, ImmuneLoad = 80 };
            var risk = NaturalMortalityCalculator.ComputeHourlyRisk(state, ageYears: 80, cfg: DefaultCfg);

            Assert.IsTrue(risk >= DefaultCfg.NaturalMortalityMaxRiskPerHour * 0.95,
                $"80yo with extreme allostatic + immune load must reach near MaxRiskPerHour. Got {risk:F6}");
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
        public void ComputeHourlyRisk_AllostaticLoadAbove90_DramaticSpike()
        {
            // AlloLoad=92 (above 90 spike threshold) → raw contribution far exceeds MaxRisk → capped
            // AlloLoad=72 (above 70 linear threshold) → small contribution, below cap
            var stateHigh = NominalState() with { AllostaticLoad = 92 };
            var stateLow = NominalState() with { AllostaticLoad = 72 };

            var riskHigh = NaturalMortalityCalculator.ComputeHourlyRisk(stateHigh, ageYears: 30, cfg: DefaultCfg);
            var riskLow = NaturalMortalityCalculator.ComputeHourlyRisk(stateLow, ageYears: 30, cfg: DefaultCfg);

            Assert.AreEqual(DefaultCfg.NaturalMortalityMaxRiskPerHour, riskHigh, delta: 1e-10,
                $"AlloLoad=92 must saturate MaxRiskPerHour ({DefaultCfg.NaturalMortalityMaxRiskPerHour}). Got {riskHigh:F6}");
            Assert.IsTrue(riskLow < DefaultCfg.NaturalMortalityMaxRiskPerHour,
                $"AlloLoad=72 must stay below MaxRiskPerHour. Got {riskLow:F6}");
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
            var cause = NaturalMortalityCalculator.ResolveCause(state, ageYears: 30);

            Assert.AreEqual(DeathCause.Starvation, cause,
                "Hunger=100 + Thirst=100 must resolve to Starvation.");
        }

        [TestMethod]
        public void ResolveCause_ExhaustionTerminal_ReturnsExhaustion()
        {
            var state = NominalState() with { Energy = 0, SleepDebtHours = 60 };
            var cause = NaturalMortalityCalculator.ResolveCause(state, ageYears: 30);

            Assert.AreEqual(DeathCause.Exhaustion, cause,
                "Energy=0 + SleepDebt=60h must resolve to Exhaustion.");
        }

        [TestMethod]
        public void ResolveCause_SystemicOverload_ReturnsSystemicFailure()
        {
            var state = NominalState() with { AllostaticLoad = 100, ImmuneLoad = 90 };
            var cause = NaturalMortalityCalculator.ResolveCause(state, ageYears: 40);

            Assert.AreEqual(DeathCause.SystemicFailure, cause,
                "AlloLoad=100 + ImmuneLoad=90 must resolve to SystemicFailure.");
        }

        [TestMethod]
        public void ResolveCause_VeryOldOtherwiseHealthy_ReturnsOldAge()
        {
            var state = NominalState(); // nominal vitals, no extreme load
            var cause = NaturalMortalityCalculator.ResolveCause(state, ageYears: 105);

            Assert.AreEqual(DeathCause.OldAge, cause,
                "Very old character with nominal vitals must resolve to OldAge.");
        }

        [TestMethod]
        public void ResolveCause_StarvationBeatsExhaustion_WhenBothPresent()
        {
            // Both starvation AND exhaustion conditions true — Starvation has higher priority
            var state = NominalState() with { Hunger = 100, Thirst = 100, Energy = 0, SleepDebtHours = 60 };
            var cause = NaturalMortalityCalculator.ResolveCause(state, ageYears: 30);

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
