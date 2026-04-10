// BehaviorContext.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Immutable snapshot of everything behavior sub-engines need for one orchestration pass.
    /// </summary>
    internal sealed record BehaviorContext(
        WDateTime Now,
        WTimeSpan Dt,
        IHumanContext HumanContext,
        IEventCollector Outbox,
        BehaviorState State,
        BehaviorConfig Config,
        IReadOnlyDictionary<string, double> Cooldowns,
        IDictionary<string, DecisionWorkingSet>? DecisionWorkingSets = null);
}
