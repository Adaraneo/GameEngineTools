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
        private const double MaxBasePhysical = 40.0;

        private const double MaxPreferenceMatch = 35.0;

        // ── WHR optimum windows (population-level baselines) ────────────────────
        private const double WhrOptimumFemale = 0.70;

        private const double WhrOptimumMale = 0.90;
        private const double WhrToleranceHalf = 0.12;

        // ── Height window (population-level) ────────────────────────────────────
        private const double HeightWindowHalf = 25.0;

        // ── First impression like constants ─────────────────────────────────────
        private const double HaloBase = 25.0;

        private const double HaloAttractionScale = 0.40;
        private const double ValenceLikeScale = 8.0;

        // ── A1: Excitatory transfer (Zillmann 1983) ──────────────────────────────
        // Arousal from ANY source boosts perceived attraction when base score > 50.
        // Max bonus at AcuteArousalLevel=100, baseScore=100: +ExcitatoryTransferMax
        private const double ExcitatoryTransferArousalThreshold = 40.0;

        private const double ExcitatoryTransferScoreThreshold = 50.0;
        private const double ExcitatoryTransferMax = 8.0;

        // ── A3: Age-match preference ─────────────────────────────────────────────
        // Similar-age attraction penalty/bonus, Gaussian with tolerance 10 years.
        private const double AgeMatchTolerance = 10.0;   // σ in years (half-tolerance)

        private const double AgeMatchWeight = 7.0;    // contribution to PreferenceMatch

        /// <inheritdoc/>
        public AttractionResult Calculate(
            AttractionProfile observerProfile,
            PhysicalAppearance targetAppearance,
            AppearanceView targetView,
            SexBiology targetBiology,
            double observerValence = 0.0,
            double observerArousal = 0.0,
            int? observerAgeYears = null,
            int? targetAgeYears = null)
        {
            var orientationWeight = SexualOrientationBehaviorMath.TargetAttractionWeight(observerProfile, targetBiology);
            var basePhysical = ComputeBasePhysical(targetAppearance, targetBiology) * orientationWeight;
            var preferenceMatch = ComputePreferenceMatch(
                observerProfile, targetAppearance, targetBiology,
                observerAgeYears, targetAgeYears) * orientationWeight;
            var stateModifier = ComputeStateModifier(targetView);

            var raw = basePhysical + preferenceMatch + stateModifier;
            var score = Math.Clamp(raw, 0.0, 100.0);

            // A1 — Excitatory transfer (Zillmann 1983; replaces Dutton & Aron misattribution model).
            // Physiological arousal from any source enhances perceived attraction,
            // but ONLY when base attraction is already above threshold (condition: score > 50).
            // "Transfer" requires a salient potential target — low-attraction targets don't benefit.
            var excitatoryBonus = ComputeExcitatoryTransfer(score, observerArousal);
            score = Math.Clamp(score + excitatoryBonus, 0.0, 100.0);

            var firstImpressionLike = ComputeFirstImpressionLike(score, observerValence);

            return new AttractionResult(
                Score: Math.Round(score, 2),
                BasePhysical: Math.Round(basePhysical, 2),
                PreferenceMatch: Math.Round(preferenceMatch, 2),
                StateModifier: Math.Round(stateModifier, 2),
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
            var whr = EstimateWhr(target, targetBiology);
            var whrOptimum = targetBiology == SexBiology.Female ? WhrOptimumFemale : WhrOptimumMale;
            var whrScore = TriangularScore(whr, whrOptimum, WhrToleranceHalf) * 18.0;

            // Height optimum is sex-specific — consistent with sex-specific WHR optimum above.
            // Population-level baselines (global averages): female ~163 cm, male ~176 cm.
            var heightOptimum = targetBiology == SexBiology.Female ? 163.0 : 176.0;
            var heightScore = TriangularScore(target.Body.Proportions.HeightCm, heightOptimum, HeightWindowHalf) * 12.0;

            // Structured morphology provides an explicit subtle-asymmetry signal.
            var symmetryScore = EstimateSymmetry(target) * 10.0;

            return Math.Clamp(whrScore + heightScore + symmetryScore, 0.0, MaxBasePhysical);
        }

        #endregion BasePhysical

        #region PreferenceMatch

        /// <summary>
        /// How closely the target matches the observer's personal <see cref="AttractionProfile"/>.
        /// Includes optional age-match scoring (A3) when both ages are provided.
        /// </summary>
        private static double ComputePreferenceMatch(
            AttractionProfile profile,
            PhysicalAppearance target,
            SexBiology targetBiology,
            int? observerAgeYears = null,
            int? targetAgeYears = null)
        {
            // Height preference match
            var heightMatch = TriangularScore(
                target.Body.Proportions.HeightCm,
                profile.PreferredHeightCm,
                profile.HeightToleranceCm) * 13.0;

            // Frame preference match
            var targetFramePref = FrameToPreference(DeriveFrame(target.Body));
            var frameMatch = (profile.FramePreference == BodyFramePreference.None ||
                              profile.FramePreference == targetFramePref)
                ? 8.0
                : 0.0;

            // WHR preference match
            var whr = EstimateWhr(target, targetBiology);
            var whrMatch = TriangularScore(whr, profile.PreferredWhr, WhrToleranceHalf) * 9.0;
            var symmetryMatch = EstimateSymmetry(target) * Math.Clamp(profile.SymmetryWeight, 0.0, 1.0) * 5.0;

            // A3 — Age-match preference.
            // Similar-age partners are preferred on average (Kenrick & Keefe 1992);
            // modelled as a Gaussian decay with tolerance ≈ 10 years.
            // Contributes up to 7 points; existing components reduced proportionally by
            // Math.Clamp to keep total ≤ MaxPreferenceMatch.
            var ageMatch = 0.0;
            if (observerAgeYears.HasValue && targetAgeYears.HasValue)
            {
                var ageDiff = Math.Abs(observerAgeYears.Value - targetAgeYears.Value);
                ageMatch = Math.Exp(-Math.Pow(ageDiff / AgeMatchTolerance, 2)) * AgeMatchWeight;
            }

            return Math.Clamp(heightMatch + frameMatch + whrMatch + symmetryMatch + ageMatch, 0.0, MaxPreferenceMatch);
        }

        #endregion PreferenceMatch

        #region StateModifier

        /// <summary>
        /// Current-state modifiers from <see cref="AppearanceView"/>.
        /// Positive: good posture. Negative: acne, bloating.
        /// </summary>
        private static double ComputeStateModifier(AppearanceView view)
        {
            var posture = (view.PostureScore - 50.0) * 0.10; // −5..+5
            var acne = -view.AcneLevel * 0.08;            // 0..−8
            var bloating = -(int)view.Bloating * 2.0;         // None=0, Light=−2, Medium=−4, High=−6

            return Math.Clamp(posture + acne + bloating, -15.0, 10.0);
        }

        #endregion StateModifier

        // ── Private helpers ──────────────────────────────────────────────────────

        #region Helpers

        /// <summary>
        /// Approximates waist-to-hip ratio from shoulder and hip breadth.
        /// A low hip/shoulder ratio in females (pear shape) maps to a low WHR proxy.
        /// Precision is intentionally low — we only model rough attractiveness signals.
        /// </summary>
        private static double EstimateWhr(PhysicalAppearance target, SexBiology biology)
        {
            _ = biology;
            return Math.Clamp(target.Body.Proportions.WaistToHipRatio, 0.55, 1.10);
        }

        private static double EstimateSymmetry(PhysicalAppearance target)
        {
            var asymmetry = target.Face.Asymmetry.FacialAsymmetry;
            return Math.Clamp(1.0 - asymmetry / 0.16, 0.0, 1.0);
        }

        /// <summary>
        /// A1 — Excitatory transfer (Zillmann 1983).
        /// Physiological arousal from a non-sexual source (exercise, fear, excitement)
        /// can be misattributed to a salient attractive target, boosting perceived attraction.
        /// Condition: arousal above threshold AND base score above threshold.
        /// Both factors must be non-trivial for the effect to register.
        /// </summary>
        private static double ComputeExcitatoryTransfer(double baseScore, double observerArousal)
        {
            if (observerArousal <= ExcitatoryTransferArousalThreshold) return 0.0;
            if (baseScore <= ExcitatoryTransferScoreThreshold) return 0.0;

            var arousalFactor = (observerArousal - ExcitatoryTransferArousalThreshold)
                                / (100.0 - ExcitatoryTransferArousalThreshold);  // 0..1
            var scoreFactor = (baseScore - ExcitatoryTransferScoreThreshold)
                                / (100.0 - ExcitatoryTransferScoreThreshold);    // 0..1

            return arousalFactor * scoreFactor * ExcitatoryTransferMax;
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
            var halo = HaloBase + attractionScore * HaloAttractionScale;
            var moodBoost = observerValence * ValenceLikeScale;

            return Math.Clamp(halo + moodBoost, 0.0, 100.0);
        }

        /// <summary>Maps a <see cref="BodyFrame"/> to the equivalent <see cref="BodyFramePreference"/>.</summary>
        private static BodyFramePreference FrameToPreference(BodyFrame frame)
        {
            return frame switch
            {
                BodyFrame.Petite => BodyFramePreference.Petite,
                BodyFrame.Medium => BodyFramePreference.Medium,
                BodyFrame.Large => BodyFramePreference.Large,
                BodyFrame.Strong => BodyFramePreference.Large,
                _ => BodyFramePreference.None
            };
        }

        private static BodyFrame DeriveFrame(BodyMorphology body)
        {
            var robustness = body.Skeletal.SkeletalRobustness;
            var muscularity = body.SoftTissue.Muscularity;
            var adiposity = body.SoftTissue.Adiposity;

            if (muscularity >= 0.68 && robustness >= 0.58)
            {
                return BodyFrame.Strong;
            }

            if (robustness <= 0.38 && adiposity <= 0.48)
            {
                return BodyFrame.Petite;
            }

            return robustness + adiposity * 0.45 >= 0.78 ? BodyFrame.Large : BodyFrame.Medium;
        }

        #endregion Helpers
    }
}
