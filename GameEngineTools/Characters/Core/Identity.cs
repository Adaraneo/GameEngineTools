// Identity.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using System;
    using Characters.Engines.Behavior;
    using Characters.Engines.Goals;
    using Characters.Engines.Interactions;
    using Characters.Engines.Physiology;
    using Characters.Engines.Psychology;
    using Characters.Engines.Relationships;
    using Characters.Engines.Schedule;
    using Characters.Engines.SemanticMemory;
    using Characters.Traits;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.World.Core.Astro;
    using GameEngineTools.World.Utils.Time;

    /// <summary>A character's name and birth date plus optional home-location and ancestry hints.</summary>
    /// <param name="FirstName">Given name.</param>
    /// <param name="LastName">Surname.</param>
    /// <param name="BirthDate">Date of birth.</param>
    public sealed record Identity(Name FirstName, Surname LastName, WDateOnly BirthDate)
    {
        /// <summary>
        /// Location ID that this character considers "home territory".
        /// When <c>InteractionSurface.Location</c> matches this value, ambient noise is treated
        /// as controllable → noise-induced stress is reduced by <c>HomeNoiseStressMultiplier</c>
        /// (Glass &amp; Singer 1972: controllable noise has dramatically lower cortisol response).
        /// <c>null</c> means no home is assigned (e.g. travellers, characters without fixed residence).
        /// </summary>
        public string? HomeLocationId { get; init; }

        /// <summary>
        /// Optional ancestry hint used by the portrait and narrative systems.
        /// Expressed as a short natural-language descriptor understood by image generation models.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Examples: <c>"East Asian ancestry"</c>, <c>"West African features"</c>,
        /// <c>"Northern European ancestry"</c>, <c>"South Asian features"</c>.
        /// </para>
        /// <para>
        /// Set once at character-creation time by the identity generator.
        /// The portrait pipeline forwards this verbatim to the image model prompt.
        /// <c>null</c> means ancestry is unspecified — the prompt omits the hint entirely.
        /// </para>
        /// </remarks>
        public string? AncestryHint { get; init; }
    }
}
