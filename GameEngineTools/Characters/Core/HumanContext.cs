// HumanContext.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using Microsoft.Extensions.Logging;

    public sealed class HumanContext : IHumanContext
    {
        public HumanId Id { get; init; }
        public Identity Identity { get; internal set; } = default!;
        public SexBiology Biology { get; init; }
        public Traits.Personality Personality { get; init; } = default!;
        public Traits.PsychologicalProfile PsychologyProfile { get; init; } = Traits.PsychologicalProfile.Default;
        public Traits.AttractionProfile? AttractionProfile { get; init; }
        public EnginesSnapshot Snapshot { get; internal set; } = default!;
        /// <summary>
        /// Combined bitmask of all body/mind channels currently occupied by non-expired actions.
        /// Set by OrchestratedHuman before each behavior tick; read by DefaultBehaviorEngine
        /// to gate secondary action selection.
        /// </summary>
        public Engines.Behavior.ActionSlotMask OccupiedSlots { get; internal set; } = Engines.Behavior.ActionSlotMask.None;
        public IEventBus EventBus { get; init; } = default!;
        public IScheduler Scheduler { get; init; } = default!;
        public IRandomSource Random { get; init; } = default!;
        public ILogger Logger { get; init; } = default!;
    }
}
