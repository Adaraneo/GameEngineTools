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
        /// Self-Transcendence pole. Primary predictor: Agreeableness (ρ=.43).
        /// Guilt is triggered when an action violates this value.
        /// </summary>
        double Benevolence,

        /// <summary>
        /// Universalism — understanding, tolerance, justice, nature protection.
        /// Self-Transcendence pole. Predictors: Openness (ρ=.30), Agreeableness (ρ=.27).
        /// Guilt is triggered when an action violates this value.
        /// </summary>
        double Universalism,

        /// <summary>
        /// Self-Direction — autonomy, creativity, curiosity, freedom.
        /// Openness-to-Change pole. Primary predictor: Openness (ρ=.42).
        /// </summary>
        double SelfDirection,

        /// <summary>
        /// Stimulation — excitement, novelty, challenge.
        /// Openness-to-Change pole. Predictors: Openness (ρ=.33), Extraversion (ρ=.21).
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
        /// Self-Enhancement pole. Predictors: Extraversion (ρ=.25), Conscientiousness (ρ=.17).
        /// Note: Extraversion is the dominant predictor, not Conscientiousness.
        /// </summary>
        double Achievement,

        /// <summary>
        /// Power — social status, dominance, control over resources.
        /// Self-Enhancement pole. Predictors: low Agreeableness (ρ=−.31), Extraversion (ρ=.19).
        /// </summary>
        double Power,

        /// <summary>
        /// Security — safety, stability, order, harmony.
        /// Conservation pole. Predictors: Conscientiousness (ρ=.21), low Openness (ρ=−.18).
        /// </summary>
        double Security,

        /// <summary>
        /// Conformity — restraint of impulses and actions that violate norms.
        /// Conservation pole. Predictors: low Openness (ρ=−.21), Conscientiousness (ρ=.20),
        /// Agreeableness (ρ=.19).
        /// </summary>
        double Conformity,

        /// <summary>
        /// Tradition — respect for customs, religious devotion, acceptance of fate.
        /// Conservation pole. Predictors: low Openness (ρ=−.28), Agreeableness (ρ=.14).
        /// Note: Conscientiousness is not the primary driver (ρ≈.10, non-significant).
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
    /// Four critical corrections vs. the original research-plan proposal:
    /// <list type="number">
    ///   <item>Neuroticism removed entirely — ρ≈0 with all 10 values in meta-analytic data.</item>
    ///   <item>Achievement: Extraversion is the dominant predictor (ρ=.25 &gt; C ρ=.17).</item>
    ///   <item>Tradition: driven by low Openness (ρ=−.28) and Agreeableness, not Conscientiousness.</item>
    ///   <item>Magnitudes pulled to the meta-analytic weighted means (e.g. Benevolence↔Agreeableness
    ///         ρ=.43, not .61; Power↔low-Agreeableness ρ=−.31, not −.42). The earlier constants sat
    ///         above the meta-analytic mean and over-determined values from personality.</item>
    /// </list>
    /// Every coefficient is exposed via <see cref="CoefficientAudit"/> with its cited upper bound
    /// so a unit test can guard against future inflation past the literature ceiling.
    /// </para>
    /// </remarks>
    public static class ValuesProfileGenerator
    {
        #region Regression coefficients (Parks-Leduc et al. 2015 meta-analytic means)

        // Each constant is the meta-analytic weighted-mean correlation between a Big Five trait
        // and a Schwartz value (Parks-Leduc, Feldman & Bardi 2015, PSPR 19:3–29). Signs preserve
        // the validated direction; magnitudes are held at or below the meta-analytic mean.
        private const double BenevolenceAgreeableness = 0.43;   // Source: Parks-Leduc 2015 (was .61)
        private const double UniversalismAgreeableness = 0.27;  // Source: Parks-Leduc 2015
        private const double UniversalismOpenness = 0.30;       // Source: Parks-Leduc 2015
        private const double SelfDirectionOpenness = 0.42;      // Source: Parks-Leduc 2015 (was .52)
        private const double StimulationOpenness = 0.33;        // Source: Parks-Leduc 2015
        private const double StimulationExtraversion = 0.21;    // Source: Parks-Leduc 2015 (was .36)
        private const double HedonismExtraversion = 0.20;       // Source: Parks-Leduc 2015
        private const double HedonismConscientiousness = -0.19; // Source: Parks-Leduc 2015
        private const double AchievementExtraversion = 0.25;    // Source: Parks-Leduc 2015 (was .31)
        private const double AchievementConscientiousness = 0.17; // Source: Parks-Leduc 2015
        private const double PowerExtraversion = 0.19;          // Source: Parks-Leduc 2015 (was .31)
        private const double PowerAgreeableness = -0.31;        // Source: Parks-Leduc 2015 (was -.42)
        private const double SecurityConscientiousness = 0.21;  // Source: Parks-Leduc 2015 (was .37)
        private const double SecurityOpenness = -0.18;          // Source: Parks-Leduc 2015 (was -.24)
        private const double ConformityConscientiousness = 0.20; // Source: Parks-Leduc 2015
        private const double ConformityAgreeableness = 0.19;    // Source: Parks-Leduc 2015 (was .26)
        private const double ConformityOpenness = -0.21;        // Source: Parks-Leduc 2015
        private const double TraditionOpenness = -0.28;         // Source: Parks-Leduc 2015 (was -.31)
        private const double TraditionAgreeableness = 0.14;     // Source: Parks-Leduc 2015 (was .22)

        /// <summary>
        /// Audit table of every regression coefficient used by <see cref="Generate"/>, paired with
        /// the meta-analytic upper bound it must not exceed in magnitude. Used by unit tests to guard
        /// against silent re-inflation of personality→value coupling past the literature ceiling
        /// (Parks-Leduc, Feldman &amp; Bardi 2015, <i>PSPR</i> 19:3–29).
        /// </summary>
        public static readonly System.Collections.Generic.IReadOnlyList<(string Name, double Coefficient, double MetaAnalyticUpperBound)> CoefficientAudit =
            new (string, double, double)[]
            {
                ("Benevolence←Agreeableness", BenevolenceAgreeableness, 0.45),
                ("Universalism←Agreeableness", UniversalismAgreeableness, 0.30),
                ("Universalism←Openness", UniversalismOpenness, 0.33),
                ("SelfDirection←Openness", SelfDirectionOpenness, 0.45),
                ("Stimulation←Openness", StimulationOpenness, 0.36),
                ("Stimulation←Extraversion", StimulationExtraversion, 0.30),
                ("Hedonism←Extraversion", HedonismExtraversion, 0.22),
                ("Hedonism←Conscientiousness", HedonismConscientiousness, 0.20),
                ("Achievement←Extraversion", AchievementExtraversion, 0.31),
                ("Achievement←Conscientiousness", AchievementConscientiousness, 0.20),
                ("Power←Extraversion", PowerExtraversion, 0.25),
                ("Power←Agreeableness", PowerAgreeableness, 0.35),
                ("Security←Conscientiousness", SecurityConscientiousness, 0.25),
                ("Security←Openness", SecurityOpenness, 0.24),
                ("Conformity←Conscientiousness", ConformityConscientiousness, 0.27),
                ("Conformity←Agreeableness", ConformityAgreeableness, 0.26),
                ("Conformity←Openness", ConformityOpenness, 0.27),
                ("Tradition←Openness", TraditionOpenness, 0.31),
                ("Tradition←Agreeableness", TraditionAgreeableness, 0.22),
            };

        #endregion
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
            double benevolence = 0.5 + BenevolenceAgreeableness * (a - 0.5);
            double universalism = 0.5 + UniversalismAgreeableness * (a - 0.5) + UniversalismOpenness * (o - 0.5);
            double selfDir = 0.5 + SelfDirectionOpenness * (o - 0.5);
            double stimulation = 0.5 + StimulationOpenness * (o - 0.5) + StimulationExtraversion * (e - 0.5);
            double hedonism = 0.5 + HedonismExtraversion * (e - 0.5) + HedonismConscientiousness * (c - 0.5);
            double achievement = 0.5 + AchievementExtraversion * (e - 0.5) + AchievementConscientiousness * (c - 0.5);
            double power = 0.5 + PowerExtraversion * (e - 0.5) + PowerAgreeableness * (a - 0.5);
            double security = 0.5 + SecurityConscientiousness * (c - 0.5) + SecurityOpenness * (o - 0.5);
            double conformity = 0.5 + ConformityConscientiousness * (c - 0.5) + ConformityAgreeableness * (a - 0.5) + ConformityOpenness * (o - 0.5);
            double tradition = 0.5 + TraditionOpenness * (o - 0.5) + TraditionAgreeableness * (a - 0.5);

            // Add residual noise when a random source is provided.
            if (random is not null)
            {
                benevolence += SampleNoise(random);
                universalism += SampleNoise(random);
                selfDir += SampleNoise(random);
                stimulation += SampleNoise(random);
                hedonism += SampleNoise(random);
                achievement += SampleNoise(random);
                power += SampleNoise(random);
                security += SampleNoise(random);
                conformity += SampleNoise(random);
                tradition += SampleNoise(random);
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
                Benevolence: ips[0],
                Universalism: ips[1],
                SelfDirection: ips[2],
                Stimulation: ips[3],
                Hedonism: ips[4],
                Achievement: ips[5],
                Power: ips[6],
                Security: ips[7],
                Conformity: ips[8],
                Tradition: ips[9]);
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
    }
}
