// SleepDecisionResult.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Sleep
{
    internal sealed record SleepDecisionResult(bool ConsumedTick, BehaviorState NewState);
}
