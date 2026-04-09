// ISleepCoordinator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Sleep
{
    using Characters.Core;

    /// <summary>
    /// Handles sleep-specific runtime flow that is intentionally separated from ordinary needs.
    /// </summary>
    internal interface ISleepCoordinator
    {
        SleepDecisionResult Tick(BehaviorContext context);
        BehaviorState Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox, BehaviorState state);
        void RestoreState();
    }
}
