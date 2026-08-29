// RegulatoryFocusProfile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using System;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// A character's position on Higgins' (1997) Promotion and Prevention regulatory-focus
    /// dimensions. The two factors are near-independent (Gorman et al. 2012; Lanaj, Chang &amp;
    /// Johnson 2012, corrected ρ≈.11) — modelled as two separate scores, not a bipolar scale.
    /// </summary>
    /// <remarks>
    /// Sources: Higgins (1997); Gorman et al. (2012, <i>Journal of Vocational Behavior</i>
    /// 80(1):160–172); Lanaj, Chang &amp; Johnson (2012, <i>Psychological Bulletin</i>
    /// 138(5):998–1034); Schmalbach et al. (2017, <i>BMC Psychology</i> 5:40) for population
    /// norms (Promotion ω≈.61 — weaker reliability, wider noise; Prevention ω≈.78).
    /// </remarks>
    /// <param name="Promotion">Eagerness/approach-to-gains orientation [0..1].</param>
    /// <param name="Prevention">Vigilance/avoidance-of-loss orientation [0..1].</param>
    public sealed record RegulatoryFocusProfile(double Promotion, double Prevention);

    /// <summary>
    /// Generates a <see cref="RegulatoryFocusProfile"/> from Big Five traits using Lanaj, Chang &amp;
    /// Johnson (2012) regression directions plus a wide residual, mirroring
    /// <see cref="DarkCoreGenerator"/>'s Box-Muller idiom.
    /// </summary>
    /// <remarks>
    /// Big Five explains only ~10–20% of regulatory-focus variance (ρ≈.2–.45), so the residual noise
    /// is deliberately large. Promotion carries 1.3× the residual of Prevention to reflect its weaker
    /// measurement reliability (ω≈.61 vs .78; Schmalbach et al. 2017).
    /// </remarks>
    public static class RegulatoryFocusGenerator
    {
        #region Regression coefficients (Lanaj, Chang & Johnson 2012)

        // Promotion: + Extraversion (ρ≈.36, verified), + Openness, + Conscientiousness,
        // + Agreeableness, − Neuroticism.
        // NOTE (2026-08-29): full-text Table 2–3 access is blocked by an APA/Figshare paywall
        // (checked PsycNet, ResearchGate, Academia.edu, and Figshare's own "open access" record,
        // which turned out to still be under file embargo despite the metadata flag). A secondary
        // source citing the same meta-analysis independently confirmed the DIRECTIONS below
        // (Promotion: +E/+O/+A/+C; Prevention: +N/+C; Promotion–Prevention near-orthogonal) — that
        // secondary source did not list Neuroticism among Promotion's antecedents at all, so
        // PromotionNeuroticismWeight is the one term without even secondary corroboration.
        // The magnitudes were separately corroborated by the user's own prior research of the
        // source (2026-08-29) — not a primary-source table check, but independent enough to lift
        // this from "provisional, direction-only" to "corroborated, pending a primary-source read."
        private const double PromotionExtraversionWeight = 0.36;      // Source: Lanaj et al. 2012 (verified)
        private const double PromotionOpennessWeight = 0.15;          // Source: Lanaj et al. 2012 (corroborated — see note above)
        private const double PromotionConscientiousnessWeight = 0.15; // Source: Lanaj et al. 2012 (corroborated — see note above)
        private const double PromotionAgreeablenessWeight = 0.10;     // Source: Lanaj et al. 2012 (corroborated — see note above)
        private const double PromotionNeuroticismWeight = -0.15;      // Source: Lanaj et al. 2012 (corroborated, but absent from the secondary source's antecedent list — verify first if revisiting)

        // Prevention: + Neuroticism/BIS, + Conscientiousness (Gorman et al. 2012 — both foci are
        // elevated by Conscientiousness, NOT differentially related to it).
        private const double PreventionNeuroticismWeight = 0.25;      // Source: Lanaj et al. 2012 (corroborated — see note above)
        private const double PreventionConscientiousnessWeight = 0.15; // Source: Gorman et al. 2012 (corroborated — see note above)

        #endregion

        // Wide residual — mirrors DarkCoreGenerator's NoiseSigma idiom but larger (weak Big Five link).
        private const double NoiseSigma = 0.18;

        // Promotion residual widening factor (ω≈.61 → noisier than Prevention's ω≈.78).
        private const double PromotionNoiseFactor = 1.3;

        /// <summary>
        /// Samples a <see cref="RegulatoryFocusProfile"/> from a <see cref="BigFive"/> profile using the
        /// project's seeded <see cref="IRandomSource"/>.
        /// </summary>
        /// <param name="rng">Deterministic per-character RNG; never <c>null</c>.</param>
        /// <param name="bigFive">The character's Big Five traits, each in [0..1].</param>
        /// <returns>A <see cref="RegulatoryFocusProfile"/> with both scores in [0..1].</returns>
        public static RegulatoryFocusProfile Generate(IRandomSource rng, BigFive bigFive)
        {
            ArgumentNullException.ThrowIfNull(rng);

            var promotionRaw = 0.5
                + PromotionExtraversionWeight * (bigFive.Extraversion - 0.5)
                + PromotionOpennessWeight * (bigFive.Openness - 0.5)
                + PromotionConscientiousnessWeight * (bigFive.Conscientiousness - 0.5)
                + PromotionAgreeablenessWeight * (bigFive.Agreeableness - 0.5)
                + PromotionNeuroticismWeight * (bigFive.Neuroticism - 0.5)
                + NextStandardNormal(rng) * NoiseSigma * PromotionNoiseFactor;

            var preventionRaw = 0.5
                + PreventionNeuroticismWeight * (bigFive.Neuroticism - 0.5)
                + PreventionConscientiousnessWeight * (bigFive.Conscientiousness - 0.5)
                + NextStandardNormal(rng) * NoiseSigma;

            return new RegulatoryFocusProfile(
                Promotion: Math.Clamp(promotionRaw, 0.0, 1.0),
                Prevention: Math.Clamp(preventionRaw, 0.0, 1.0));
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
