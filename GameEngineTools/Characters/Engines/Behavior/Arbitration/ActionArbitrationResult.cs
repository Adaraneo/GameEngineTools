// ActionArbitrationResult.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Arbitration
{
    internal sealed record ActionArbitrationResult(bool KeepRunningPlan, PlannedAction? SelectedPlan, BehaviorCandidate? SelectedCandidate, BehaviorState NewState);
}
