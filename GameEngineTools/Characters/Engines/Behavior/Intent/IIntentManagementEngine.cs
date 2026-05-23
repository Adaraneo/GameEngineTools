// IIntentManagementEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Intent
{
    /// <summary>
    /// Maintains and applies the lightweight intent layer between modifiers and arbitration.
    /// </summary>
    internal interface IIntentManagementEngine
    {
        BehaviorState UpdateIntent(BehaviorContext context, IReadOnlyList<BehaviorCandidate> candidates);

        void ApplyBias(BehaviorContext context, List<BehaviorCandidate> candidates);
    }
}
