// TemporalDiscountingTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Tests for <see cref="DiscountedValueModifier"/> / <see cref="DiscountedValueMath"/> — hyperboloid
    /// temporal discounting (Green &amp; Myerson 2004), its sequencing after loss aversion, the
    /// per-agent <see cref="TemporalDiscountProfile"/> (independent of Big Five), and the optional
    /// quasi-hyperbolic present-bias mode.
    /// </summary>
    [TestClass]
    public class TemporalDiscountingTests : TestBase
    {
        [TestMethod]
        public void DiscountFactor_DecreasesWithDelay()
        {
            const double k = 0.05, s = 0.7;
            var delays = new[] { 0.0, 1.0, 5.0, 10.0, 50.0, 200.0 };

            double previous = double.PositiveInfinity;
            foreach (var d in delays)
            {
                var f = DiscountedValueMath.HyperboloidFactor(d, k, s);
                Assert.IsTrue(f < previous,
                    $"F(D) must be strictly decreasing in D. F({d})={f:F4} was not below previous {previous:F4}.");
                Assert.IsTrue(f > 0.0 && f <= 1.0, $"F({d})={f:F4} out of (0,1].");
                previous = f;
            }
        }

        [TestMethod]
        public void HyperboloidExponent_LessThanOne_DiscountsLessSteeplyThanExponential()
        {
            // With s < 1 the hyperboloid tail decays more slowly than a pure exponential e^{-kD};
            // at a large delay the hyperboloid factor must remain well above the exponential one.
            const double k = 0.1, s = 0.7, largeDelay = 100.0;

            var hyperboloid = DiscountedValueMath.HyperboloidFactor(largeDelay, k, s);
            var exponential = Math.Exp(-k * largeDelay);

            Assert.IsTrue(hyperboloid > exponential,
                $"Hyperboloid (s<1) should discount less steeply in the tail. " +
                $"Hyperboloid={hyperboloid:E3}, Exponential={exponential:E3}.");
        }

        [TestMethod]
        public void SequentialApplication_LossAversionThenDiscount_NoDoubleCounting()
        {
            // The discount must read the utility ALREADY transformed by loss aversion, not the raw value.
            // Reference = Work@50; Create@40 is a 10-point loss in a non-risky (Competence) domain.
            const double reference = 50.0, rawLoss = 40.0;
            var candidates = new List<BehaviorCandidate>
            {
                new(Work, reference, WTimeSpan.FromHours(2), BehaviorDomain.Competence),  // status quo
                new(Create, rawLoss, WTimeSpan.FromHours(24), BehaviorDomain.Competence)  // -10 loss, D = 1 day
            };

            var ctx = BuildContext(currentPlan: Work, neuroticism: 0.5);
            new LossAversionModifier().Modify(ctx, candidates);
            new DiscountedValueModifier().Modify(ctx, candidates);

            // Expected: loss-aversion-adjusted utility, THEN hyperboloid discount.
            // D is Duration.TotalDays — note the world calendar uses 26-hour days, so 24h ≈ 0.923 days.
            var lossAdjusted = reference + 1.96 * (rawLoss - reference);  // 50 + 1.96·(-10) = 30.4
            var delayDays = WTimeSpan.FromHours(24).TotalDays;
            var factor = 1.0 / Math.Pow(1.0 + 0.05 * delayDays, 0.7);    // Competence → domain mult 1.0
            var expected = lossAdjusted * factor;

            var create = candidates.First(c => c.Name == Create).Utility;
            Assert.AreEqual(expected, create, 0.01,
                $"Discount must apply to the loss-aversion-transformed utility. Got {create:F3}, expected {expected:F3}.");

            // Had the discount read the RAW 40 instead, it would land near 40·factor — assert it did not.
            Assert.IsTrue(Math.Abs(create - rawLoss * factor) > 1.0,
                "Discount appears to have read the raw utility instead of the transformed one (double-count / wrong order).");
        }

        [TestMethod]
        public void PerAgentK_IsIndependentOfBigFive()
        {
            // K is sampled lognormally with no Big Five input (Yeh, Myerson & Green 2021): correlation
            // between K and any trait must be near zero across a large sample.
            const int n = 200;
            var rng = new SeededRandom(1234);
            var cfg = new BehaviorConfig();

            var ks = new double[n];
            var traits = new double[5][];
            for (var t = 0; t < 5; t++) traits[t] = new double[n];

            for (var i = 0; i < n; i++)
            {
                // Vary Big Five freely; the generator must ignore it entirely.
                var bf = new[] { rng.NextUnit(), rng.NextUnit(), rng.NextUnit(), rng.NextUnit(), rng.NextUnit() };
                for (var t = 0; t < 5; t++) traits[t][i] = bf[t];

                var profile = TemporalDiscountGenerator.Generate(rng, cfg);
                Assert.IsTrue(profile.K > 0.0, $"K must be strictly positive, got {profile.K}.");
                ks[i] = profile.K;
            }

            for (var t = 0; t < 5; t++)
            {
                var r = Pearson(traits[t], ks);
                Assert.IsTrue(Math.Abs(r) < 0.25,
                    $"K must be ~uncorrelated with Big Five trait {t}: |r|={Math.Abs(r):F3} too large.");
            }
        }

        [TestMethod]
        public void QuasiHyperbolicMode_Toggle_ProducesPresentBiasDiscontinuity()
        {
            // β-δ mode: a present-valued (D=0) candidate is untouched, but any positive delay drops the
            // utility by the β jump — the intentional present-bias discontinuity.
            var cfg = new BehaviorConfig(UseQuasiHyperbolicMode: true, PresentBiasBeta: 0.75);
            var candidates = new List<BehaviorCandidate>
            {
                new(Idle, 50.0, WTimeSpan.Zero, BehaviorDomain.Competence),            // D = 0 (present)
                new(Work, 50.0, WTimeSpan.FromHours(0.024), BehaviorDomain.Competence) // D ≈ 0.001 day
            };

            new DiscountedValueModifier().Modify(BuildContext(Idle, neuroticism: 0.5, config: cfg), candidates);

            var present = candidates.First(c => c.Name == Idle).Utility;
            var delayed = candidates.First(c => c.Name == Work).Utility;

            Assert.AreEqual(50.0, present, 0.001, "A present-valued (D=0) candidate must be undiscounted.");
            Assert.AreEqual(50.0 * 0.75, delayed, 0.2, "An ε-delayed candidate drops to ≈ β × utility.");
            Assert.IsTrue(present - delayed > 10.0,
                $"Present bias must create a clear discontinuity at D=0. Present={present:F2}, Delayed={delayed:F2}.");
        }

        [TestMethod]
        public void Disabled_ConfigFlag_IsNoOp()
        {
            var cfg = new BehaviorConfig(TemporalDiscountingEnabled: false);
            var candidates = new List<BehaviorCandidate>
            {
                new(Work, 50.0, WTimeSpan.FromHours(48), BehaviorDomain.Competence) // long delay → would discount
            };

            new DiscountedValueModifier().Modify(BuildContext(Work, neuroticism: 0.5, config: cfg), candidates);

            Assert.AreEqual(50.0, candidates[0].Utility, 0.001,
                "With TemporalDiscountingEnabled = false the modifier must leave utility unchanged.");
        }

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

        private static BehaviorContext BuildContext(string currentPlan, double neuroticism, BehaviorConfig? config = null)
        {
            var personality = new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, neuroticism),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral);

            var snapshot = new EnginesSnapshot(
                new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null),
                new PsychologyState(0, 0.5, 0.5, 10, 0, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface("test", false, 0.2, 0.2, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            var human = new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("TemporalDiscounting"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };

            var state = new BehaviorState(10, 5, 5, 20, 50, 30,
                new PlannedAction(currentPlan, new WDateTime(0), WTimeSpan.FromHours(1), 50.0));

            return new BehaviorContext(
                new WDateTime(0),
                WTimeSpan.FromHours(1),
                human,
                new EventCollector(),
                state,
                config ?? new BehaviorConfig(),
                new Dictionary<string, double>());
        }

        private sealed class SeededRandom : IRandomSource
        {
            private readonly Random _r;
            public SeededRandom(int seed) => _r = new Random(seed);
            public int Next(int min, int max) => _r.Next(min, max);
            public double NextUnit() => _r.NextDouble();
            public bool Chance(double p) => _r.NextDouble() < p;
        }

        #endregion
    }
}
