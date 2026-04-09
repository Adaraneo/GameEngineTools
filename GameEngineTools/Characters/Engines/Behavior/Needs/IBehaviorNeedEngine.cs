// IBehaviorNeedEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Needs
{
    /// <summary>
    /// Produces domain-specific drives and initial candidates from the current context.
    /// </summary>
    internal interface IBehaviorNeedEngine
    {
        BehaviorNeedOutput Evaluate(BehaviorContext context);
    }
}
