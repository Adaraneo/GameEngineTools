// AttractionProfile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    /// <summary>
    /// Persistent physical-preference profile generated once per character at creation time.
    /// Describes what the character finds attractive in others — independent of psychology.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a biological/developmental layer, deliberately kept separate from
    /// <see cref="GameEngineTools.Characters.Core.Personality"/>, which models psychological traits.
    /// Physical preferences are generated once and never change during simulation.
    /// </para>
    /// <para>
    /// All preference values are expressed as desired midpoints (0..1 or enum).
    /// The calculator uses a proximity function: the closer the observed trait is to the
    /// preferred midpoint, the higher the contribution to the attraction score.
    /// </para>
    /// </remarks>
    public sealed record AttractionProfile(
        /// <summary>Preferred height in centimetres (centre of a tolerance window).</summary>
        double PreferredHeightCm,

        /// <summary>Width of the height tolerance window in centimetres (±).</summary>
        double HeightToleranceCm,

        /// <summary>Preferred body frame.</summary>
        BodyFramePreference FramePreference,

        /// <summary>
        /// Preferred waist-to-hip ratio (WHR). Typical attractive range: 0.67–0.80 female, 0.85–0.95 male.
        /// Stored as a midpoint; tolerance is fixed by the calculator.
        /// </summary>
        double PreferredWhr,

        /// <summary>
        /// Weight of facial symmetry in the score (0..1).
        /// Higher value means the character is more sensitive to symmetry cues.
        /// </summary>
        double SymmetryWeight,

        /// <summary>
        /// Weight of the mere-exposure bonus applied per positive interaction (0..1).
        /// Higher value means familiarity has more impact than initial physical impression.
        /// </summary>
        double MereExposureWeight
    );

    /// <summary>
    /// Body-frame preference category used by <see cref="AttractionProfile"/>.
    /// </summary>
    public enum BodyFramePreference
    {
        /// <summary>No strong preference — all frames evaluated equally.</summary>
        None,

        /// <summary>Preference for petite or slender builds.</summary>
        Petite,

        /// <summary>Preference for medium / average builds.</summary>
        Medium,

        /// <summary>Preference for larger or strong builds.</summary>
        Large
    }
}
