// IPortraitSpecBuilder.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation.Portraits
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Builds a deterministic portrait specification from stable character data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The builder is the only place that translates raw appearance data
    /// (centimetres, enum values, float ranges) into natural-language descriptors
    /// suitable for image generation models. Callers must supply age and ancestry
    /// so the formatter can produce a complete, model-ready prompt.
    /// </para>
    /// </remarks>
    public interface IPortraitSpecBuilder
    {
        /// <summary>
        /// Builds a portrait specification from the supplied character data.
        /// </summary>
        /// <param name="biology">Biological sex of the character.</param>
        /// <param name="appearance">Stable physical appearance traits.</param>
        /// <param name="ageYears">
        /// Character's current age in years.
        /// Critical for portrait realism — without age the model defaults to a generic ~25-year-old.
        /// </param>
        /// <param name="ancestryHint">
        /// Optional ancestry hint expressed as natural language,
        /// e.g. "East Asian ancestry", "West African features".
        /// Pass <c>null</c> if unspecified — the formatter will omit the hint entirely.
        /// </param>
        /// <param name="snapshot">
        /// Optional runtime snapshot used for conservative expression mapping.
        /// When <c>null</c> the expression defaults to <see cref="PortraitExpressionKind.Neutral"/>.
        /// </param>
        /// <returns>A fully populated, immutable portrait specification.</returns>
        PortraitSpec Build(
            SexBiology biology,
            PhysicalAppearance appearance,
            int ageYears,
            string? ancestryHint = null,
            EnginesSnapshot? snapshot = null);
    }
}
