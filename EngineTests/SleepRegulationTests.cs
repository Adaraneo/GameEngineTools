// SleepRegulationTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
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

        #endregion

        #region Homeostatic / circadian dissociation

        [TestMethod]
        public void SleepPropensity_RestedAtTrough_SleepierThan_DeprivedAtPeak()
        {
            var cfg = new PhysiologyConfig();

            // Rested character (low S) at the circadian trough (night ~3:00).
            var thresholdTrough = SleepRegulationCalculator.CircadianThreshold(3.0, HoursPerDay, 0.0, cfg);
            var restedTrough = SleepRegulationCalculator.SleepPropensity(0.30, thresholdTrough);

            // Sleep-deprived character (high S) at the circadian alerting peak (afternoon).
            var thresholdPeak = SleepRegulationCalculator.CircadianThreshold(cfg.ProcessCPeakHour, HoursPerDay, 0.0, cfg);
            var deprivedPeak = SleepRegulationCalculator.SleepPropensity(0.95, thresholdPeak);

            Assert.IsTrue(restedTrough > deprivedPeak,
                $"Rested-at-trough propensity ({restedTrough:F3}) must exceed deprived-at-peak ({deprivedPeak:F3}) — " +
                "the dissociation the old flat sinusoid could not produce.");
            Assert.IsTrue(restedTrough > 0, "A rested character at the circadian trough is still net sleepy.");
        }

        #endregion

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

        #endregion

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

        #endregion
    }
}
