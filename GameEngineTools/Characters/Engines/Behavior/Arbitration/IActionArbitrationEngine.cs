// IActionArbitrationEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Arbitration
{
    /// <summary>
    /// Resolves the fully shaped candidate list into the final action plan for the tick.
    /// </summary>
    internal interface IActionArbitrationEngine
    {
        ActionArbitrationResult Arbitrate(BehaviorContext context, List<BehaviorCandidate> candidates);
    }
}
