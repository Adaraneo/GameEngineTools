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
        Ambient,

        /// <summary>
        /// The body of a deceased character, spawned at the place of death.
        /// A mourner can inter it (<see cref="GameEngineTools.Characters.Engines.ActionNames.Bury"/>),
        /// which consumes the corpse and produces a <see cref="Grave"/>.
        /// </summary>
        Corpse,

        /// <summary>
        /// A grave carrying a deceased character's identity, produced by burial.
        /// Mourners visiting it generate grave-visit grief modulation (continuing bonds).
        /// </summary>
        Grave
    }
}
