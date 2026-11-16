// IEngine.cs
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
    /// Common contract for all character engines: exposes immutable <see cref="State"/> and
    /// <see cref="Config"/> and the deterministic tick/handle/restore lifecycle.
    /// </summary>
    /// <typeparam name="TState">The engine's state record type.</typeparam>
    /// <typeparam name="TConfig">The engine's configuration record type.</typeparam>
    public interface IEngine<TState, TConfig>
    {
        /// <summary>Current engine state.</summary>
        TState State { get; }

        /// <summary>Engine configuration.</summary>
        TConfig Config { get; }

        /// <summary>Deterministically advances state from the given inputs and elapsed time.</summary>
        /// <param name="now">Current game time.</param>
        /// <param name="dt">Elapsed time since the previous tick.</param>
        /// <param name="ctx">Read-only character context and services.</param>
        /// <param name="outbox">Collector for events emitted this tick.</param>
        void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox);

        /// <summary>Reacts to a domain event from another engine or the environment.</summary>
        /// <param name="event">The event to handle.</param>
        /// <param name="ctx">Read-only character context and services.</param>
        /// <param name="outbox">Collector for events emitted in response.</param>
        void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox);

        /// <summary>Restores the engine to a previously persisted state.</summary>
        /// <param name="state">The state to restore.</param>
        void RestoreState(TState state);
    }
}
