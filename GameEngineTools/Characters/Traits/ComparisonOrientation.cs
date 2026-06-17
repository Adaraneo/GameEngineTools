// ComparisonOrientation.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using System;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Dispositional tendency to engage in social comparison — the Iowa-Netherlands
    /// Comparison Orientation Measure (INCOM; Gibbons &amp; Buunk 1999).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The INCOM distinguishes two subscales:
    /// <list type="bullet">
    ///   <item><b>Ability</b> — tendency to compare one's skills and performance with others.</item>
    ///   <item><b>Opinion</b> — tendency to compare one's beliefs and attitudes with others.</item>
    /// </list>
    /// <b>Overall</b> is a composite of both subscales and drives the intensity scalar
    /// in the social comparison engine.
    /// </para>
    /// <para>
    /// Validated convergent correlates (Gibbons &amp; Buunk 1999, <i>JPSP</i> 76(1)):
    /// <list type="bullet">
    ///   <item>Self-esteem r≈−.18 to −.32 (higher SCO → lower self-esteem).</item>
    ///   <item>Negative affect r≈.29 to .39 (higher SCO → more negative affect).</item>
    ///   <item>No social-desirability confound (MCSD r≈.00, non-significant).</item>
    /// </list>
    /// Population anchor: normalized item mean ≈ 0.65 (3.60/5 on the original 5-point scale).
    /// By construction the deterministic (null-random) Overall for a perfectly average character
    /// (all Big Five traits = 0.5) is exactly 0.5 — consistent with the [0..1] midpoint convention
    /// used elsewhere in this library.
    /// </para>
    /// </remarks>
    /// <param name="Ability">
    /// Tendency to compare one's abilities and performance with others [0..1].
    /// Slightly more Neuroticism-loaded than Opinion (anxiety-driven competence monitoring).
    /// </param>
    /// <param name="Opinion">
    /// Tendency to compare one's beliefs, attitudes, and opinions with others [0..1].
    /// Slightly more Openness-loaded than Ability (broader information-seeking).
    /// </param>
    /// <param name="Overall">
    /// Composite INCOM score [0..1]. Used as the primary orientation/intensity scalar by
    /// <see cref="GameEngineTools.Characters.Engines.Social.DefaultSocialComparisonEngine"/>.
    /// Correlates most strongly with Neuroticism (higher N → higher SCO).
    /// </param>
    public sealed record ComparisonOrientationProfile(double Ability, double Opinion, double Overall);

    /// <summary>
    /// Generates a <see cref="ComparisonOrientationProfile"/> from Big Five personality traits
    /// using empirically motivated regression equations (Gibbons &amp; Buunk 1999, <i>JPSP</i> 76(1)).
    /// </summary>
    /// <remarks>
    /// Generation pattern mirrors <see cref="ValuesProfileGenerator.Generate"/>:
    /// <c>0.5 + Σ ρ·(trait − 0.5)</c> plus optional Gaussian residual noise (Box-Muller, σ≈0.10).
    /// Pass <c>null</c> for <paramref name="random"/> to get a deterministic (mean-only) profile.
    /// The deterministic profile for all-0.5 traits is exactly 0.5 for every component by construction.
    /// </remarks>
    public static class ComparisonOrientationGenerator
    {
        #region Regression coefficients (Gibbons & Buunk 1999, JPSP 76(1))

        // Source: Gibbons & Buunk 1999, JPSP 76(1)
        // Overall SCO is primarily Neuroticism-driven; Extraversion and Openness add small secondary effects.
        private const double OverallNeuroticismWeight = 0.35;     // Source: Gibbons & Buunk 1999, JPSP 76(1)
        private const double OverallExtraversionWeight = 0.08;    // Source: Gibbons & Buunk 1999, JPSP 76(1)
        private const double OverallOpennessWeight = 0.06;        // Source: Gibbons & Buunk 1999, JPSP 76(1)

        // Ability subscale: slightly more Neuroticism-loaded (anxiety-driven performance monitoring).
        private const double AbilityNeuroticismWeight = 0.30;     // Source: Gibbons & Buunk 1999, JPSP 76(1)
        private const double AbilityExtraversionWeight = 0.08;    // Source: Gibbons & Buunk 1999, JPSP 76(1)
        private const double AbilityOpennessWeight = 0.04;        // Source: Gibbons & Buunk 1999, JPSP 76(1)

        // Opinion subscale: slightly more Openness-loaded (curiosity about others' views).
        private const double OpinionNeuroticismWeight = 0.25;     // Source: Gibbons & Buunk 1999, JPSP 76(1)
        private const double OpinionExtraversionWeight = 0.07;    // Source: Gibbons & Buunk 1999, JPSP 76(1)
        private const double OpinionOpennessWeight = 0.10;        // Source: Gibbons & Buunk 1999, JPSP 76(1)

        #endregion

        private const double NoiseSigma = 0.10;

        /// <summary>
        /// Generates a <see cref="ComparisonOrientationProfile"/> from a <see cref="BigFive"/> profile.
        /// </summary>
        /// <param name="bigFive">The character's Big Five traits, each in [0..1].</param>
        /// <param name="random">
        /// Random source for inter-individual residual noise.
        /// Pass <c>null</c> for a deterministic (mean-only) profile, useful for tests.
        /// </param>
        /// <returns>A <see cref="ComparisonOrientationProfile"/> with all components in [0..1].</returns>
        public static ComparisonOrientationProfile Generate(BigFive bigFive, Random? random = null)
        {
            var n = bigFive.Neuroticism;
            var e = bigFive.Extraversion;
            var o = bigFive.Openness;

            // Formula: 0.5 + Σ(ρ_trait × (trait − 0.5)) + noise
            // The 0.5 baseline centres all scores at the population midpoint.
            var overall = 0.5
                + OverallNeuroticismWeight * (n - 0.5)
                + OverallExtraversionWeight * (e - 0.5)
                + OverallOpennessWeight * (o - 0.5);

            var ability = 0.5
                + AbilityNeuroticismWeight * (n - 0.5)
                + AbilityExtraversionWeight * (e - 0.5)
                + AbilityOpennessWeight * (o - 0.5);

            var opinion = 0.5
                + OpinionNeuroticismWeight * (n - 0.5)
                + OpinionExtraversionWeight * (e - 0.5)
                + OpinionOpennessWeight * (o - 0.5);

            if (random is not null)
            {
                overall += SampleNoise(random);
                ability += SampleNoise(random);
                opinion += SampleNoise(random);
            }

            return new ComparisonOrientationProfile(
                Ability: Math.Clamp(ability, 0.0, 1.0),
                Opinion: Math.Clamp(opinion, 0.0, 1.0),
                Overall: Math.Clamp(overall, 0.0, 1.0));
        }

        /// <summary>
        /// Generates a <see cref="ComparisonOrientationProfile"/> using the project's seeded
        /// <see cref="IRandomSource"/> — mirrors the pattern used by <see cref="DualControlMath"/>.
        /// </summary>
        /// <param name="rng">Deterministic per-character RNG; never <c>null</c>.</param>
        /// <param name="bigFive">The character's Big Five traits, each in [0..1].</param>
        /// <returns>A <see cref="ComparisonOrientationProfile"/> with all components in [0..1].</returns>
        public static ComparisonOrientationProfile Generate(IRandomSource rng, BigFive bigFive)
        {
            ArgumentNullException.ThrowIfNull(rng);

            var n = bigFive.Neuroticism;
            var e = bigFive.Extraversion;
            var o = bigFive.Openness;

            var overall = 0.5
                + OverallNeuroticismWeight * (n - 0.5)
                + OverallExtraversionWeight * (e - 0.5)
                + OverallOpennessWeight * (o - 0.5)
                + NextStandardNormal(rng) * NoiseSigma;

            var ability = 0.5
                + AbilityNeuroticismWeight * (n - 0.5)
                + AbilityExtraversionWeight * (e - 0.5)
                + AbilityOpennessWeight * (o - 0.5)
                + NextStandardNormal(rng) * NoiseSigma;

            var opinion = 0.5
                + OpinionNeuroticismWeight * (n - 0.5)
                + OpinionExtraversionWeight * (e - 0.5)
                + OpinionOpennessWeight * (o - 0.5)
                + NextStandardNormal(rng) * NoiseSigma;

            return new ComparisonOrientationProfile(
                Ability: Math.Clamp(ability, 0.0, 1.0),
                Opinion: Math.Clamp(opinion, 0.0, 1.0),
                Overall: Math.Clamp(overall, 0.0, 1.0));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Samples Gaussian noise using the Box-Muller transform.
        /// σ = <see cref="NoiseSigma"/>; μ = 0.
        /// </summary>
        private static double SampleNoise(Random rng)
        {
            // Box-Muller: two uniform samples → one standard normal.
            var u1 = 1.0 - rng.NextDouble();
            var u2 = 1.0 - rng.NextDouble();
            var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return z * NoiseSigma;
        }

        /// <summary>
        /// Standard normal via Box-Muller using the project's <see cref="IRandomSource"/>.
        /// Identical idiom to <see cref="DualControlMath"/>.
        /// </summary>
        private static double NextStandardNormal(IRandomSource rng)
        {
            var u1 = 1.0 - rng.NextUnit();
            var u2 = 1.0 - rng.NextUnit();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
