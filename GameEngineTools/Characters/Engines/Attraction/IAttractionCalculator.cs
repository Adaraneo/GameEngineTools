// IAttractionCalculator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Attraction
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Calculates how attractive character <c>A</c> finds character <c>B</c>.
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
    /// </remarks>
    public interface IAttractionCalculator
    {
        /// <summary>
        /// Computes how attractive observer <paramref name="observerProfile"/> finds the target
        /// described by <paramref name="targetAppearance"/> and <paramref name="targetView"/>.
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
        /// <param name="positiveInteractionCount">
        /// Number of accepted positive interactions the observer has had with the target so far.
        /// Used to compute the mere-exposure bonus. Pass <c>0</c> for a first impression.
        /// </param>
        /// <returns>
        /// An <see cref="AttractionResult"/> with the final score and a per-component breakdown.
        /// </returns>
        AttractionResult Calculate(
            AttractionProfile observerProfile,
            PhysicalAppearance targetAppearance,
            AppearanceView targetView,
            SexBiology targetBiology,
            int positiveInteractionCount = 0);
    }

    /// <summary>
    /// Result of a single attraction calculation, including a per-component breakdown for diagnostics.
    /// </summary>
    /// <param name="Score">
    /// Final attraction score in [0, 100]. This is the value that goes into
    /// <c>FirstImpressionFormed.Attraction</c> or a <c>RelationshipEdge</c>.
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
    /// <param name="MereExposure">
    /// Bonus from repeated positive contact. Range: [0, 15].
    /// </param>
    public sealed record AttractionResult(
        double Score,
        double BasePhysical,
        double PreferenceMatch,
        double StateModifier,
        double MereExposure)
    {
        /// <summary>
        /// Creates a neutral result with all components zeroed and score at 50.
        /// Useful as a fallback when no profile is available.
        /// </summary>
        public static AttractionResult Neutral => new(50, 0, 0, 0, 0);
    }
}
