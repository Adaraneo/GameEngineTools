// IHumanContext.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using System;
    using Microsoft.Extensions.Logging;
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
    /// Immutable per-tick context handed to every engine: read-only identity/traits, the
    /// engines snapshot, occupied action slots, and the four shared services.
    /// </summary>
    public interface IHumanContext
    {
        /// <summary>Character identifier.</summary>
        HumanId Id { get; }

        /// <summary>Name and birth-date identity.</summary>
        Identity Identity { get; }

        /// <summary>Biological sex.</summary>
        SexBiology Biology { get; }

        /// <summary>Big Five personality traits.</summary>
        Personality Personality { get; }

        /// <summary>Psychological trait profile.</summary>
        PsychologicalProfile PsychologyProfile { get; }

        /// <summary>Attraction-relevant traits, or <c>null</c>.</summary>
        AttractionProfile? AttractionProfile { get; }

        /// <summary>Read-only snapshot of all engine states.</summary>
        EnginesSnapshot Snapshot { get; }

        /// <summary>
        /// Combined bitmask of all body/mind channels currently occupied by non-expired actions.
        /// Set by OrchestratedHuman before each behavior tick.
        /// </summary>
        ActionSlotMask OccupiedSlots { get; }

        /// <summary>Body mass in kilograms (from the projected appearance); 0 when unknown, then engines use their reference mass.</summary>
        double BodyMassKg => 0.0;

        /// <summary>Event bus for publishing/subscribing to domain events.</summary>
        IEventBus EventBus { get; }

        /// <summary>Scheduler for deferred actions.</summary>
        IScheduler Scheduler { get; }

        /// <summary>Deterministic per-character random source.</summary>
        IRandomSource Random { get; }

        /// <summary>Logger.</summary>
        ILogger Logger { get; }
    }
}
