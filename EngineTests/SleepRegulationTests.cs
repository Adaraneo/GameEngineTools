// SleepRegulationTests.cs
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

    /// <summary>
    /// Tests for the Borbély two-process sleep model (<see cref="SleepRegulationCalculator"/>) and the
    /// Van Dongen cognitive dose-response, plus the Psychology CognitiveLoad integration.
    /// </summary>
    [TestClass]
    public class SleepRegulationTests : TestBase
    {
        private const double HoursPerDay = 24.0;

        #region Process S time constants

        [TestMethod]
        public void ProcessS_BuildupAndDecay_FollowTimeConstants()
        {
            var cfg = new PhysiologyConfig();

            // After exactly one time constant, a saturating rise from 0 reaches 1 − 1/e ≈ 0.632.
            var afterBuildupTau = SleepRegulationCalculator.BuildupProcessS(0.0, cfg.ProcessSBuildupTimeConstantHours, cfg);
            Assert.AreEqual(1.0 - Math.Exp(-1.0), afterBuildupTau, 0.001,
                $"Process S after one buildup τ should be ≈0.632. Got {afterBuildupTau:F3}");

            // After one decay time constant, a fall from 1 reaches 1/e ≈ 0.368.
            var afterDecayTau = SleepRegulationCalculator.DecayProcessS(1.0, cfg.ProcessSDecayTimeConstantHours, cfg);
            Assert.AreEqual(Math.Exp(-1.0), afterDecayTau, 0.001,
                $"Process S after one decay τ should be ≈0.368. Got {afterDecayTau:F3}");
        }

        [TestMethod]
        public void ProcessS_DecayIsFasterThanBuildup()
        {
            var cfg = new PhysiologyConfig();
            Assert.IsTrue(cfg.ProcessSDecayTimeConstantHours < cfg.ProcessSBuildupTimeConstantHours,
                "Sleep discharges S faster than wake charges it (4.2 h vs 18.2 h).");

            // Over the same elapsed hours, decay covers more of its range than buildup covers of its.
            const double dt = 6.0;
            var builtFraction = SleepRegulationCalculator.BuildupProcessS(0.0, dt, cfg); // fraction of [0..1] gained
            var decayedFraction = 1.0 - SleepRegulationCalculator.DecayProcessS(1.0, dt, cfg); // fraction of [1..0] lost
            Assert.IsTrue(decayedFraction > builtFraction,
                $"Over {dt} h, decay ({decayedFraction:F3}) should outpace buildup ({builtFraction:F3}).");
        }

        #endregion Process S time constants

        #region Homeostatic / circadian dissociation

        [TestMethod]
        public void SleepPropensity_DeprivedAtThePeak_IsStillSleepierThan_RestedAtTheTrough()
        {
            var cfg = new PhysiologyConfig();

            // Rested (low S) at the circadian trough (04:00): S is far below the sleep-onset threshold H(t) = 0.55 there, so sleep
            // cannot start yet (sleep onset needs S to reach H, Daan, Beersma & Borbély 1984).
            var restedTrough = SleepRegulationCalculator.ComputeSleepPropensity(0.30, 4.0, HoursPerDay, 0.0, cfg);

            // Deprived (high S) at the afternoon peak of the threshold (16:00, H = 0.79): propensity is still positive.
            var deprivedPeak = SleepRegulationCalculator.ComputeSleepPropensity(0.95, cfg.ProcessCPeakHour, HoursPerDay, 0.0, cfg);

            Assert.IsTrue(restedTrough < 0, $"A rested character cannot fall asleep at 04:00 ({restedTrough:F3}).");
            Assert.IsTrue(deprivedPeak > 0, $"A sleep-deprived character is sleepy even at the circadian peak ({deprivedPeak:F3}); the circadian swing (2 x 0.12) cannot offset high pressure.");
        }

        [TestMethod]
        public void DebtFreeLevel_IsTheSameOnEveryDayLength_WhenTheBodyClockFollowsTheDay()
        {
            var cfg = new PhysiologyConfig();
            var earth = SleepRegulationCalculator.DebtFreeLevel(cfg, 24, 1.0);

            foreach (var hoursPerDay in new[] { 12, 20, 26, 32, 40, 60 })
            {
                var scaled = SleepRegulationCalculator.DebtFreeLevel(cfg, hoursPerDay, hoursPerDay / 24.0);
                Assert.AreEqual(earth, scaled, 1e-9, $"{hoursPerDay} h day with a day-following body clock.");
            }
        }

        [TestMethod]
        public void ProcessS_TimeConstants_StretchWithTheBodyClockScale()
        {
            var cfg = new PhysiologyConfig();
            var earthSixteen = SleepRegulationCalculator.BuildupProcessS(0.1, 16, cfg);
            var longDaySame = SleepRegulationCalculator.BuildupProcessS(0.1, 16 * 40.0 / 24.0, cfg, 40.0 / 24.0);
            var earthDecay = SleepRegulationCalculator.DecayProcessS(0.65, 8, cfg);
            var longDecay = SleepRegulationCalculator.DecayProcessS(0.65, 8 * 40.0 / 24.0, cfg, 40.0 / 24.0);

            Assert.AreEqual(earthSixteen, longDaySame, 1e-9, "26.7 h awake on a 40 h day is what 16 h awake is on Earth.");
            Assert.AreEqual(earthDecay, longDecay, 1e-9, "13.3 h asleep on a 40 h day is what 8 h asleep is on Earth.");
        }

        [TestMethod]
        public void CircadianThresholds_FollowDaan1984_AndPeakInTheAfternoon()
        {
            var cfg = new PhysiologyConfig();
            double H(double hour) => SleepRegulationCalculator.CircadianThreshold(hour, HoursPerDay, 0.0, cfg);
            double L(double hour) => SleepRegulationCalculator.WakeThreshold(hour, HoursPerDay, 0.0, cfg);

            Assert.AreEqual(0.79, H(cfg.ProcessCPeakHour), 1e-9, "H0+ 0.67 + a 0.12");
            Assert.AreEqual(0.55, H(cfg.ProcessCPeakHour + HoursPerDay / 2.0), 1e-9, "H0+ 0.67 - a 0.12, twelve hours later");
            Assert.AreEqual(0.29, L(cfg.ProcessCPeakHour), 1e-9, "H0- 0.17 + a 0.12");
            Assert.AreEqual(0.05, L(cfg.ProcessCPeakHour + HoursPerDay / 2.0), 1e-9, "H0- 0.17 - a 0.12");
        }

        /// <summary>
        /// Hours slept from <paramref name="bedtimeClock24"/> until S has decayed to the waking threshold (not before 4 h).
        /// Process S before bed is built up from 0.1 over <paramref name="awakeHours"/>.
        /// </summary>
        private static double SimulateSleepLength(double bedtimeClock24, double awakeHours)
        {
            var cfg = new PhysiologyConfig();
            var s = SleepRegulationCalculator.BuildupProcessS(0.1, awakeHours, cfg);
            const double step = 0.05;
            for (var t = step; t < 24; t += step)
            {
                s = SleepRegulationCalculator.DecayProcessS(s, step, cfg);
                var hour = (bedtimeClock24 + t) % 24.0;
                if (t >= 4.0 && s <= SleepRegulationCalculator.WakeThreshold(hour, 24, 0.0, cfg))
                    return t;
            }

            return 24;
        }

        [TestMethod]
        public void NormalNight_LastsAboutEightHours_AndWakesAroundSevenOrEight()
        {
            // Zulley, Wever & Aschoff (1981): in synchronised subjects sleep began ~1.3 h before the temperature minimum and ended ~6.9 h
            // after it, i.e. lasted ~8.3 h.
            var length = SimulateSleepLength(bedtimeClock24: 23.0, awakeHours: 16);

            Assert.IsTrue(length is > 7.0 and < 9.5, $"A normal night lasted {length:F1} h.");
        }

        [TestMethod]
        public void SleepAfter40HoursAwake_IsOnlyAboutAnHourLonger_ThanAfter16()
        {
            // Åkerstedt & Gillberg (1986): after 40 h awake sleep at 23:00 lasted only about one hour longer than after 16 h awake, because
            // the circadian waking threshold ends it; a pure homeostatic model would predict many more hours.
            var normal = SimulateSleepLength(23.0, awakeHours: 16);
            var deprived = SimulateSleepLength(23.0, awakeHours: 40);

            Assert.IsTrue(deprived - normal is > 0.3 and < 2.0, $"Normal {normal:F1} h, after 40 h awake {deprived:F1} h.");
        }

        [TestMethod]
        public void SleepStartingAfterTheCircadianMinimum_IsShortened()
        {
            // Zulley, Wever & Aschoff (1981): sleep that begins several hours after the temperature minimum lasts less than ~4-5 h,
            // sleep that begins well before it can last 15 h or more.
            var morning = SimulateSleepLength(bedtimeClock24: 8.0, awakeHours: 24);
            var night = SimulateSleepLength(bedtimeClock24: 23.0, awakeHours: 16);

            Assert.IsTrue(morning < 5.5, $"Sleep starting at 08:00 lasted {morning:F1} h.");
            Assert.IsTrue(night - morning > 2.5, $"Night {night:F1} h vs morning {morning:F1} h.");
        }

        #endregion Homeostatic / circadian dissociation

        #region Van Dongen chronic restriction

        [TestMethod]
        public void CognitiveDeficit_Chronic6hRestriction_GrowsMonotonically_WhileSDoesNotDiverge()
        {
            var cfg = new PhysiologyConfig();
            const int days = 14;
            const int awakeHours = 18;  // 6 h sleep opportunity per 24 h day
            const int sleepHours = 6;

            double s = 0.3;
            double deficit = 0.0;
            double maxS = s;
            var endOfDayDeficit = new List<double>();

            for (var day = 0; day < days; day++)
            {
                for (var h = 0; h < awakeHours; h++)
                {
                    s = SleepRegulationCalculator.BuildupProcessS(s, 1.0, cfg);
                    deficit = SleepRegulationCalculator.UpdateCognitiveDeficit(deficit, s, 1.0, asleep: false, cfg);
                    maxS = Math.Max(maxS, s);
                }
                for (var h = 0; h < sleepHours; h++)
                {
                    s = SleepRegulationCalculator.DecayProcessS(s, 1.0, cfg);
                    deficit = SleepRegulationCalculator.UpdateCognitiveDeficit(deficit, s, 1.0, asleep: true, cfg);
                    maxS = Math.Max(maxS, s);
                }
                endOfDayDeficit.Add(deficit);
            }

            // Process S saturates — it must NOT diverge past its ceiling.
            Assert.IsTrue(maxS <= cfg.ProcessSUpperAsymptote + 1e-9,
                $"Process S must stay bounded by its asymptote. Max was {maxS:F3}.");

            // Behavioural deficit must grow monotonically day over day under chronic restriction.
            for (var i = 1; i < endOfDayDeficit.Count; i++)
            {
                Assert.IsTrue(endOfDayDeficit[i] > endOfDayDeficit[i - 1],
                    $"Deficit must grow each day under 6 h restriction. Day {i}: {endOfDayDeficit[i]:F4} " +
                    $"vs day {i - 1}: {endOfDayDeficit[i - 1]:F4}");
            }
            Assert.IsTrue(endOfDayDeficit[^1] > endOfDayDeficit[0],
                "Cumulative deficit after 14 days must clearly exceed day-1 deficit.");
        }

        [TestMethod]
        public void CognitiveDeficit_AdequateSleep_RecoversToZero()
        {
            var cfg = new PhysiologyConfig();
            // Start with an accumulated deficit, then sleep 8 h.
            double deficit = 0.3;
            for (var h = 0; h < 8; h++)
                deficit = SleepRegulationCalculator.UpdateCognitiveDeficit(deficit, 0.2, 1.0, asleep: true, cfg);

            Assert.IsTrue(deficit < 0.15,
                $"8 h of sleep should substantially recover the cognitive deficit. Got {deficit:F3}.");
        }

        #endregion Van Dongen chronic restriction

        #region Psychology integration

        [TestMethod]
        public void Psychology_HighCognitiveDeficit_RaisesCognitiveLoad()
        {
            var withDeficit = TickCogLoadOnce(cognitiveDeficit: 0.8);
            var withoutDeficit = TickCogLoadOnce(cognitiveDeficit: 0.0);

            Assert.IsTrue(withDeficit > withoutDeficit,
                $"A high Van Dongen cognitive deficit must raise CognitiveLoad. " +
                $"WithDeficit={withDeficit:F2}, WithoutDeficit={withoutDeficit:F2}");
        }

        private double TickCogLoadOnce(double cognitiveDeficit)
        {
            var cfg = new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false);
            var engine = new DefaultPsychologyEngine(
                Options.Create(cfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new ZeroRandom());
            engine.RestoreState(new PsychologyState(
                Valence: 0.0, Arousal: 0.4, Dominance: 0.5,
                Stress: 0, CognitiveLoad: 0, DominantEmotion: DiscreteEmotion.Neutral));

            var ctx = BuildContext(cognitiveDeficit);
            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), ctx, new EventCollector());
            return engine.State.CognitiveLoad;
        }

        private static IHumanContext BuildContext(double cognitiveDeficit)
        {
            var self = new HumanId(Guid.NewGuid());
            var personality = new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral);

            var physio = new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null, CognitiveDeficit: cognitiveDeficit);
            var psych = new PsychologyState(0.0, 0.4, 0.5, 0, 0, DiscreteEmotion.Neutral);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.1, 0.1, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));
            return new HumanContext
            {
                Id = self,
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        #endregion Psychology integration
    }
}
