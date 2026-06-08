// IDailyScheduleEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Schedule
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;

    #region ScheduleSlot

    /// <summary>
    /// A single time-anchored routine entry in a character's daily schedule.
    /// </summary>
    /// <param name="SlotId">Stable identifier — used for cancellation and logging.</param>
    /// <param name="HourOfDay">
    /// Hour of day (0..HoursPerDay-1) when this slot fires.
    /// The scheduler converts this to an absolute <see cref="WDateTime"/> each day.
    /// </param>
    /// <param name="PreferredAction">
    /// The action name from <c>ActionNames</c> this slot recommends
    /// (e.g. <c>ActionNames.Work</c>, <c>ActionNames.ReachOut</c>).
    /// </param>
    /// <param name="PreferredLocationId">
    /// Optional location the character should move to before the action.
    /// When set and the character is elsewhere, <c>MoveTo</c> bias is also applied.
    /// </param>
    /// <param name="BiasStrength">
    /// How strongly this slot biases the action utility [0..1].
    /// Applied as: <c>flatBias = BiasStrength × Config.MaxSlotFlatBias</c>.
    /// </param>
    /// <param name="CanSkipWhenStressed">
    /// When <c>true</c>, bias is reduced to zero if stress exceeds
    /// <see cref="DailyScheduleConfig.SkipStressThreshold"/> or energy falls below
    /// <see cref="DailyScheduleConfig.SkipEnergyThreshold"/>.
    /// Social and rest slots should set this to <c>false</c>.
    /// </param>
    public sealed record ScheduleSlot(
        string SlotId,
        int HourOfDay,
        string PreferredAction,
        string? PreferredLocationId = null,
        double BiasStrength = 0.7,
        bool CanSkipWhenStressed = true);

    #endregion ScheduleSlot

    #region DailyScheduleState

    /// <summary>
    /// Immutable per-character schedule state carried between ticks.
    /// </summary>
    public sealed record DailyScheduleState(
        /// <summary>All slots in this character's routine. Stable across days.</summary>
        IReadOnlyList<ScheduleSlot> Slots,

        /// <summary>
        /// The slot that fired in the current tick (set by PhaseA, cleared after BehaviorEngine).
        /// <c>null</c> when no slot fired this tick.
        /// </summary>
        ScheduleSlot? ActiveSlot,

        /// <summary>Day index of the last day for which slots were scheduled.</summary>
        long LastScheduledDayIndex,

        /// <summary>
        /// Occupation ID used when seeding — for diagnostics and persistence.
        /// <c>null</c> or empty string means no fixed occupation.
        /// </summary>
        string? Occupation)
    {
        /// <summary>Empty state for characters with no schedule.</summary>
        public static DailyScheduleState Empty { get; } =
            new(Array.Empty<ScheduleSlot>(), null, -1, null);
    }

    #endregion DailyScheduleState

    #region Domain events

    /// <summary>
    /// Emitted when the engine registers slots for a new day via <see cref="IScheduler"/>.
    /// </summary>
    public sealed record ScheduleDayRegistered(
        WDateTime OccurredAt,
        HumanId Human,
        int SlotCount,
        long DayIndex) : IDomainEvent;

    /// <summary>
    /// Emitted when a scheduled slot fires (arrives via PhaseA from IScheduler).
    /// Picked up by <see cref="DailyScheduleBehaviorModifier"/> the same tick.
    /// </summary>
    public sealed record ScheduleSlotTriggered(
        WDateTime OccurredAt,
        HumanId Human,
        string SlotId,
        string PreferredAction,
        string? PreferredLocationId,
        double BiasStrength) : IDomainEvent;

    /// <summary>
    /// Emitted when a slot bias is actually applied to a behavior candidate.
    /// </summary>
    public sealed record ScheduleSlotBiasApplied(
        WDateTime OccurredAt,
        HumanId Human,
        string SlotId,
        string Action,
        double Bias) : IDomainEvent;

    #endregion Domain events

    #region IDailyScheduleEngine

    /// <summary>
    /// Manages a character's daily routine, anchoring time-of-day slots to the
    /// <see cref="IScheduler"/> and translating fired slots into behavior bias.
    /// </summary>
    public interface IDailyScheduleEngine : IEngine<DailyScheduleState, DailyScheduleConfig>
    {
        /// <summary>
        /// Seeds initial schedule slots from the given occupation and personality,
        /// then immediately registers today's slots with <paramref name="scheduler"/>.
        /// Call once from <see cref="GameEngineTools.Characters.Hosting.IHumanFactory.Create"/> before the first tick.
        /// </summary>
        /// <param name="occupationId">
        /// Occupation ID looked up in <see cref="IOccupationRegistry"/>.
        /// <c>null</c> or empty means no schedule (no slots registered).
        /// Use <see cref="OccupationIds"/> constants for built-in occupations.
        /// </param>
        /// <param name="personality">Personality used for chronotype and motivation modulation.</param>
        /// <param name="now">Current world time — used to anchor the first day.</param>
        /// <param name="scheduler">Character's scheduler — receives the timed callbacks.</param>
        /// <param name="humanId">Character identifier for all emitted events.</param>
        void SeedFromOccupation(
            string? occupationId,
            Personality personality,
            WDateTime now,
            IScheduler scheduler,
            HumanId humanId);
    }

    #endregion IDailyScheduleEngine
}
