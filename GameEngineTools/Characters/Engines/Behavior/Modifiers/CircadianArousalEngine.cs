// CircadianArousalEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using static ActionNames;

    internal sealed class CircadianArousalEngine : IBehaviorModifierEngine
    {
        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var bonus = BehaviorMath.ComputeChronoBonus(context.Now, context.HumanContext.Personality.Chronotype);
            BehaviorCandidateEditor.Add(candidates, MoveToSocial, bonus);
            BehaviorCandidateEditor.Add(candidates, MoveToPrivate, bonus * 0.5);
            BehaviorCandidateEditor.Add(candidates, MoveToWork, bonus * 0.35);
            BehaviorCandidateEditor.Add(candidates, MoveToPublic, bonus * 0.4);
        }
    }
}
