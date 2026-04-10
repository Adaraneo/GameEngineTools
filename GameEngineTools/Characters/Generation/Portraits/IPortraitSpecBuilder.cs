// IPortraitSpecBuilder.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation.Portraits
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Builds a deterministic portrait specification from stable appearance data.
    /// </summary>
    public interface IPortraitSpecBuilder
    {
        /// <summary>
        /// Builds a portrait specification from the supplied appearance data.
        /// </summary>
        /// <param name="biology">Biological sex carried by the character.</param>
        /// <param name="appearance">Stable physical appearance traits.</param>
        /// <param name="snapshot">Optional runtime snapshot used for conservative expression mapping.</param>
        /// <returns>A deterministic portrait specification.</returns>
        PortraitSpec Build(SexBiology biology, PhysicalAppearance appearance, EnginesSnapshot? snapshot = null);
    }
}
