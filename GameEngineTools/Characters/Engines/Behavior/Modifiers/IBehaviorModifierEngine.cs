// IBehaviorModifierEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    /// <summary>
    /// Adjusts candidate utility or availability without making the final action choice.
    /// </summary>
    internal interface IBehaviorModifierEngine
    {
        void Modify(BehaviorContext context, List<BehaviorCandidate> candidates);
    }
}
