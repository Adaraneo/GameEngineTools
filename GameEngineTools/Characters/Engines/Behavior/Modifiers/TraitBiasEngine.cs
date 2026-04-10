// TraitBiasEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using GameEngineTools.Characters.Traits;
    using static ActionNames;

    /// <summary>
    /// Reserved extension point for stable trait shaping that should remain separate from transient state.
    /// </summary>
    internal sealed class TraitBiasEngine : IBehaviorModifierEngine
    {
        #region IBehaviorModifierEngine

        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var personality = context.HumanContext.Personality;
            var profile = context.HumanContext.PsychologyProfile;

            var productiveBias = Centered(personality.BigFive.Conscientiousness) * 6.0 + Centered(profile.Narrative.DiligenceIdentity) * 4.0;
            var socialBias = Centered(personality.Motivation.Affiliation) * 6.0 + Centered(personality.BigFive.Extraversion) * 5.0 + Centered(profile.Narrative.BelongingIdentity) * 3.0;
            var curiosityBias = Centered(personality.Motivation.Curiosity) * 6.0 + Centered(personality.BigFive.Openness) * 4.0;
            var privacyBias = Centered(personality.BigFive.Neuroticism) * 4.0 + (personality.Attachment == AttachmentStyle.Avoidant ? 2.5 : 0.0);
            var intimacyBias = personality.Sociosexuality switch
            {
                Sociosexuality.Restricted => -2.0,
                Sociosexuality.Unrestricted => 2.0,
                _ => 0.0
            };

            BehaviorCandidateEditor.Add(candidates, Work, productiveBias);
            BehaviorCandidateEditor.Add(candidates, Create, productiveBias * 0.7 + curiosityBias * 0.6);
            BehaviorCandidateEditor.Add(candidates, MoveToWork, productiveBias * 0.4);

            BehaviorCandidateEditor.Add(candidates, ReachOut, socialBias);
            BehaviorCandidateEditor.Add(candidates, MoveToSocial, socialBias * 0.8);
            BehaviorCandidateEditor.Add(candidates, InviteIntimacy, socialBias * 0.4 + intimacyBias);

            BehaviorCandidateEditor.Add(candidates, MoveToPublic, curiosityBias);
            BehaviorCandidateEditor.Add(candidates, MoveToPrivate, privacyBias);
            BehaviorCandidateEditor.Add(candidates, SelfCare, privacyBias * 0.5);
        }

        #endregion

        #region Helpers

        private static double Centered(double value) => value - 0.5;

        #endregion
    }
}
