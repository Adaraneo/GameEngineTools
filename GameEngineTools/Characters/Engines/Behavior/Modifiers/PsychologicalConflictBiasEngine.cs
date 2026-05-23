// PsychologicalConflictBiasEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using GameEngineTools.Characters.Traits;
    using static ActionNames;

    /// <summary>
    /// Applies self-narrative and coping-style pressure before final arbitration.
    /// </summary>
    internal sealed class PsychologicalConflictBiasEngine : IBehaviorModifierEngine
    {
        #region IBehaviorModifierEngine

        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var profile = context.HumanContext.PsychologyProfile;
            var stress = context.HumanContext.Snapshot.Psychology.Stress / 100.0;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var bias = IdentityBias(profile, candidate.Name) + CopingBias(profile, stress, candidate.Name);
                if (Math.Abs(bias) < 0.001) continue;
                candidates[i] = candidate with { Utility = Math.Max(0.0, candidate.Utility + bias) };
            }
        }

        #endregion IBehaviorModifierEngine

        #region Helpers

        private static double IdentityBias(PsychologicalProfile profile, string actionName)
            => actionName switch
            {
                Work or Create => profile.Narrative.DiligenceIdentity * 8.0,
                SelfCare => (1.0 - profile.Narrative.ToughnessIdentity) * 6.0,
                ReachOut or MoveToSocial => profile.Narrative.BelongingIdentity * 7.0,
                InviteIntimacy => profile.Narrative.BelongingIdentity * 4.0,
                _ => 0.0
            };

        private static double CopingBias(PsychologicalProfile profile, double stress, string actionName)
            => profile.Coping switch
            {
                CopingStyle.Avoidant when actionName is ReachOut or InviteIntimacy => -(4.0 + stress * 6.0),
                CopingStyle.Avoidant when actionName is SelfCare or Idle => 2.0 + stress * 3.0,
                CopingStyle.PeoplePleasing when actionName is ReachOut or MoveToSocial => 3.0 + stress * 3.0,
                CopingStyle.Rationalizing when actionName is Work or Create => 2.5 + stress * 2.0,
                CopingStyle.Humor when actionName is MoveToSocial or ReachOut => 2.0,
                CopingStyle.AggressiveCompensation when actionName is Work => 3.0 + stress * 4.0,
                CopingStyle.AggressiveCompensation when actionName is SelfCare => -2.5,
                _ => 0.0
            };

        #endregion Helpers
    }
}
