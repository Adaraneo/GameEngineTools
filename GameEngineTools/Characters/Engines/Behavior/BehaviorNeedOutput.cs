// BehaviorNeedOutput.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    internal sealed record BehaviorNeedOutput(IReadOnlyList<BehaviorDrive> Drives, IReadOnlyList<BehaviorCandidate> Candidates);
}
