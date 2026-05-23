// WorldObjectCategory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    /// <summary>
    /// Semantic category of a world object.
    /// Used by behavior engines to classify affordances and
    /// drive NPC decision-making without visual rendering.
    /// </summary>
    /// <remarks>
    /// Categories are intentionally broad — the fine-grained detail
    /// lives in <see cref="WorldObject.Affordances"/>.
    /// </remarks>
    public enum WorldObjectCategory
    {
        /// <summary>
        /// Furniture or fixture — chair, table, bench, shelf.
        /// Typically provides Rest or Work affordances.
        /// </summary>
        Furniture,

        /// <summary>
        /// Food source or consumable — apple, bread.
        /// Provides Hunger satisfaction.
        /// </summary>
        Food,

        /// <summary>
        /// Drink source - water, meed, beer, ale.
        /// Provides Thirst satisfaction.
        /// </summary>
        Drink,

        /// <summary>
        /// Light source — torch, candle, fireplace, lantern.
        /// Affects mood (valence), safety perception and HeatSignature.
        /// </summary>
        LightSource,

        /// <summary>
        /// Tool or instrument — hammer, lute, quill.
        /// Provides Work or Entertainment affordances.
        /// </summary>
        Tool,

        /// <summary>
        /// Shelter element — bed, tent, alcove.
        /// Provides maximum Rest affordance; enables sleep context.
        /// </summary>
        Shelter,

        /// <summary>
        /// Potential threat or hazard — fire, trap, weapon rack.
        /// Can raise stress and suppress approach behavior.
        /// </summary>
        Hazard,

        /// <summary>
        /// Decorative or ambient element — painting, statue, banner.
        /// Low behavioral impact; may contribute to valence.
        /// </summary>
        Ambient
    }
}
