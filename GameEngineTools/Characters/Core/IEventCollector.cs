// IEventCollector.cs
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

    /// <summary>
    /// Lightweight per-tick event collector. Engines add events during a tick; the
    /// orchestrator drains and publishes them afterwards.
    /// </summary>
    public interface IEventCollector
    {
        /// <summary>Adds an event to the collector.</summary>
        /// <param name="event">The event to add.</param>
        void Add(IDomainEvent @event);

        /// <summary>Removes and returns all collected events.</summary>
        IReadOnlyList<IDomainEvent> Drain(); // used by the orchestrator
    }
}
