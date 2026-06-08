// HumanContext.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using Microsoft.Extensions.Logging;

    /// <summary>Default mutable-by-orchestrator implementation of <see cref="IHumanContext"/>.</summary>
    public sealed class HumanContext : IHumanContext
    {
        /// <inheritdoc/>
        public HumanId Id { get; init; }
        /// <inheritdoc/>
        public Identity Identity { get; internal set; } = default!;
        /// <inheritdoc/>
        public SexBiology Biology { get; init; }
        /// <inheritdoc/>
        public Traits.Personality Personality { get; init; } = default!;
        /// <inheritdoc/>
        public Traits.PsychologicalProfile PsychologyProfile { get; init; } = Traits.PsychologicalProfile.Default;
        /// <inheritdoc/>
        public Traits.AttractionProfile? AttractionProfile { get; init; }
        /// <inheritdoc/>
        public EnginesSnapshot Snapshot { get; internal set; } = default!;
        /// <summary>
        /// Combined bitmask of all body/mind channels currently occupied by non-expired actions.
        /// Set by OrchestratedHuman before each behavior tick; read by DefaultBehaviorEngine
        /// to gate secondary action selection.
        /// </summary>
        public Engines.Behavior.ActionSlotMask OccupiedSlots { get; internal set; } = Engines.Behavior.ActionSlotMask.None;
        /// <inheritdoc/>
        public IEventBus EventBus { get; init; } = default!;
        /// <inheritdoc/>
        public IScheduler Scheduler { get; init; } = default!;
        /// <inheritdoc/>
        public IRandomSource Random { get; init; } = default!;
        /// <inheritdoc/>
        public ILogger Logger { get; init; } = default!;
    }
}
