// IActionArbitrationEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Arbitration
{
    internal interface IActionArbitrationEngine { ActionArbitrationResult Arbitrate(BehaviorContext context, List<BehaviorCandidate> candidates); }
}
