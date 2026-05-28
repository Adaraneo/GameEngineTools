// ValuesProfile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using System;

    /// <summary>
    /// Immutable motivational profile based on Schwartz's Basic Human Values Theory (1992).
    /// Generated once at character creation from BigFive; stable across the character's lifetime.
    /// </summary>
    /// <remarks>
    /// All values are in [0..1], where 0 = not important and 1 = extremely important.
    /// Values are ipsatized (mean-centred) after generation per the recommendation of
    /// Parks-Leduc, Feldman &amp; Bardi (2015, <i>Pers. Soc. Psych. Rev.</i> 19:3–29),
    /// which removes scale-use bias and makes cross-character comparisons valid.
    /// <para>
    /// The 10-value model is used (not the refined 19-value model from Schwartz et al. 2012)
    /// because the 10-value structure has direct, validated Big Five mappings and sufficient
    /// granularity for NPC behaviour differentiation at simulation scale.
    /// </para>
    /// <para>
    /// Circumplex structure (adjacent values are motivationally compatible;
    /// opposing values are in motivational tension):
    /// <code>
    ///   Openness-to-Change ←→ Conservation
    ///   SelfDirection — Stimulation — Hedonism — Achievement — Power
    ///                                    ↑
    ///   Universalism — Benevolence — Conformity — Tradition — Security
    ///   Self-Transcendence ←→ Self-Enhancement
    /// </code>
    /// </para>
    /// </remarks>
    public sealed record ValuesProfile(
        /// <summary>
        /// Benevolence — caring for close others, loyalty, helpfulness.
        /// Self-Transcendence pole. Primary predictor: Agreeableness (ρ=.61).
        /// Guilt is triggered when an action violates this value.
        /// </summary>
        double Benevolence,

        /// <summary>
        /// Universalism — understanding, tolerance, justice, nature protection.
        /// Self-Transcendence pole. Predictors: Agreeableness (ρ=.39), Openness (ρ=.33).
        /// Guilt is triggered when an action violates this value.
        /// </summary>
        double Universalism,

        /// <summary>
        /// Self-Direction — autonomy, creativity, curiosity, freedom.
        /// Openness-to-Change pole. Primary predictor: Openness (ρ=.52).
        /// </summary>
        double SelfDirection,

        /// <summary>
        /// Stimulation — excitement, novelty, challenge.
        /// Openness-to-Change pole. Predictors: Openness (ρ=.36), Extraversion (ρ=.36).
        /// </summary>
        double Stimulation,

        /// <summary>
        /// Hedonism — pleasure, sensory gratification.
        /// Boundary between Openness-to-Change and Self-Enhancement.
        /// Predictors: Extraversion (ρ=.20), low Conscientiousness (ρ=−.19).
        /// </summary>
        double Hedonism,

        /// <summary>
        /// Achievement — personal success, competence, ambition.
        /// Self-Enhancement pole. Predictors: Extraversion (ρ=.31), Conscientiousness (ρ=.17).
        /// Note: Extraversion is the dominant predictor, not Conscientiousness.
        /// </summary>
        double Achievement,

        /// <summary>
        /// Power — social status, dominance, control over resources.
        /// Self-Enhancement pole. Predictors: Extraversion (ρ=.31), low Agreeableness (ρ=−.42).
        /// </summary>
        double Power,

        /// <summary>
        /// Security — safety, stability, order, harmony.
        /// Conservation pole. Predictors: Conscientiousness (ρ=.37), low Openness (ρ=−.24).
        /// </summary>
        double Security,

        /// <summary>
        /// Conformity — restraint of impulses and actions that violate norms.
        /// Conservation pole. Predictors: Conscientiousness (ρ=.27), Agreeableness (ρ=.26),
        /// low Openness (ρ=−.27).
        /// </summary>
        double Conformity,

        /// <summary>
        /// Tradition — respect for customs, religious devotion, acceptance of fate.
        /// Conservation pole. Predictors: low Openness (ρ=−.31), Agreeableness (ρ=.22).
        /// Note: Conscientiousness is not the primary driver (ρ=.10, non-significant).
        /// </summary>
        double Tradition);

    /// <summary>
    /// Generates a <see cref="ValuesProfile"/> from Big Five personality traits using
    /// empirically validated regression coefficients from meta-analysis.
    /// </summary>
    /// <remarks>
    /// Coefficient source: Parks-Leduc, Feldman &amp; Bardi (2015, <i>PSPR</i> 19:3–29),
    /// meta-analysis of 60 studies, N≈55,000, sample-size weighted, corrected for unreliability.
    /// <para>
    /// Three critical corrections vs. the original research-plan proposal:
    /// <list type="number">
    ///   <item>Neuroticism removed entirely — ρ≈0 with all 10 values in meta-analytic data.</item>
    ///   <item>Achievement: Extraversion is the dominant predictor (ρ=.31 &gt; C ρ=.17).</item>
    ///   <item>Tradition: driven by low Openness (ρ=−.31) and Agreeableness, not Conscientiousness.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static class ValuesProfileGenerator
    {
        // ── Calibration noise ─────────────────────────────────────────────────
        // Meta-analytic ρ² explains only 4–37% of value variance.
        // The remainder is environment, socialization, and individual experience.
        // σ=0.10 Gaussian noise preserves meaningful personality→values signal
        // while preventing over-determinism (Parks-Leduc et al. 2015 recommend
        // treating residual variance as substantively important).
        private const double NoiseSigma = 0.10;

        /// <summary>
        /// Generates a <see cref="ValuesProfile"/> from a <see cref="BigFive"/> trait profile.
        /// </summary>
        /// <param name="bigFive">The character's Big Five traits, each in [0..1].</param>
        /// <param name="random">
        /// Random source for inter-individual residual noise.
        /// Pass <c>null</c> for a deterministic (mean-only) profile, useful for tests.
        /// </param>
        /// <returns>
        /// Ipsatized <see cref="ValuesProfile"/> with all values in [0..1].
        /// Ipsatization (mean-centering per Parks-Leduc et al. 2015) removes scale-use bias
        /// so that value importance is relative to the character's own mean, not absolute.
        /// </returns>
        public static ValuesProfile Generate(BigFive bigFive, Random? random = null)
        {
            var o = bigFive.Openness;
            var c = bigFive.Conscientiousness;
            var e = bigFive.Extraversion;
            var a = bigFive.Agreeableness;
            // Note: Neuroticism deliberately excluded — ρ≈0 with all 10 Schwartz values.

            // Raw scores from regression equations (Parks-Leduc et al. 2015).
            // Formula: 0.5 + Σ(ρ_trait × (trait − 0.5)) + noise
            // The 0.5 baseline centres all values at the population midpoint.
            double benevolence  = 0.5 + 0.61 * (a - 0.5);
            double universalism = 0.5 + 0.39 * (a - 0.5) + 0.33 * (o - 0.5);
            double selfDir      = 0.5 + 0.52 * (o - 0.5);
            double stimulation  = 0.5 + 0.36 * (o - 0.5) + 0.36 * (e - 0.5);
            double hedonism     = 0.5 + 0.20 * (e - 0.5) - 0.19 * (c - 0.5);
            double achievement  = 0.5 + 0.31 * (e - 0.5) + 0.17 * (c - 0.5);
            double power        = 0.5 + 0.31 * (e - 0.5) - 0.42 * (a - 0.5);
            double security     = 0.5 + 0.37 * (c - 0.5) - 0.24 * (o - 0.5);
            double conformity   = 0.5 + 0.27 * (c - 0.5) + 0.26 * (a - 0.5) - 0.27 * (o - 0.5);
            double tradition    = 0.5 - 0.31 * (o - 0.5) + 0.22 * (a - 0.5);

            // Add residual noise when a random source is provided.
            if (random is not null)
            {
                benevolence  += SampleNoise(random);
                universalism += SampleNoise(random);
                selfDir      += SampleNoise(random);
                stimulation  += SampleNoise(random);
                hedonism     += SampleNoise(random);
                achievement  += SampleNoise(random);
                power        += SampleNoise(random);
                security     += SampleNoise(random);
                conformity   += SampleNoise(random);
                tradition    += SampleNoise(random);
            }

            // Clamp before ipsatization to keep values in a valid range.
            double[] raw =
            {
                Math.Clamp(benevolence, 0, 1),
                Math.Clamp(universalism, 0, 1),
                Math.Clamp(selfDir, 0, 1),
                Math.Clamp(stimulation, 0, 1),
                Math.Clamp(hedonism, 0, 1),
                Math.Clamp(achievement, 0, 1),
                Math.Clamp(power, 0, 1),
                Math.Clamp(security, 0, 1),
                Math.Clamp(conformity, 0, 1),
                Math.Clamp(tradition, 0, 1),
            };

            // Ipsatize: subtract the character's own mean so that importance is relative.
            // This is the explicit recommendation of Parks-Leduc et al. (2015): "controlling
            // for personal scale-use tendencies in values is advisable."
            var mean = 0.0;
            foreach (var v in raw) mean += v;
            mean /= raw.Length;

            double[] ips = new double[raw.Length];
            for (var i = 0; i < raw.Length; i++)
                ips[i] = Math.Clamp(raw[i] - mean + 0.5, 0, 1);

            return new ValuesProfile(
                Benevolence:   ips[0],
                Universalism:  ips[1],
                SelfDirection: ips[2],
                Stimulation:   ips[3],
                Hedonism:      ips[4],
                Achievement:   ips[5],
                Power:         ips[6],
                Security:      ips[7],
                Conformity:    ips[8],
                Tradition:     ips[9]);
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
            var z  = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return z * NoiseSigma;
        }
    }
}
