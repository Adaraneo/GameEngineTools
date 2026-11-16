// SexBiology.cs
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

    /// <summary>Biological sex.</summary>
    public enum SexBiology
    {
        /// <summary>Female.</summary>
        Female,

        /// <summary>Male.</summary>
        Male,

        /// <summary>Intersex.</summary>
        Intersex,

        /// <summary>Unknown / unspecified.</summary>
        Unknown
    }
}
