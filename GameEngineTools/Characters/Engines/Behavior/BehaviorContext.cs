// BehaviorContext.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    internal sealed record BehaviorContext(WDateTime Now, WTimeSpan Dt, IHumanContext HumanContext, IEventCollector Outbox, BehaviorState State, BehaviorConfig Config, IReadOnlyDictionary<string, double> Cooldowns);
}
