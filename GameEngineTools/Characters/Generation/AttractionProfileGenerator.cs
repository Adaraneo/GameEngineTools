// AttractionProfileGenerator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Generates a randomised <see cref="AttractionProfile"/> for a character.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called once during character creation — the result is stored in
    /// <see cref="GameEngineTools.Characters.Hosting.HumanBlueprint"/> and persisted alongside
    /// <see cref="PhysicalAppearance"/>.
    /// </para>
    /// <para>
    /// <b>Design notes:</b>
    /// <list type="bullet">
    ///   <item>Height preference is centred on a sex-adjusted mean with normal jitter.</item>
    ///   <item>Frame preference is drawn from a uniform distribution over <see cref="BodyFramePreference"/> values.</item>
    ///   <item>WHR preference follows population norms with individual noise.</item>
    ///   <item>Symmetry and mere-exposure weights are independent uniform samples.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public interface IAttractionProfileGenerator
    {
        /// <summary>
        /// Generates an <see cref="AttractionProfile"/> for a character of the given biology.
        /// </summary>
        /// <param name="biology">
        /// The observer's biological sex — used to calibrate preferred height and WHR baseline.
        /// </param>
        /// <param name="rng">Source of randomness (deterministic when seeded).</param>
        /// <returns>A freshly generated, immutable <see cref="AttractionProfile"/>.</returns>
        AttractionProfile Generate(SexBiology biology, IRandomSource rng);
    }

    /// <summary>
    /// Default implementation of <see cref="IAttractionProfileGenerator"/>.
    /// </summary>
    public sealed class AttractionProfileGenerator : IAttractionProfileGenerator
    {
        // ── Height preference baselines (cm) ────────────────────────────────────
        // Roughly: people tend to prefer partners close to their own sex-typical mean.
        private const double HeightMeanFemale = 175.0; // mean preferred height when observer is female
        private const double HeightMeanMale   = 170.0; // mean preferred height when observer is male
        private const double HeightStdDev     = 7.0;   // individual variation (σ)
        private const double HeightToleranceMin = 8.0;
        private const double HeightToleranceMax = 20.0;

        // ── WHR preference baselines ─────────────────────────────────────────────
        // Female target optimum ~0.70, male target optimum ~0.90
        private const double WhrMeanFemaleTarget  = 0.70;
        private const double WhrMeanMaleTarget    = 0.90;
        private const double WhrStdDev            = 0.06;

        /// <inheritdoc/>
        public AttractionProfile Generate(SexBiology biology, IRandomSource rng)
        {
            // Height preference — normal distribution around sex-adjusted mean
            var heightMean = biology == SexBiology.Female ? HeightMeanFemale : HeightMeanMale;
            var preferredHeight = Math.Clamp(
                heightMean + SampleNormal(rng) * HeightStdDev,
                140.0,
                210.0);

            // Height tolerance window — how strict the observer is
            var heightTolerance = Lerp(rng.NextUnit(), HeightToleranceMin, HeightToleranceMax);

            // Frame preference — uniform pick (no sex bias intentionally)
            var framePreference = PickFramePreference(rng);

            // WHR preference — depends on which sex the observer is typically attracted to.
            // For simplicity we use the observer's own biology as a proxy for orientation baseline.
            // A future IAttractionProfileGenerator overload could accept orientation explicitly.
            var whrMean = biology == SexBiology.Male ? WhrMeanFemaleTarget : WhrMeanMaleTarget;
            var preferredWhr = Math.Clamp(whrMean + SampleNormal(rng) * WhrStdDev, 0.55, 1.05);

            // Symmetry weight — how much the observer cares about facial symmetry
            var symmetryWeight = Math.Clamp(0.5 + SampleNormal(rng) * 0.15, 0.0, 1.0);

            // Mere-exposure weight — how much repeated contact boosts attraction
            var mereExposureWeight = Math.Clamp(0.5 + SampleNormal(rng) * 0.15, 0.0, 1.0);

            return new AttractionProfile(
                PreferredHeightCm:   Math.Round(preferredHeight, 1),
                HeightToleranceCm:   Math.Round(heightTolerance, 1),
                FramePreference:     framePreference,
                PreferredWhr:        Math.Round(preferredWhr, 3),
                SymmetryWeight:      Math.Round(symmetryWeight, 3),
                MereExposureWeight:  Math.Round(mereExposureWeight, 3));
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Approximates a standard-normal sample using the Box-Muller-lite trick
        /// (sum of three uniform samples, centred). Quick and good enough for generation.
        /// </summary>
        private static double SampleNormal(IRandomSource rng)
            => rng.NextUnit() + rng.NextUnit() + rng.NextUnit() - 1.5;

        private static double Lerp(double t, double a, double b)
            => a + (b - a) * t;

        private static BodyFramePreference PickFramePreference(IRandomSource rng)
        {
            // Uniform over None, Petite, Medium, Large
            var values = (BodyFramePreference[])Enum.GetValues(typeof(BodyFramePreference));
            var index  = (int)(rng.NextUnit() * values.Length);
            return values[Math.Clamp(index, 0, values.Length - 1)];
        }
    }
}
