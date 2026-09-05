// WantingLikingTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Objects;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Tests for Subsystem C (Wanting/Liking): <see cref="WantingSensitivityGenerator"/> trait
    /// generation, the cue-gated multiplicative <see cref="WantingGainModifier"/> (applied after
    /// discounting, only to present-cue candidates, off by default), and the consumption-time
    /// <c>LikingCapacity</c> scaling of hedonic impact in <see cref="DefaultPsychologyEngine"/>.
    /// </summary>
    [TestClass]
    public class WantingLikingTests : TestBase
    {
        #region Generator

        [TestMethod]
        public void WantingSensitivity_CorrelatesWithExtraversion_NotPerfectly()
        {
            const int n = 500;
            var rng = new SeededRandom(24680);

            var extraversion = new double[n];
            var wanting = new double[n];
            for (var i = 0; i < n; i++)
            {
                var bf = new BigFive(rng.NextUnit(), rng.NextUnit(), rng.NextUnit(), rng.NextUnit(), rng.NextUnit());
                var p = WantingSensitivityGenerator.Generate(rng, bf);
                extraversion[i] = bf.Extraversion;
                wanting[i] = p.WantingSensitivity;
            }

            var r = Pearson(extraversion, wanting);
            // Moderate positive bridge (Mitchell et al. 2007) but residual noise dominates — NOT r>0.5.
            Assert.IsTrue(r > 0.15 && r < 0.5,
                $"WantingSensitivity should correlate moderately (not perfectly) with Extraversion. Got r={r:F3}.");
        }

        [TestMethod]
        public void LikingCapacity_Uncorrelated_WithBigFive()
        {
            const int n = 500;
            var rng = new SeededRandom(11223);

            var traits = new double[5][];
            for (var t = 0; t < 5; t++) traits[t] = new double[n];
            var liking = new double[n];

            for (var i = 0; i < n; i++)
            {
                var vals = new[] { rng.NextUnit(), rng.NextUnit(), rng.NextUnit(), rng.NextUnit(), rng.NextUnit() };
                for (var t = 0; t < 5; t++) traits[t][i] = vals[t];
                var p = WantingSensitivityGenerator.Generate(rng, new BigFive(vals[0], vals[1], vals[2], vals[3], vals[4]));
                liking[i] = p.LikingCapacity;
            }

            for (var t = 0; t < 5; t++)
            {
                var r = Pearson(traits[t], liking);
                Assert.IsTrue(Math.Abs(r) < 0.15,
                    $"LikingCapacity must be ~uncorrelated with Big Five trait {t}: |r|={Math.Abs(r):F3} too large.");
            }
        }

        [TestMethod]
        public void WantingGain_DoesNotDuplicate_RegulatoryFocusPromotion()
        {
            // Redundancy-audit benchmark: Wanting and RegulatoryFocus.Promotion are distinct constructs.
            // They share Extraversion(+) but differ on Conscientiousness/Agreeableness sign, and each
            // carries heavy independent noise → trait-level correlation must stay well below 0.7.
            const int n = 500;
            var rng = new SeededRandom(31415);

            var wanting = new double[n];
            var promotion = new double[n];
            for (var i = 0; i < n; i++)
            {
                var bf = new BigFive(rng.NextUnit(), rng.NextUnit(), rng.NextUnit(), rng.NextUnit(), rng.NextUnit());
                wanting[i] = WantingSensitivityGenerator.Generate(rng, bf).WantingSensitivity;
                promotion[i] = RegulatoryFocusGenerator.Generate(rng, bf).Promotion;
            }

            var r = Pearson(wanting, promotion);
            Assert.IsTrue(r < 0.7,
                $"Wanting must not be redundant with RegulatoryFocus.Promotion (r<0.7). Got r={r:F3}.");
        }

        #endregion Generator

        #region Wanting gain modifier

        [TestMethod]
        public void WantingGain_DisabledByDefault_IsNoOp()
        {
            // Default config has WantingGainEnabled = false — no change even with a cue and a profile.
            var candidates = new List<BehaviorCandidate>
            {
                new(Eat, 50.0, WTimeSpan.Zero, BehaviorDomain.Physiological)
            };
            var ctx = BuildContext(
                wanting: new WantingSensitivityProfile(WantingSensitivity: 1.0, LikingCapacity: 0.5),
                availableObjects: new[] { FoodObject() });

            new WantingGainModifier().Modify(ctx, candidates);

            Assert.AreEqual(50.0, candidates[0].Utility, 0.001,
                "WantingGainEnabled = false must leave utility unchanged.");
        }

        [TestMethod]
        public void WantingGain_OnlyAffectsCueTriggeredCandidates()
        {
            // Only a present reward cue (a food object affording Eat) qualifies; Work has no present cue.
            var cfg = new BehaviorConfig(WantingGainEnabled: true);
            var candidates = new List<BehaviorCandidate>
            {
                new(Eat, 50.0, WTimeSpan.Zero, BehaviorDomain.Physiological),   // cue present → gain
                new(Work, 50.0, WTimeSpan.Zero, BehaviorDomain.Competence)      // no cue → unchanged
            };
            var ctx = BuildContext(
                wanting: new WantingSensitivityProfile(WantingSensitivity: 0.8, LikingCapacity: 0.5),
                availableObjects: new[] { FoodObject() },
                config: cfg);

            new WantingGainModifier().Modify(ctx, candidates);

            var kappa = 1.0 + 0.8 * 0.5; // 1.4
            Assert.AreEqual(50.0 * kappa, candidates.First(c => c.Name == Eat).Utility, 0.001,
                "Cue-triggered Eat receives the κ gain.");
            Assert.AreEqual(50.0, candidates.First(c => c.Name == Work).Utility, 0.001,
                "Non-cue Work is untouched.");
        }

        [TestMethod]
        public void WantingGain_AppliesAfterDiscounting_NotBefore()
        {
            var cfg = new BehaviorConfig(WantingGainEnabled: true); // discounting on by default
            var candidates = new List<BehaviorCandidate>
            {
                new(Eat, 50.0, WTimeSpan.FromHours(24), BehaviorDomain.Physiological) // delayed + cue-relevant
            };
            var ctx = BuildContext(
                wanting: new WantingSensitivityProfile(WantingSensitivity: 0.8, LikingCapacity: 0.5),
                availableObjects: new[] { FoodObject() },
                config: cfg);

            new DiscountedValueModifier().Modify(ctx, candidates);
            var discounted = candidates[0].Utility;       // < 50 (delayed)
            new WantingGainModifier().Modify(ctx, candidates);
            var final = candidates[0].Utility;

            var kappa = 1.0 + 0.8 * 0.5; // 1.4
            Assert.AreEqual(discounted * kappa, final, 0.001,
                "κ must multiply the already-discounted utility.");
            Assert.IsTrue(Math.Abs(final - 50.0 * kappa) > 0.01,
                "κ applied to the raw (undiscounted) value would indicate wrong ordering.");
        }

        #endregion Wanting gain modifier

        #region Liking — consumption-time hedonic impact

        [TestMethod]
        public void LikingCapacity_ScalesHedonicImpact()
        {
            var highLiking = ApplyMoodBoost(new WantingSensitivityProfile(WantingSensitivity: 0.5, LikingCapacity: 1.0));
            var lowLiking = ApplyMoodBoost(new WantingSensitivityProfile(WantingSensitivity: 0.5, LikingCapacity: 0.0));

            Assert.IsTrue(highLiking > lowLiking,
                $"Higher LikingCapacity yields a larger hedonic (valence) impact. " +
                $"High={highLiking:F4}, Low={lowLiking:F4}.");
        }

        [TestMethod]
        public void LikingCapacity_NullProfile_LeavesMoodBoostUnchanged()
        {
            // Null profile must reproduce full (factor 1.0) hedonic impact — same as LikingCapacity = 1.0.
            var nullProfile = ApplyMoodBoost(profile: null);
            var fullLiking = ApplyMoodBoost(new WantingSensitivityProfile(WantingSensitivity: 0.5, LikingCapacity: 1.0));

            Assert.AreEqual(fullLiking, nullProfile, 1e-9,
                "Null WantingSensitivity must leave the MoodBoost impact unscaled (factor 1.0).");
        }

        /// <summary>
        /// Restores a known psychology state, delivers a MoodBoost <see cref="ObjectAffordanceApplied"/>
        /// to a character with the given profile, and returns the resulting Valence.
        /// </summary>
        private static double ApplyMoodBoost(WantingSensitivityProfile? profile)
        {
            var cfg = new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false);

            var engine = new DefaultPsychologyEngine(
                Options.Create(cfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new ZeroRandom());
            engine.RestoreState(new PsychologyState(0.0, 0.4, 0.5, 10, 10, DiscreteEmotion.Neutral));

            var ctx = BuildHumanContext(profile);
            var ev = new ObjectAffordanceApplied(new WDateTime(0), ctx.Id, "treat_01", AffordanceType.MoodBoost, 0.8);
            engine.Handle(ev, ctx, new EventCollector());

            return engine.State.Valence;
        }

        #endregion Liking — consumption-time hedonic impact

        #region Helpers

        private static double Pearson(double[] x, double[] y)
        {
            var n = x.Length;
            double mx = x.Average(), my = y.Average();
            double cov = 0, vx = 0, vy = 0;
            for (var i = 0; i < n; i++)
            {
                var dx = x[i] - mx;
                var dy = y[i] - my;
                cov += dx * dy;
                vx += dx * dx;
                vy += dy * dy;
            }
            var denom = Math.Sqrt(vx * vy);
            return denom <= 0 ? 0.0 : cov / denom;
        }

        private static WorldObject FoodObject() => new WorldObject
        {
            Id = "food_01",
            DisplayName = "bread",
            Category = WorldObjectCategory.Food,
            LocationId = "test",
            IsAvailable = true,
            Affordances = ImmutableArray.Create(new WorldObjectAffordance(AffordanceType.Hunger, 0.8))
        };

        private static Personality MakePersonality(WantingSensitivityProfile? wanting)
            => new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral,
                WantingSensitivity: wanting);

        private static HumanContext BuildHumanContext(WantingSensitivityProfile? wanting)
        {
            var personality = MakePersonality(wanting);
            var snapshot = new EnginesSnapshot(
                new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null),
                new PsychologyState(0, 0.5, 0.5, 10, 0, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface("test", false, 0.2, 0.2, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("WantingLiking"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        private static BehaviorContext BuildContext(
            WantingSensitivityProfile? wanting,
            IReadOnlyList<WorldObject>? availableObjects = null,
            BehaviorConfig? config = null)
        {
            var human = BuildHumanContext(wanting);
            var state = new BehaviorState(10, 5, 5, 20, 50, 30,
                new PlannedAction(Idle, new WDateTime(0), WTimeSpan.FromHours(1), 50.0));

            return new BehaviorContext(
                new WDateTime(0),
                WTimeSpan.FromHours(1),
                human,
                new EventCollector(),
                state,
                config ?? new BehaviorConfig(),
                new Dictionary<string, double>(),
                AvailableObjects: availableObjects);
        }

        private sealed class SeededRandom : IRandomSource
        {
            private readonly Random _r;

            public SeededRandom(int seed) => _r = new Random(seed);

            public int Next(int min, int max) => _r.Next(min, max);

            public double NextUnit() => _r.NextDouble();

            public bool Chance(double p) => _r.NextDouble() < p;
        }

        #endregion Helpers
    }
}
