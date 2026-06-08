// IObjectInteractionEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Resolves character-object interactions committed by the Behavior engine.
    /// Sits between Interactions and Relationships in the orchestration pipeline.
    /// </summary>
    public interface IObjectInteractionEngine
    {
        /// <summary>Reacts to an object-interaction domain event.</summary>
        /// <param name="event">The event to handle.</param>
        /// <param name="ctx">Character context.</param>
        /// <param name="outbox">Collector for emitted events.</param>
        void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox);

        /// <summary>Advances any in-progress object interactions.</summary>
        /// <param name="now">Current game time.</param>
        /// <param name="dt">Elapsed time since the previous tick.</param>
        /// <param name="ctx">Character context.</param>
        /// <param name="outbox">Collector for emitted events.</param>
        void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox);
    }
}
