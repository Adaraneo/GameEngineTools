// DualControlMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using System;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Generates a per-character <see cref="SexualResponsiveness"/> (DCM) profile from Big Five
    /// priors plus Gaussian noise. Mirrors the "trait as weak prior, not constant" pattern used
    /// elsewhere (e.g. <see cref="Engines.ToM.ToMMath.GenerateCeiling"/>, ValuesProfile, InterestProfile).
    /// </summary>
    /// <remarks>
    /// Trait↔DCM correlations (Bancroft &amp; Janssen 2000; Janssen &amp; Bancroft 2007) are only
    /// MODEST, so coupling coefficients are deliberately small — most variance comes from the noise
    /// term. Couplings:
    /// <list type="bullet">
    ///   <item>SES (excitation) ← + Extraversion, + Openness (sensation-seeking component).</item>
    ///   <item>SIS1 (performance/failure inhibition) ← + Neuroticism (anxiety-driven inhibition).</item>
    ///   <item>SIS2 (threat/context inhibition) ← + Neuroticism, + Conscientiousness (caution),
    ///         − Sociosexuality.Desire (openness to uncommitted sex lowers contextual inhibition).</item>
    /// </list>
    /// Life-stage gating is NOT handled here — minors are routed to
    /// <see cref="SexualResponsiveness.Default"/> upstream via <see cref="Generation.PersonalityHints.ForStadium"/>,
    /// so this method is only invoked for adults.
    /// </remarks>
    public static class DualControlMath
    {
        // Population mean for every facet (no trait, no noise → 0.5).
        private const double FacetMean = 0.5;

        // Noise SD — dominant variance source (weak coupling on purpose).
        private const double NoiseSd = 0.15;

        // Weak coupling coefficients (modest empirical correlations).
        private const double SesFromExtraversion = 0.12;
        private const double SesFromOpenness = 0.08;
        private const double Sis1FromNeuroticism = 0.18;
        private const double Sis2FromNeuroticism = 0.12;
        private const double Sis2FromConscientiousness = 0.10;
        private const double Sis2FromSociosexualDesire = 0.10; // subtracted

        /// <summary>
        /// Samples a DCM profile for an adult character.
        /// </summary>
        /// <param name="rng">Deterministic per-character RNG.</param>
        /// <param name="openness">Big Five Openness [0–1].</param>
        /// <param name="conscientiousness">Big Five Conscientiousness [0–1].</param>
        /// <param name="extraversion">Big Five Extraversion [0–1].</param>
        /// <param name="neuroticism">Big Five Neuroticism [0–1].</param>
        /// <param name="sociosexuality">Generated sociosexuality (uses the Desire facet for SIS2).</param>
        /// <returns>A clamped <see cref="SexualResponsiveness"/> with all facets in [0–1].</returns>
        public static SexualResponsiveness Generate(
            IRandomSource rng,
            double openness,
            double conscientiousness,
            double extraversion,
            double neuroticism,
            Sociosexuality sociosexuality)
        {
            var ses = FacetMean
                + SesFromExtraversion * (extraversion - 0.5)
                + SesFromOpenness * (openness - 0.5)
                + NextStandardNormal(rng) * NoiseSd;

            var sis1 = FacetMean
                + Sis1FromNeuroticism * (neuroticism - 0.5)
                + NextStandardNormal(rng) * NoiseSd;

            var sis2 = FacetMean
                + Sis2FromNeuroticism * (neuroticism - 0.5)
                + Sis2FromConscientiousness * (conscientiousness - 0.5)
                - Sis2FromSociosexualDesire * (sociosexuality.Desire - 0.5)
                + NextStandardNormal(rng) * NoiseSd;

            return new SexualResponsiveness(
                Math.Clamp(ses, 0.0, 1.0),
                Math.Clamp(sis1, 0.0, 1.0),
                Math.Clamp(sis2, 0.0, 1.0));
        }

        /// <summary>
        /// Standard normal via Box-Muller, using the project RNG (deterministic, testable).
        /// Identical idiom to <see cref="Engines.ToM.ToMMath.GenerateCeiling"/>.
        /// </summary>
        private static double NextStandardNormal(IRandomSource rng)
        {
            var u1 = 1.0 - rng.NextUnit();
            var u2 = 1.0 - rng.NextUnit();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
