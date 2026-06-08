// IAttractionCalculator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Attraction
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Calculates how attractive character <c>A</c> finds character <c>B</c>,
    /// and what initial like score would arise from a first impression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attraction is <b>asymmetric</b>: A may find B attractive while B is indifferent to A.
    /// Each call to <see cref="Calculate"/> is a fresh snapshot — call it on-demand
    /// (e.g. at <c>FirstImpressionFormed</c> or when updating a relationship edge).
    /// </para>
    /// <para>
    /// <b>Score composition:</b>
    /// <list type="number">
    ///   <item><term>BasePhysical</term><description>Evolutionary signals independent of individual preference (WHR, height range, symmetry).</description></item>
    ///   <item><term>PreferenceMatch</term><description>How well B matches A's <see cref="AttractionProfile"/>.</description></item>
    ///   <item><term>StateModifier</term><description>B's current <see cref="AppearanceView"/> — posture, skin, bloating.</description></item>
    ///   <item><term>MereExposure</term><description>Bonus from repeated positive contact (familiarity effect).</description></item>
    /// </list>
    /// Final score is clamped to [0, 100].
    /// </para>
    /// <para>
    /// <b>First impression like:</b>
    /// Derived from the halo effect — physical attraction biases initial liking.
    /// The observer's current emotional valence modulates the baseline.
    /// Only meaningful when <c>positiveInteractionCount</c> is 0.
    /// </para>
    /// </remarks>
    public interface IAttractionCalculator
    {
        /// <summary>
        /// Computes how attractive observer <paramref name="observerProfile"/> finds the target
        /// described by <paramref name="targetAppearance"/> and <paramref name="targetView"/>,
        /// and derives an initial like score based on the halo effect.
        /// </summary>
        /// <param name="observerProfile">
        /// The observer's personal attraction preferences.
        /// </param>
        /// <param name="targetAppearance">
        /// Stable physical traits of the target (height, frame, facial features…).
        /// </param>
        /// <param name="targetView">
        /// Current projected appearance of the target (posture, skin, bloating…).
        /// </param>
        /// <param name="targetBiology">
        /// Biological sex of the target — used for WHR baseline selection.
        /// </param>
        /// <param name="observerValence">
        /// The observer's current emotional valence in [−1, +1].
        /// Positive mood boosts <see cref="AttractionResult.FirstImpressionLike"/>;
        /// negative mood reduces it. Defaults to <c>0.0</c> (neutral).
        /// </param>
        /// <returns>
        /// An <see cref="AttractionResult"/> with the final score and a per-component breakdown.
        /// </returns>
        /// <remarks>
        /// The mere-exposure effect is <b>not</b> computed here — it lives in
        /// <c>DefaultRelationshipsEngine</c>, which applies a logarithmic bonus to
        /// <c>Attraction</c> on every accepted <c>InteractionOutcome</c>.
        /// </remarks>
        AttractionResult Calculate(
            AttractionProfile observerProfile,
            PhysicalAppearance targetAppearance,
            AppearanceView targetView,
            SexBiology targetBiology,
            double observerValence = 0.0,
            /// <summary>
            /// Observer's current physiological arousal level [0–100] from
            /// <c>PhysiologyState.AcuteArousalLevel</c>.
            /// Used for excitatory transfer (Zillmann 1983): arousal from any source boosts
            /// perceived attraction when base attraction is already above 50.
            /// Defaults to 0 (no arousal) — backward compatible.
            /// </summary>
            double observerArousal = 0.0,
            /// <summary>
            /// Observer's age in years. Combined with <paramref name="targetAgeYears"/> for
            /// age-match scoring (A3). Pass <c>null</c> to skip age-match.
            /// </summary>
            int? observerAgeYears = null,
            /// <summary>Target's age in years. Pass <c>null</c> to skip age-match.</summary>
            int? targetAgeYears = null);
    }

    /// <summary>
    /// Result of a single attraction calculation, including a per-component breakdown for diagnostics.
    /// </summary>
    /// <param name="Score">
    /// Final attraction score in [0, 100]. This is the value that goes into
    /// <c>FirstImpressionFormed.Attraction</c> for first-impression seeding.
    /// </param>
    /// <param name="BasePhysical">
    /// Contribution from evolutionary baseline signals (WHR, height, symmetry).
    /// Range: approximately [0, 40].
    /// </param>
    /// <param name="PreferenceMatch">
    /// Contribution from how well the target matches the observer's personal preferences.
    /// Range: approximately [0, 35].
    /// </param>
    /// <param name="StateModifier">
    /// Modifier from the target's current appearance state (posture, skin, cycle).
    /// Can be negative. Range: approximately [−15, +10].
    /// </param>
    /// <param name="FirstImpressionLike">
    /// Initial like score derived from the halo effect and the observer's emotional valence.
    /// Use this as <c>FirstImpressionFormed.Like</c>.
    /// Range: [0, 100].
    /// </param>
    public sealed record AttractionResult(
        double Score,
        double BasePhysical,
        double PreferenceMatch,
        double StateModifier,
        double FirstImpressionLike)
    {
        /// <summary>
        /// Creates a neutral result with all components zeroed, score at 50 and like at 45.
        /// Useful as a fallback when no profile is available.
        /// </summary>
        /// <remarks>
        /// Like defaults to 45 rather than 50 — an unknown stranger is not automatically liked.
        /// </remarks>
        public static AttractionResult Neutral => new(50, 0, 0, 0, 45);
    }
}
