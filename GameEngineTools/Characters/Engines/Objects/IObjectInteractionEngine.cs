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
        void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox);

        void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox);
    }
}
