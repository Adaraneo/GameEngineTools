// BehaviorNeedOutput.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    /// <summary>
    /// Combined output of a need engine: domain pressure plus concrete candidate actions.
    /// </summary>
    internal sealed record BehaviorNeedOutput(
        IReadOnlyList<BehaviorDrive> Drives,
        IReadOnlyList<BehaviorCandidate> Candidates);
}
