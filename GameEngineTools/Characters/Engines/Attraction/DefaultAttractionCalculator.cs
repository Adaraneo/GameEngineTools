// DefaultAttractionCalculator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Attraction
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Default implementation of <see cref="IAttractionCalculator"/>.
    /// Computes attraction as a sum of four independent components, each with a defined ceiling.
    /// Also derives an initial like score from the halo effect for use in first impressions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Component ceilings (sum = 100):</b>
    /// <list type="table">
    ///   <item><term>BasePhysical</term><description>max 40 — WHR + height range + symmetry</description></item>
    ///   <item><term>PreferenceMatch</term><description>max 35 — personal height/frame/WHR preference</description></item>
    ///   <item><term>StateModifier</term><description>−15..+10 — posture, skin, bloating</description></item>
    ///   <item><term>MereExposure</term><description>max 15 — familiarity bonus</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>FirstImpressionLike formula:</b>
    /// <c>Like = 25 + Attraction × 0.40 + ObserverValence × 8</c> clamped to [0, 100].
    /// Calibration: Attraction 50 → Like ~45; Attraction 80 → Like ~57; Attraction 20 → Like ~33.
    /// </para>
    /// <para>
    /// The calculator is a <b>pure function</b> — no state, no side-effects.
    /// Register as a singleton in DI; the same instance can be reused across all characters.
    /// </para>
    /// </remarks>
    public sealed class DefaultAttractionCalculator : IAttractionCalculator
    {
        // ── Component ceilings ───────────────────────────────────────────────────
        private const double MaxBasePhysical    = 40.0;
        private const double MaxPreferenceMatch = 35.0;
        private const double MaxMereExposure    = 15.0;

        // ── WHR optimum windows (population-level baselines) ────────────────────
        private const double WhrOptimumFemale   = 0.70;
        private const double WhrOptimumMale     = 0.90;
        private const double WhrToleranceHalf   = 0.12; // ±0.12 before score drops to 0

        // ── Height window (population-level) ────────────────────────────────────
        private const double HeightWindowHalf   = 25.0; // ±25 cm before base score drops to 0

        // ── Mere-exposure cap and scale ──────────────────────────────────────────
        private const int    MereExposureSaturationCount = 20; // interactions to reach max bonus

        // ── First impression like constants ─────────────────────────────────────
        // Like = HaloBase + Attraction × HaloAttractionScale + Valence × ValenceLikeScale
        // Attraction 50 → Like 45 (neutral stranger, not automatically liked)
        // Attraction 80 → Like 57
        // Attraction 20 → Like 33
        private const double HaloBase            = 25.0;
        private const double HaloAttractionScale = 0.40;
        private const double ValenceLikeScale    = 8.0;  // valence ±1 → ±8 Like points

        /// <inheritdoc/>
        public AttractionResult Calculate(
            AttractionProfile observerProfile,
            PhysicalAppearance targetAppearance,
            AppearanceView targetView,
            SexBiology targetBiology,
            int positiveInteractionCount = 0,
            double observerValence = 0.0)
        {
            var basePhysical    = ComputeBasePhysical(targetAppearance, targetBiology);
            var preferenceMatch = ComputePreferenceMatch(observerProfile, targetAppearance, targetBiology);
            var stateModifier   = ComputeStateModifier(targetView);
            var mereExposure    = ComputeMereExposure(observerProfile, positiveInteractionCount);

            var raw   = basePhysical + preferenceMatch + stateModifier + mereExposure;
            var score = Math.Clamp(raw, 0.0, 100.0);

            var firstImpressionLike = ComputeFirstImpressionLike(score, observerValence);

            return new AttractionResult(
                Score:               Math.Round(score, 2),
                BasePhysical:        Math.Round(basePhysical, 2),
                PreferenceMatch:     Math.Round(preferenceMatch, 2),
                StateModifier:       Math.Round(stateModifier, 2),
                MereExposure:        Math.Round(mereExposure, 2),
                FirstImpressionLike: Math.Round(firstImpressionLike, 2));
        }

        // ── Component calculations ───────────────────────────────────────────────

        #region BasePhysical

        /// <summary>
        /// Evolutionary baseline — WHR, height within plausible range, facial symmetry.
        /// Independent of the observer's individual preferences.
        /// </summary>
        private static double ComputeBasePhysical(PhysicalAppearance target, SexBiology targetBiology)
        {
            // WHR approximation from shoulder/hip measurements
            // True WHR = waist/hip; we don't track waist, so we approximate via hip/shoulder ratio.
            // For females, hip > shoulder is attractive; for males, shoulder > hip is attractive.
            var whr        = EstimateWhr(target, targetBiology);
            var whrOptimum = targetBiology == SexBiology.Female ? WhrOptimumFemale : WhrOptimumMale;
            var whrScore   = TriangularScore(whr, whrOptimum, WhrToleranceHalf) * 18.0;

            // Height within a broad "plausible partner" range (population-level baseline)
            var heightScore = TriangularScore(target.HeightCm, 170.0, HeightWindowHalf) * 12.0;

            // Facial symmetry proxy — NoseProminence and LipFullness both near 0.5 implies symmetry
            var symmetryProxy = 1.0 - Math.Abs(target.NoseProminence - 0.5) * 2.0
                                    - Math.Abs(target.LipFullness    - 0.5) * 2.0;
            var symmetryScore = Math.Max(0.0, symmetryProxy) * 10.0;

            return Math.Clamp(whrScore + heightScore + symmetryScore, 0.0, MaxBasePhysical);
        }

        #endregion BasePhysical

        #region PreferenceMatch

        /// <summary>
        /// How closely the target matches the observer's personal <see cref="AttractionProfile"/>.
        /// </summary>
        private static double ComputePreferenceMatch(
            AttractionProfile profile,
            PhysicalAppearance target,
            SexBiology targetBiology)
        {
            // Height preference match
            var heightMatch = TriangularScore(
                target.HeightCm,
                profile.PreferredHeightCm,
                profile.HeightToleranceCm) * 15.0;

            // Frame preference match
            var targetFramePref = FrameToPreference(target.Frame);
            var frameMatch = (profile.FramePreference == BodyFramePreference.None ||
                              profile.FramePreference == targetFramePref)
                ? 10.0
                : 0.0;

            // WHR preference match
            var whr      = EstimateWhr(target, targetBiology);
            var whrMatch = TriangularScore(whr, profile.PreferredWhr, WhrToleranceHalf) * 10.0;

            return Math.Clamp(heightMatch + frameMatch + whrMatch, 0.0, MaxPreferenceMatch);
        }

        #endregion PreferenceMatch

        #region StateModifier

        /// <summary>
        /// Current-state modifiers from <see cref="AppearanceView"/>.
        /// Positive: good posture. Negative: acne, bloating.
        /// </summary>
        private static double ComputeStateModifier(AppearanceView view)
        {
            var posture  = (view.PostureScore - 50.0) * 0.10; // −5..+5
            var acne     = -view.AcneLevel * 0.08;            // 0..−8
            var bloating = -(int)view.Bloating * 2.0;         // None=0, Light=−2, Medium=−4, High=−6

            return Math.Clamp(posture + acne + bloating, -15.0, 10.0);
        }

        #endregion StateModifier

        #region MereExposure

        /// <summary>
        /// Familiarity bonus — the more positive interactions, the higher the bonus,
        /// up to <see cref="MaxMereExposure"/> scaled by <see cref="AttractionProfile.MereExposureWeight"/>.
        /// </summary>
        private static double ComputeMereExposure(AttractionProfile profile, int positiveInteractionCount)
        {
            if (positiveInteractionCount <= 0)
            {
                return 0.0;
            }

            // Logarithmic growth: fast at first, then levels off
            var saturation = Math.Log(1.0 + positiveInteractionCount)
                           / Math.Log(1.0 + MereExposureSaturationCount);

            return Math.Clamp(
                saturation * MaxMereExposure * profile.MereExposureWeight,
                0.0,
                MaxMereExposure);
        }

        #endregion MereExposure

        // ── Private helpers ──────────────────────────────────────────────────────

        #region Helpers

        /// <summary>
        /// Approximates waist-to-hip ratio from shoulder and hip breadth.
        /// A low hip/shoulder ratio in females (pear shape) maps to a low WHR proxy.
        /// Precision is intentionally low — we only model rough attractiveness signals.
        /// </summary>
        private static double EstimateWhr(PhysicalAppearance target, SexBiology biology)
        {
            // Crude proxy: hip/(shoulder + hip) normalised to a WHR-like range
            var ratio = target.HipBreadthCm / (target.ShoulderBreadthCm + target.HipBreadthCm);

            // Map to ~0.60..1.00 range
            return biology == SexBiology.Female
                ? 0.55 + ratio * 0.50
                : 0.75 + ratio * 0.35;
        }

        /// <summary>
        /// Returns a score in [0, 1] that peaks at <paramref name="optimum"/>
        /// and falls linearly to 0 at <c>±<paramref name="halfWindow"/></c>.
        /// </summary>
        private static double TriangularScore(double value, double optimum, double halfWindow)
        {
            if (halfWindow <= 0.0)
            {
                return value == optimum ? 1.0 : 0.0;
            }

            var distance = Math.Abs(value - optimum);
            return Math.Max(0.0, 1.0 - distance / halfWindow);
        }

        /// <summary>
        /// Derives an initial like score from the halo effect.
        /// </summary>
        /// <remarks>
        /// Formula: <c>25 + attractionScore × 0.40 + observerValence × 8</c>
        /// The halo effect means physically attractive targets are perceived as more likeable
        /// on first contact, before any personality information is available.
        /// Observer mood (valence) shifts the baseline up or down by up to 8 points.
        /// </remarks>
        /// <param name="attractionScore">Computed attraction score in [0, 100].</param>
        /// <param name="observerValence">Observer's current emotional valence in [−1, +1].</param>
        private static double ComputeFirstImpressionLike(double attractionScore, double observerValence)
        {
            var halo      = HaloBase + attractionScore * HaloAttractionScale;
            var moodBoost = observerValence * ValenceLikeScale;

            return Math.Clamp(halo + moodBoost, 0.0, 100.0);
        }

        /// <summary>Maps a <see cref="BodyFrame"/> to the equivalent <see cref="BodyFramePreference"/>.</summary>
        private static BodyFramePreference FrameToPreference(BodyFrame frame)
        {
            return frame switch
            {
                BodyFrame.Petite  => BodyFramePreference.Petite,
                BodyFrame.Medium  => BodyFramePreference.Medium,
                BodyFrame.Large   => BodyFramePreference.Large,
                BodyFrame.Strong  => BodyFramePreference.Large,
                _                 => BodyFramePreference.None
            };
        }

        #endregion Helpers
    }
}
