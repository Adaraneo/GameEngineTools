// RegulatoryFocusTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
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
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Tests for <see cref="RegulatoryFocusProfile"/> generation (Big Five directions, near-orthogonal
    /// Promotion/Prevention), its modulation of effective loss-aversion λ inside
    /// <see cref="LossAversionModifier"/>, and the optional feature-flagged
    /// <see cref="RegulatoryFitModifier"/>.
    /// </summary>
    [TestClass]
    public class RegulatoryFocusTests : TestBase
    {
        #region Generator

        [TestMethod]
        public void RegulatoryFocusGenerator_Promotion_CorrelatesWithExtraversion()
        {
            const int n = 500;
            var rng = new SeededRandom(98765);

            var extraversion = new double[n];
            var promotion = new double[n];
            for (var i = 0; i < n; i++)
            {
                var bf = new BigFive(rng.NextUnit(), rng.NextUnit(), rng.NextUnit(), rng.NextUnit(), rng.NextUnit());
                var rf = RegulatoryFocusGenerator.Generate(rng, bf);
                extraversion[i] = bf.Extraversion;
                promotion[i] = rf.Promotion;
            }

            var r = Pearson(extraversion, promotion);
            // Extraversion is the dominant, verified Promotion predictor (ρ≈.36, Lanaj et al. 2012).
            // Wide residual noise pulls the realised correlation around that anchor — tolerate ±0.15.
            Assert.AreEqual(0.36, r, 0.15,
                $"Promotion should correlate ~0.36 with Extraversion. Got r={r:F3}.");
        }

        [TestMethod]
        public void RegulatoryFocusGenerator_PromotionPrevention_NearOrthogonal()
        {
            const int n = 500;
            var rng = new SeededRandom(13579);

            var promotion = new double[n];
            var prevention = new double[n];
            for (var i = 0; i < n; i++)
            {
                var bf = new BigFive(rng.NextUnit(), rng.NextUnit(), rng.NextUnit(), rng.NextUnit(), rng.NextUnit());
                var rf = RegulatoryFocusGenerator.Generate(rng, bf);
                promotion[i] = rf.Promotion;
                prevention[i] = rf.Prevention;
            }

            var r = Pearson(promotion, prevention);
            // Higgins' foci are near-independent (ρ≈.11), NOT a bipolar negative scale.
            Assert.IsTrue(Math.Abs(r) < 0.2,
                $"Promotion and Prevention must be near-orthogonal (|r|<0.2). Got r={r:F3}.");
            Assert.IsTrue(r > -0.3,
                $"Promotion/Prevention must NOT be strongly negative (bipolar). Got r={r:F3}.");
        }

        #endregion Generator

        #region Loss-aversion λ modulation

        [TestMethod]
        public void LossAversion_PreventionFocus_IncreasesEffectiveLambda()
        {
            var preventionLoss = RunLoss(new RegulatoryFocusProfile(Promotion: 0.1, Prevention: 0.9));
            var baselineLoss = RunLoss(regulatoryFocus: null);

            Assert.IsTrue(preventionLoss < baselineLoss,
                $"High Prevention raises effective λ → steeper loss (lower utility). " +
                $"Prevention={preventionLoss:F2}, Baseline={baselineLoss:F2}.");
        }

        [TestMethod]
        public void LossAversion_PromotionFocus_DecreasesEffectiveLambda()
        {
            var promotionLoss = RunLoss(new RegulatoryFocusProfile(Promotion: 0.9, Prevention: 0.1));
            var baselineLoss = RunLoss(regulatoryFocus: null);

            Assert.IsTrue(promotionLoss > baselineLoss,
                $"High Promotion lowers effective λ → shallower loss (higher utility). " +
                $"Promotion={promotionLoss:F2}, Baseline={baselineLoss:F2}.");
        }

        [TestMethod]
        public void LossAversion_NullRegulatoryFocus_FallsBackToBaseline()
        {
            // With null RegulatoryFocus the modifier must reproduce the pre-Subsystem-B λ = 1.96 result.
            var loss = RunLoss(regulatoryFocus: null);
            const double reference = 50.0, raw = 40.0;
            var expected = reference + 1.96 * (raw - reference); // 30.4

            Assert.AreEqual(expected, loss, 0.001,
                $"Null RegulatoryFocus must leave λ at its baseline (1.96) value. Got {loss:F3}.");
        }

        /// <summary>
        /// Applies <see cref="LossAversionModifier"/> to a fixed −10 loss candidate (Competence domain,
        /// reference = Work@50, Neuroticism = 0.5) and returns the loss candidate's resulting utility.
        /// </summary>
        private static double RunLoss(RegulatoryFocusProfile? regulatoryFocus)
        {
            var candidates = new List<BehaviorCandidate>
            {
                new(Work, 50.0, WTimeSpan.FromHours(2), BehaviorDomain.Competence), // reference
                new(Create, 40.0, WTimeSpan.FromHours(2), BehaviorDomain.Competence) // −10 loss
            };
            new LossAversionModifier().Modify(BuildContext(Work, neuroticism: 0.5, regulatoryFocus: regulatoryFocus), candidates);
            return candidates.First(c => c.Name == Create).Utility;
        }

        #endregion Loss-aversion λ modulation

        #region Regulatory fit bonus

        [TestMethod]
        public void RegulatoryFit_DisabledByDefault_IsNoOp()
        {
            var candidates = new List<BehaviorCandidate>
            {
                new(Create, 50.0, WTimeSpan.FromHours(2), BehaviorDomain.Competence),     // eager
                new(Flee, 50.0, WTimeSpan.FromHours(1), BehaviorDomain.Physiological)      // vigilant
            };

            // Default config has RegulatoryFitEnabled = false.
            var ctx = BuildContext(Work, neuroticism: 0.5,
                regulatoryFocus: new RegulatoryFocusProfile(0.8, 0.8));
            new RegulatoryFitModifier().Modify(ctx, candidates);

            Assert.AreEqual(50.0, candidates.First(c => c.Name == Create).Utility, 0.001);
            Assert.AreEqual(50.0, candidates.First(c => c.Name == Flee).Utility, 0.001);
        }

        [TestMethod]
        public void RegulatoryFit_WhenEnabled_BonusScalesWithFocus()
        {
            var cfg = new BehaviorConfig(RegulatoryFitEnabled: true); // RegulatoryFitBonusMagnitude default 3.0
            var rf = new RegulatoryFocusProfile(Promotion: 0.8, Prevention: 0.4);
            var candidates = new List<BehaviorCandidate>
            {
                new(Create, 50.0, WTimeSpan.FromHours(2), BehaviorDomain.Competence),  // eager → Promotion×3
                new(Flee, 50.0, WTimeSpan.FromHours(1), BehaviorDomain.Physiological), // vigilant → Prevention×3
                new(Work, 50.0, WTimeSpan.FromHours(2), BehaviorDomain.Competence)     // neutral → no bonus
            };

            new RegulatoryFitModifier().Modify(
                BuildContext(Work, neuroticism: 0.5, regulatoryFocus: rf, config: cfg), candidates);

            Assert.AreEqual(50.0 + 0.8 * 3.0, candidates.First(c => c.Name == Create).Utility, 0.001,
                "Eager action receives a Promotion-scaled bonus.");
            Assert.AreEqual(50.0 + 0.4 * 3.0, candidates.First(c => c.Name == Flee).Utility, 0.001,
                "Vigilant action receives a Prevention-scaled bonus.");
            Assert.AreEqual(50.0, candidates.First(c => c.Name == Work).Utility, 0.001,
                "Neutral-strategy action receives no fit bonus.");
        }

        #endregion Regulatory fit bonus

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

        private static BehaviorContext BuildContext(
            string currentPlan,
            double neuroticism,
            RegulatoryFocusProfile? regulatoryFocus = null,
            BehaviorConfig? config = null)
        {
            var personality = new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, neuroticism),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral,
                RegulatoryFocus: regulatoryFocus);

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
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("RegulatoryFocus"),
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

        #endregion Helpers
    }
}
