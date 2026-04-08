// AffectiveStateEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using static ActionNames;

    internal sealed class AffectiveStateEngine : IBehaviorModifierEngine
    {
        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var ps = context.HumanContext.Snapshot.Psychology;
            if (ps.Stress <= 60 && ps.Valence >= -0.5) return;
            var selfCareBoost = 1.0 + Math.Max(0, ps.Stress - 60) / 400.0 + Math.Max(0, -ps.Valence - 0.5) * 0.1;
            BehaviorCandidateEditor.Multiply(candidates, SelfCare, selfCareBoost);
        }
    }
}
