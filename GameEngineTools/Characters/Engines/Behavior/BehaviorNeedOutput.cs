// BehaviorNeedOutput.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    /// <summary>
    /// Combined output of a need engine: domain pressure plus concrete candidate actions.
    /// </summary>
    internal sealed record BehaviorNeedOutput(
        IReadOnlyList<BehaviorDrive> Drives,
        IReadOnlyList<BehaviorCandidate> Candidates)
    {
        /// <summary>
        /// Empty output — no drives, no candidates.
        /// Returned when a need engine has nothing to contribute this tick.
        /// </summary>
        internal static readonly BehaviorNeedOutput Empty = new(Array.Empty<BehaviorDrive>(), Array.Empty<BehaviorCandidate>());
    }
}
