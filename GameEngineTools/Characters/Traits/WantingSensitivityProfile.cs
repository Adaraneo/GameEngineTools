// WantingSensitivityProfile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using System;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Per-character trait sensitivity to cue-triggered incentive salience ("wanting") and
    /// hedonic-impact capacity ("liking"). Generated once at character creation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WantingSensitivity</b> bridges to Big Five via the Sensitivity-to-Reward/BAS
    /// literature (Mitchell et al. 2007): Extraversion (+), Conscientiousness (−),
    /// Agreeableness (−). Weaker/noisier bridge than RegulatoryFocus, per Hughson et al.
    /// (2019, <i>Scientific Reports</i> 9:2351, N=1,598) showing incentive-salience
    /// attribution is statistically independent of sensation-/novelty-seeking in a large
    /// sample — trait wanting is NOT simply reducible to a single Big Five factor.
    /// </para>
    /// <para>
    /// <b>LikingCapacity</b> is generated near-independently of Big Five (Kaiser et al. 2020 —
    /// no strong trait-personality anchor for hedonic capacity).
    /// </para>
    /// Sources: Mitchell et al. (2007, <i>European Journal of Personality</i>); Hughson et al.
    /// (2019); Kaiser et al. (2020); Berridge &amp; Robinson (2016, <i>American Psychologist</i>).
    /// </remarks>
    /// <param name="WantingSensitivity">Trait-level gain on cue-triggered salience [0..1].</param>
    /// <param name="LikingCapacity">Trait-level hedonic-impact capacity [0..1].</param>
    public sealed record WantingSensitivityProfile(double WantingSensitivity, double LikingCapacity);

    /// <summary>
    /// Generates a <see cref="WantingSensitivityProfile"/> from Big Five traits, mirroring
    /// <see cref="DarkCoreGenerator"/>'s Box-Muller idiom but with wider residual noise (weak,
    /// dissociable trait-personality link; Hughson et al. 2019).
    /// </summary>
    public static class WantingSensitivityGenerator
    {
        #region Regression coefficients (Mitchell et al. 2007 — Sensitivity-to-Reward/BAS)

        private const double WantingExtraversionWeight = 0.30;       // Source: Mitchell et al. 2007
        private const double WantingConscientiousnessWeight = -0.15; // Source: Mitchell et al. 2007
        private const double WantingAgreeablenessWeight = -0.10;     // Source: Mitchell et al. 2007

        #endregion

        // Wider noise than DarkCore/RegulatoryFocus — Hughson et al. (2019) independence finding.
        private const double WantingNoiseSigma = 0.22;

        // Liking: near-independent of Big Five — noise dominates, no Big Five term.
        private const double LikingNoiseSigma = 0.20;

        /// <summary>
        /// Samples a <see cref="WantingSensitivityProfile"/> from a <see cref="BigFive"/> profile using
        /// the project's seeded <see cref="IRandomSource"/>.
        /// </summary>
        /// <param name="rng">Deterministic per-character RNG; never <c>null</c>.</param>
        /// <param name="bigFive">The character's Big Five traits, each in [0..1].</param>
        /// <returns>A <see cref="WantingSensitivityProfile"/> with both scores in [0..1].</returns>
        public static WantingSensitivityProfile Generate(IRandomSource rng, BigFive bigFive)
        {
            ArgumentNullException.ThrowIfNull(rng);

            var wantingRaw = 0.5
                + WantingExtraversionWeight * (bigFive.Extraversion - 0.5)
                + WantingConscientiousnessWeight * (bigFive.Conscientiousness - 0.5)
                + WantingAgreeablenessWeight * (bigFive.Agreeableness - 0.5)
                + NextStandardNormal(rng) * WantingNoiseSigma;

            // Liking: no Big Five term — purely noise-driven trait, per research gate.
            var likingRaw = 0.5 + NextStandardNormal(rng) * LikingNoiseSigma;

            return new WantingSensitivityProfile(
                WantingSensitivity: Math.Clamp(wantingRaw, 0.0, 1.0),
                LikingCapacity: Math.Clamp(likingRaw, 0.0, 1.0));
        }

        /// <summary>
        /// Standard normal via Box-Muller using the project's <see cref="IRandomSource"/> — identical
        /// idiom to <see cref="DarkCoreGenerator"/>.
        /// </summary>
        private static double NextStandardNormal(IRandomSource rng)
        {
            var u1 = 1.0 - rng.NextUnit();
            var u2 = 1.0 - rng.NextUnit();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
