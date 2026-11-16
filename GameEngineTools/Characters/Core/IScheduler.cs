// IScheduler.cs
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

    /// <summary>Schedules actions to run at or after a given game time.</summary>
    public interface IScheduler
    {
        /// <summary>Schedules an action at an absolute game time.</summary>
        /// <param name="when">When to run the action.</param>
        /// <param name="action">The action to run.</param>
        /// <param name="tag">Optional tag for grouping/cancellation.</param>
        /// <returns>An identifier that can be used to cancel the action.</returns>
        ScheduledId ScheduleAt(WDateTime when, ScheduledAction action, string? tag = null);

        /// <summary>Schedules an action after a delay relative to <paramref name="now"/>.</summary>
        /// <param name="now">Reference time.</param>
        /// <param name="delay">Delay before the action runs.</param>
        /// <param name="action">The action to run.</param>
        /// <param name="tag">Optional tag for grouping/cancellation.</param>
        /// <returns>An identifier that can be used to cancel the action.</returns>
        ScheduledId ScheduleAfter(WDateTime now, WTimeSpan delay, ScheduledAction action, string? tag = null);

        /// <summary>Cancels a scheduled action.</summary>
        /// <param name="id">Identifier of the action to cancel.</param>
        /// <returns><c>true</c> if an action was cancelled.</returns>
        bool Cancel(ScheduledId id);

        /// <summary>Returns the actions due at or before <paramref name="now"/> (pull model).</summary>
        /// <param name="now">Current game time.</param>
        IEnumerable<(ScheduledId id, ScheduledAction action)> Due(WDateTime now);

        /// <summary>Drops every pending action (a character's scheduler is its own). The default does nothing, for test stubs.</summary>
        void Clear() { }
    }
}
