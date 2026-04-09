// ActionArbitrationResult.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Arbitration
{
    /// <summary>
    /// Final arbitration result returned to the orchestrator after candidate resolution.
    /// </summary>
    internal sealed record ActionArbitrationResult(
        bool KeepRunningPlan,
        PlannedAction? SelectedPlan,
        BehaviorCandidate? SelectedCandidate,
        BehaviorState NewState);
}
