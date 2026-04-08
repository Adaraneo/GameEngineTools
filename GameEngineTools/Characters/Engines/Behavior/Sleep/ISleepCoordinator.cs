// ISleepCoordinator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Sleep
{
    using Characters.Core;

    internal interface ISleepCoordinator
    {
        SleepDecisionResult Tick(BehaviorContext context);
        BehaviorState Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox, BehaviorState state);
        void RestoreState();
    }
}
