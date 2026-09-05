// DarkCoreTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;
    using System;
    using System.Linq;

    /// <summary>
    /// Tests for <see cref="DarkCoreGenerator"/> — the generation of the dark-core (D-factor)
    /// axis from Big Five traits and biological sex.
    /// </summary>
    [TestClass]
    public class DarkCoreTests : TestBase
    {
        #region Test 1 — Low Agreeableness → higher DarkCore than high Agreeableness

        [TestMethod]
        public void LowAgreeableness_ProducesHigherDarkCore_ThanHighAgreeableness()
        {
            // Arrange — two characters differing only in Agreeableness.
            var lowA = new BigFive(Openness: 0.5, Conscientiousness: 0.5,
                                   Extraversion: 0.5, Agreeableness: 0.1, Neuroticism: 0.5);
            var highA = new BigFive(Openness: 0.5, Conscientiousness: 0.5,
                                    Extraversion: 0.5, Agreeableness: 0.9, Neuroticism: 0.5);

            // Act — deterministic (null random) so the comparison is unambiguous.
            var profileLowA = DarkCoreGenerator.Generate(lowA, SexBiology.Female, random: null);
            var profileHighA = DarkCoreGenerator.Generate(highA, SexBiology.Female, random: null);

            // Assert — low-A character must score higher on DarkCore.
            Assert.IsTrue(profileLowA.DarkCore > profileHighA.DarkCore,
                $"Low-A DarkCore ({profileLowA.DarkCore:F3}) must exceed high-A DarkCore ({profileHighA.DarkCore:F3}).");
        }

        #endregion Test 1 — Low Agreeableness → higher DarkCore than high Agreeableness

        #region Test 2 — Male biology shifts DarkCore higher than female for identical Big Five

        [TestMethod]
        public void MaleBiology_HigherDarkCore_ThanFemale_SameBigFive()
        {
            var bigFive = new BigFive(Openness: 0.5, Conscientiousness: 0.5,
                                      Extraversion: 0.5, Agreeableness: 0.5, Neuroticism: 0.5);

            // Act — null random keeps residuals at zero; only the sex constant differs.
            var male = DarkCoreGenerator.Generate(bigFive, SexBiology.Male, random: null);
            var female = DarkCoreGenerator.Generate(bigFive, SexBiology.Female, random: null);

            // Assert — male is higher (Muris et al. 2017).
            Assert.IsTrue(male.DarkCore > female.DarkCore,
                $"Male DarkCore ({male.DarkCore:F3}) must exceed female DarkCore ({female.DarkCore:F3}).");
        }

        #endregion Test 2 — Male biology shifts DarkCore higher than female for identical Big Five

        #region Test 3 — Population distribution is right-skewed (mean < 0.5)

        [TestMethod]
        public void Population_DarkCore_IsRightSkewed_MeanBelowHalf()
        {
            // Generate a seeded population of 500 characters with varied Big Five traits.
            var rng = new Random(42);
            double[] scores = new double[500];

            for (var i = 0; i < 500; i++)
            {
                var bigFive = new BigFive(
                    Openness: rng.NextDouble(),
                    Conscientiousness: rng.NextDouble(),
                    Extraversion: rng.NextDouble(),
                    Agreeableness: rng.NextDouble(),
                    Neuroticism: rng.NextDouble());

                // Use a fresh per-character random for noise; sex alternates to avoid constant bias.
                var bio = i % 2 == 0 ? SexBiology.Female : SexBiology.Male;
                var profile = DarkCoreGenerator.Generate(bigFive, bio, new Random(rng.Next()));
                scores[i] = profile.DarkCore;
            }

            var mean = scores.Average();

            // Assert — right-skewed distribution: mean should be clearly below 0.5.
            // The skew exponent 1.6 applied after Agreeableness-centering at 0.5 produces mean ≈ 0.25–0.35.
            Assert.IsTrue(mean < 0.45,
                $"Population DarkCore mean ({mean:F3}) should be < 0.45, confirming right-skew (most are low-D).");
        }

        #endregion Test 3 — Population distribution is right-skewed (mean < 0.5)

        #region Test 4 — Deterministic: two null-random calls produce identical results

        [TestMethod]
        public void NullRandom_IsDeterministic()
        {
            var bigFive = new BigFive(Openness: 0.6, Conscientiousness: 0.4,
                                      Extraversion: 0.5, Agreeableness: 0.3, Neuroticism: 0.7);

            var p1 = DarkCoreGenerator.Generate(bigFive, SexBiology.Male, random: null);
            var p2 = DarkCoreGenerator.Generate(bigFive, SexBiology.Male, random: null);

            Assert.AreEqual(p1, p2, "Null-random generation must be deterministic (no noise).");
        }

        #endregion Test 4 — Deterministic: two null-random calls produce identical results

        #region Test 5 — All outputs in [0,1]

        [TestMethod]
        public void AllOutputs_InRange_ZeroToOne()
        {
            // Test extreme trait combinations to verify clamping.
            var extremes = new[]
            {
                new BigFive(0.0, 0.0, 0.0, 0.0, 0.0),
                new BigFive(1.0, 1.0, 1.0, 1.0, 1.0),
                new BigFive(0.0, 0.0, 0.0, 0.0, 1.0), // worst case: low A + high N
                new BigFive(1.0, 1.0, 1.0, 1.0, 0.0),
            };

            foreach (var bf in extremes)
            {
                foreach (var bio in new[] { SexBiology.Female, SexBiology.Male, SexBiology.Unknown })
                {
                    var p = DarkCoreGenerator.Generate(bf, bio, random: null);

                    Assert.IsTrue(p.DarkCore >= 0.0 && p.DarkCore <= 1.0,
                        $"DarkCore {p.DarkCore:F4} out of [0,1] for A={bf.Agreeableness}, bio={bio}.");
                    Assert.IsTrue(p.JustifyingBeliefs >= 0.0 && p.JustifyingBeliefs <= 1.0,
                        $"JustifyingBeliefs {p.JustifyingBeliefs:F4} out of [0,1] for A={bf.Agreeableness}, bio={bio}.");
                }
            }
        }

        #endregion Test 5 — All outputs in [0,1]

        #region Test 6 — IRandomSource overload produces consistent results with null-random for same traits

        [TestMethod]
        public void IRandomSourceOverload_LowAgreeableness_HigherDarkCore_ThanHighAgreeableness()
        {
            // IRandomSource overload should preserve the same A→DarkCore direction.
            // Use ZeroRandom (always returns 0) as a deterministic IRandomSource.
            var rng = new ZeroRandom();

            var lowA = DarkCoreGenerator.Generate(rng, new BigFive(0.5, 0.5, 0.5, 0.1, 0.5), SexBiology.Female);
            var highA = DarkCoreGenerator.Generate(rng, new BigFive(0.5, 0.5, 0.5, 0.9, 0.5), SexBiology.Female);

            Assert.IsTrue(lowA.DarkCore > highA.DarkCore,
                $"IRandomSource overload: low-A DarkCore ({lowA.DarkCore:F3}) must exceed high-A ({highA.DarkCore:F3}).");
        }

        #endregion Test 6 — IRandomSource overload produces consistent results with null-random for same traits
    }
}
