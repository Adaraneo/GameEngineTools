// TraitBiasEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    /// <summary>
    /// Reserved extension point for stable trait shaping that should remain separate from transient state.
    /// </summary>
    internal sealed class TraitBiasEngine : IBehaviorModifierEngine
    {
        #region IBehaviorModifierEngine

        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
        }

        #endregion
    }
}
