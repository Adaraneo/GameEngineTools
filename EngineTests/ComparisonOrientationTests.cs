// ComparisonOrientationTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Traits;
    using System;

    /// <summary>
    /// Tests for <see cref="ComparisonOrientationGenerator"/> and <see cref="ComparisonOrientationProfile"/>.
    /// Mirrors the structure of <see cref="ValuesProfileTests"/>.
    /// </summary>
    [TestClass]
    public class ComparisonOrientationTests : TestBase
    {
        #region Test 1 — High Neuroticism → Higher Overall SCO than Low Neuroticism

        /// <summary>
        /// A high-Neuroticism character must have a higher Overall SCO than a low-Neuroticism
        /// character (deterministic, null-random). Neuroticism is the primary predictor of the INCOM
        /// composite (Gibbons &amp; Buunk 1999, JPSP 76(1)).
        /// </summary>
        [TestMethod]
        public void ComparisonOrientation_HighNeuroticism_HigherOverallThanLow()
        {
            // Arrange — all other traits held at 0.5
            var highN = new BigFive(Openness: 0.5, Conscientiousness: 0.5,
                                   Extraversion: 0.5, Agreeableness: 0.5, Neuroticism: 0.9);
            var lowN = new BigFive(Openness: 0.5, Conscientiousness: 0.5,
                                   Extraversion: 0.5, Agreeableness: 0.5, Neuroticism: 0.1);

            // Act — deterministic (null random)
            var highProfile = ComparisonOrientationGenerator.Generate(highN, random: null);
            var lowProfile = ComparisonOrientationGenerator.Generate(lowN, random: null);

            // Assert — high N yields higher Overall SCO
            Assert.IsTrue(highProfile.Overall > lowProfile.Overall,
                $"High Neuroticism (N=0.9) should produce higher Overall SCO than Low Neuroticism (N=0.1). " +
                $"High={highProfile.Overall:F3}, Low={lowProfile.Overall:F3}");
        }

        #endregion Test 1 — High Neuroticism → Higher Overall SCO than Low Neuroticism

        #region Test 2 — Deterministic: two null-random Generate calls produce identical profiles

        /// <summary>
        /// Without a random source, <see cref="ComparisonOrientationGenerator.Generate"/> must
        /// return byte-identical profiles on repeated calls (pure function, no side effects).
        /// </summary>
        [TestMethod]
        public void ComparisonOrientation_Deterministic_WithNullRandom()
        {
            // Arrange
            var bigFive = new BigFive(Openness: 0.4, Conscientiousness: 0.6,
                                     Extraversion: 0.7, Agreeableness: 0.3, Neuroticism: 0.8);

            // Act — two calls, null random
            var p1 = ComparisonOrientationGenerator.Generate(bigFive, random: null);
            var p2 = ComparisonOrientationGenerator.Generate(bigFive, random: null);

            // Assert — byte-identical records
            Assert.AreEqual(p1, p2,
                "Null-random generation must be deterministic (no noise).");
        }

        #endregion Test 2 — Deterministic: two null-random Generate calls produce identical profiles

        #region Test 3 — All outputs in [0, 1]

        /// <summary>
        /// All three components of the generated profile must be in [0..1] regardless of
        /// extreme trait inputs.
        /// </summary>
        [TestMethod]
        public void ComparisonOrientation_AllComponents_InRange()
        {
            var extremes = new BigFive[]
            {
                new(0.0, 0.0, 0.0, 0.0, 0.0),
                new(1.0, 1.0, 1.0, 1.0, 1.0),
                new(0.5, 0.5, 0.5, 0.5, 0.5),
                new(0.0, 1.0, 0.0, 1.0, 0.0),
                new(1.0, 0.0, 1.0, 0.0, 1.0),
            };

            foreach (var bf in extremes)
            {
                // Deterministic
                var p = ComparisonOrientationGenerator.Generate(bf, random: null);

                Assert.IsTrue(p.Overall is >= 0.0 and <= 1.0,
                    $"Overall must be in [0,1]. Got {p.Overall:F4} for N={bf.Neuroticism}");
                Assert.IsTrue(p.Ability is >= 0.0 and <= 1.0,
                    $"Ability must be in [0,1]. Got {p.Ability:F4}");
                Assert.IsTrue(p.Opinion is >= 0.0 and <= 1.0,
                    $"Opinion must be in [0,1]. Got {p.Opinion:F4}");
            }
        }

        #endregion Test 3 — All outputs in [0, 1]

        #region Test 4 — Average character has Overall ≈ 0.5

        /// <summary>
        /// An average character (all Big Five traits = 0.5) with null random must produce
        /// Overall exactly 0.5 by construction of the formula.
        /// </summary>
        [TestMethod]
        public void ComparisonOrientation_AverageCharacter_OverallApproximatelyMidpoint()
        {
            var average = new BigFive(Openness: 0.5, Conscientiousness: 0.5,
                                     Extraversion: 0.5, Agreeableness: 0.5, Neuroticism: 0.5);

            var p = ComparisonOrientationGenerator.Generate(average, random: null);

            Assert.AreEqual(0.5, p.Overall, delta: 1e-10,
                $"Average character should produce Overall=0.5 by construction. Got: {p.Overall:F4}");
            Assert.AreEqual(0.5, p.Ability, delta: 1e-10,
                $"Average character should produce Ability=0.5 by construction. Got: {p.Ability:F4}");
            Assert.AreEqual(0.5, p.Opinion, delta: 1e-10,
                $"Average character should produce Opinion=0.5 by construction. Got: {p.Opinion:F4}");
        }

        #endregion Test 4 — Average character has Overall ≈ 0.5

        #region Test 5 (optional) — Population mean of Overall in plausible band

        /// <summary>
        /// Optional population check: across a sample of randomly generated characters,
        /// the mean Overall SCO should fall in a plausible band [0.45..0.65].
        /// </summary>
        [TestMethod]
        public void ComparisonOrientation_PopulationMean_InPlausibleBand()
        {
            const int n = 500;
            var rng = new Random(42);

            double sum = 0.0;
            for (var i = 0; i < n; i++)
            {
                var bf = new BigFive(
                    Openness: rng.NextDouble(),
                    Conscientiousness: rng.NextDouble(),
                    Extraversion: rng.NextDouble(),
                    Agreeableness: rng.NextDouble(),
                    Neuroticism: rng.NextDouble());

                sum += ComparisonOrientationGenerator.Generate(bf, rng).Overall;
            }

            var mean = sum / n;

            Assert.IsTrue(mean is >= 0.45 and <= 0.65,
                $"Population mean of Overall SCO should be in [0.45, 0.65]. Got: {mean:F4}");
        }

        #endregion Test 5 (optional) — Population mean of Overall in plausible band

        #region Test 6 — High Neuroticism → Higher Ability subscale than Low Neuroticism

        /// <summary>
        /// Ability subscale should also scale with Neuroticism (competence-monitoring anxiety).
        /// </summary>
        [TestMethod]
        public void ComparisonOrientation_HighNeuroticism_HigherAbilitySubscale()
        {
            var highN = new BigFive(Openness: 0.5, Conscientiousness: 0.5,
                                   Extraversion: 0.5, Agreeableness: 0.5, Neuroticism: 0.9);
            var lowN = new BigFive(Openness: 0.5, Conscientiousness: 0.5,
                                   Extraversion: 0.5, Agreeableness: 0.5, Neuroticism: 0.1);

            var highProfile = ComparisonOrientationGenerator.Generate(highN, random: null);
            var lowProfile = ComparisonOrientationGenerator.Generate(lowN, random: null);

            Assert.IsTrue(highProfile.Ability > lowProfile.Ability,
                $"High N should produce higher Ability SCO. High={highProfile.Ability:F3}, Low={lowProfile.Ability:F3}");
        }

        #endregion Test 6 — High Neuroticism → Higher Ability subscale than Low Neuroticism

        #region Test 7 — High Openness → Higher Opinion subscale than Low Openness

        /// <summary>
        /// Opinion subscale is slightly more Openness-loaded than Ability
        /// (curiosity about others' views).
        /// </summary>
        [TestMethod]
        public void ComparisonOrientation_HighOpenness_HigherOpinionSubscale()
        {
            var highO = new BigFive(Openness: 0.9, Conscientiousness: 0.5,
                                   Extraversion: 0.5, Agreeableness: 0.5, Neuroticism: 0.5);
            var lowO = new BigFive(Openness: 0.1, Conscientiousness: 0.5,
                                   Extraversion: 0.5, Agreeableness: 0.5, Neuroticism: 0.5);

            var highProfile = ComparisonOrientationGenerator.Generate(highO, random: null);
            var lowProfile = ComparisonOrientationGenerator.Generate(lowO, random: null);

            Assert.IsTrue(highProfile.Opinion > lowProfile.Opinion,
                $"High O should produce higher Opinion SCO. High={highProfile.Opinion:F3}, Low={lowProfile.Opinion:F3}");
        }

        #endregion Test 7 — High Openness → Higher Opinion subscale than Low Openness
    }
}
