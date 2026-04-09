// CircadianArousalEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using static ActionNames;

    /// <summary>
    /// Applies lightweight chronotype and time-of-day shaping to location-oriented behavior.
    /// </summary>
    internal sealed class CircadianArousalEngine : IBehaviorModifierEngine
    {
        #region IBehaviorModifierEngine

        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var bonus = BehaviorMath.ComputeChronoBonus(context.Now, context.HumanContext.Personality.Chronotype);

            // The same arousal signal is distributed across destinations with conservative weights.
            BehaviorCandidateEditor.Add(candidates, MoveToSocial, bonus);
            BehaviorCandidateEditor.Add(candidates, MoveToPrivate, bonus * 0.5);
            BehaviorCandidateEditor.Add(candidates, MoveToWork, bonus * 0.35);
            BehaviorCandidateEditor.Add(candidates, MoveToPublic, bonus * 0.4);
        }

        #endregion
    }
}
