// IBehaviorModifierEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    internal interface IBehaviorModifierEngine { void Modify(BehaviorContext context, List<BehaviorCandidate> candidates); }
}
