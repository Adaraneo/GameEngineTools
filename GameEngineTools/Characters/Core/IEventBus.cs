// IEventBus.cs
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

    /// <summary>In-process publish/subscribe event bus.</summary>
    public interface IEventBus
    {
        /// <summary>Publishes an event to all subscribers.</summary>
        /// <param name="event">The event to publish.</param>
        void Publish(IDomainEvent @event);

        /// <summary>Subscribes a handler to events of type <typeparamref name="TEvent"/>.</summary>
        /// <typeparam name="TEvent">Event type to subscribe to.</typeparam>
        /// <param name="handler">Handler invoked for each matching event.</param>
        /// <returns>A disposable that cancels the subscription.</returns>
        IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class, IDomainEvent;
    }
}
