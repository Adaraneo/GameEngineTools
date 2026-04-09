// SleepDecisionResult.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Sleep
{
    /// <summary>
    /// Result of sleep handling for the current tick, including early-return semantics.
    /// </summary>
    internal sealed record SleepDecisionResult(
        bool ConsumedTick,
        BehaviorState NewState);
}
