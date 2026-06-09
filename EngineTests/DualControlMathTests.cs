// DualControlMathTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Unit tests for <see cref="DualControlMath"/> — per-NPC DCM (SES/SIS1/SIS2) generation.
    /// </summary>
    /// <remarks>
    /// The generator is a weak trait prior + dominant Gaussian noise, so all assertions are
    /// distributional PROPERTIES (variance &gt; 0, range bounds, mean shift under trait extremes,
    /// determinism for a fixed seed) rather than exact values.
    /// </remarks>
    [TestClass]
    public class DualControlMathTests
    {
        // ── Shared fixture ───────────────────────────────────────────────────────

        /// <summary>Neutral sociosexuality used wherever the facet coupling is not under test.</summary>
        private static readonly Sociosexuality NeutralSocio = new(0.5, 0.5, 0.5);

        // ── Region: variation ────────────────────────────────────────────────────

        #region Variation — no longer a constant

        /// <summary>
        /// Across many seeds every facet must vary (SD &gt; 0) — i.e. it is no longer the
        /// constant <see cref="SexualResponsiveness.Default"/> (0.5/0.5/0.5).
        /// </summary>
        [TestMethod]
        public void Generate_ProducesVariation()
        {
            // Arrange
            const int n = 200;

            // Act
            var samples = Enumerable.Range(0, n)
                .Select(seed => DualControlMath.Generate(
                    new DeterministicRandom(seed), 0.5, 0.5, 0.5, 0.5, NeutralSocio))
                .ToList();

            // Assert
            Assert.IsTrue(StdDev(samples.Select(s => s.SES)) > 0.0, "SES must vary across seeds.");
            Assert.IsTrue(StdDev(samples.Select(s => s.SIS1)) > 0.0, "SIS1 must vary across seeds.");
            Assert.IsTrue(StdDev(samples.Select(s => s.SIS2)) > 0.0, "SIS2 must vary across seeds.");
        }

        #endregion Variation — no longer a constant

        // ── Region: range ────────────────────────────────────────────────────────

        #region Unit-range clamping

        /// <summary>
        /// Every generated facet must stay within [0,1] even at trait extremes.
        /// </summary>
        [TestMethod]
        public void Generate_FacetsWithinUnitRange()
        {
            // Arrange — push couplings hard with extreme traits in both directions.
            var extremes = new[] { 0.0, 1.0 };

            // Act + Assert
            foreach (var o in extremes)
            foreach (var c in extremes)
            foreach (var e in extremes)
            foreach (var nn in extremes)
            {
                for (var seed = 0; seed < 50; seed++)
                {
                    var dcm = DualControlMath.Generate(
                        new DeterministicRandom(seed), o, c, e, nn, NeutralSocio);

                    Assert.IsTrue(dcm.SES is >= 0.0 and <= 1.0, $"SES out of range: {dcm.SES}");
                    Assert.IsTrue(dcm.SIS1 is >= 0.0 and <= 1.0, $"SIS1 out of range: {dcm.SIS1}");
                    Assert.IsTrue(dcm.SIS2 is >= 0.0 and <= 1.0, $"SIS2 out of range: {dcm.SIS2}");
                }
            }
        }

        #endregion Unit-range clamping

        // ── Region: trait coupling ─────────────────────────────────────────────────

        #region Neuroticism → SIS1 coupling (distributional)

        /// <summary>
        /// High Neuroticism must, on average, raise SIS1 (anxiety-driven performance inhibition)
        /// compared with low Neuroticism. Distributional claim over many samples — not exact.
        /// </summary>
        [TestMethod]
        public void Generate_HighNeuroticism_RaisesSIS1OnAverage()
        {
            // Arrange
            const int n = 400;

            // Act
            var highN = Enumerable.Range(0, n)
                .Select(seed => DualControlMath.Generate(
                    new DeterministicRandom(seed), 0.5, 0.5, 0.5, 0.9, NeutralSocio).SIS1)
                .Average();

            var lowN = Enumerable.Range(0, n)
                .Select(seed => DualControlMath.Generate(
                    new DeterministicRandom(seed), 0.5, 0.5, 0.5, 0.1, NeutralSocio).SIS1)
                .Average();

            // Assert
            Assert.IsTrue(highN > lowN,
                $"Mean SIS1 should be higher for high Neuroticism ({highN:F4}) than low ({lowN:F4}).");
        }

        #endregion Neuroticism → SIS1 coupling (distributional)

        // ── Region: determinism ────────────────────────────────────────────────────

        #region Determinism

        /// <summary>
        /// The same seed and inputs must yield an identical profile.
        /// </summary>
        [TestMethod]
        public void Generate_IsDeterministicForSameSeed()
        {
            // Arrange + Act
            var a = DualControlMath.Generate(new DeterministicRandom(123), 0.6, 0.4, 0.7, 0.3, NeutralSocio);
            var b = DualControlMath.Generate(new DeterministicRandom(123), 0.6, 0.4, 0.7, 0.3, NeutralSocio);

            // Assert
            Assert.AreEqual(a.SES, b.SES, "SES must be deterministic for a fixed seed.");
            Assert.AreEqual(a.SIS1, b.SIS1, "SIS1 must be deterministic for a fixed seed.");
            Assert.AreEqual(a.SIS2, b.SIS2, "SIS2 must be deterministic for a fixed seed.");
        }

        #endregion Determinism

        // ── Private helpers ─────────────────────────────────────────────────────────

        #region Private helpers

        /// <summary>Population standard deviation of a sequence.</summary>
        private static double StdDev(IEnumerable<double> values)
        {
            var list = values.ToList();
            var mean = list.Average();
            return Math.Sqrt(list.Select(v => (v - mean) * (v - mean)).Average());
        }

        /// <summary>
        /// Thin wrapper around <see cref="System.Random"/> for deterministic tests.
        /// </summary>
        private sealed class DeterministicRandom : IRandomSource
        {
            private readonly Random _inner;

            /// <summary>Initializes a new deterministic random with the given seed.</summary>
            /// <param name="seed">RNG seed — same seed always produces the same sequence.</param>
            public DeterministicRandom(int seed) => _inner = new Random(seed);

            /// <inheritdoc/>
            public int Next(int minInclusive, int maxExclusive) => _inner.Next(minInclusive, maxExclusive);

            /// <inheritdoc/>
            public double NextUnit() => _inner.NextDouble();

            /// <inheritdoc/>
            public bool Chance(double p) => _inner.NextDouble() < p;
        }

        #endregion Private helpers
    }
}
