// HumanRuntimeOptions.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Hosting
{
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Runtime orchestration options for newly created humans.
    /// </summary>
    public sealed class HumanRuntimeOptions
    {
        /// <summary>
        /// Optional cadence for behaviour-level reasoning.
        /// When null or non-positive, behavior runs every tick.
        /// </summary>
        public WTimeSpan? BehaviorDecisionStep { get; init; }
    }
}
