// SexualOrientationBehaviorMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Relationships;

    /// <summary>
    /// Runtime behavior weights derived from a stable <see cref="AttractionProfile"/>.
    /// Orientation modulates romantic and sexual signals; it does not decide platonic liking.
    /// </summary>
    public static class SexualOrientationBehaviorMath
    {
        #region Target weights

        /// <summary>
        /// Returns the target-sex attraction weight in [0, 1].
        /// Missing profile or target biology means neutral behavior.
        /// </summary>
        public static double TargetAttractionWeight(AttractionProfile? profile, SexBiology? targetBiology)
        {
            if (profile is null || targetBiology is null)
            {
                return 1.0;
            }

            return targetBiology.Value switch
            {
                SexBiology.Female => Math.Clamp(profile.FemaleTargetAttraction, 0.0, 1.0),
                SexBiology.Male => Math.Clamp(profile.MaleTargetAttraction, 0.0, 1.0),
                _ => Math.Clamp(profile.OtherTargetAttraction, 0.0, 1.0)
            };
        }

        /// <summary>
        /// Multiplier for romantic-interest growth. Kept softer than sexual weighting because
        /// sexual orientation is not a complete model of romantic attachment.
        /// </summary>
        public static double RomanticInterestMultiplier(AttractionProfile? profile, SexBiology? targetBiology)
        {
            if (profile is null || targetBiology is null)
            {
                return 1.0;
            }

            var weight = TargetAttractionWeight(profile, targetBiology);
            return Math.Clamp(0.25 + weight * 0.75, 0.20, 1.0);
        }

        /// <summary>
        /// Multiplier for sexual-interest growth. Low target attraction strongly dampens, but does not
        /// hard-block, because the simulation keeps probabilistic overlap and relationship context.
        /// </summary>
        public static double SexualInterestMultiplier(AttractionProfile? profile, SexBiology? targetBiology)
        {
            if (profile is null || targetBiology is null)
            {
                return 1.0;
            }

            var weight = TargetAttractionWeight(profile, targetBiology);
            return Math.Clamp(0.05 + weight * 0.95, 0.05, 1.0);
        }

        #endregion Target weights

        #region Runtime behavior

        /// <summary>
        /// Small bounded acceptance bias for intimacy-coded social invitations.
        /// Neutral or missing orientation data intentionally returns zero.
        /// </summary>
        public static double InviteAcceptanceBias(AttractionProfile? profile, SexBiology? targetBiology)
        {
            if (profile is null || targetBiology is null)
            {
                return 0.0;
            }

            var weight = TargetAttractionWeight(profile, targetBiology);
            return Math.Clamp((weight - 0.5) * 0.14, -0.07, 0.07);
        }

        /// <summary>
        /// Slightly stronger orientation term for explicitly sexual encounter resolution.
        /// Still bounded so sociosexuality, privacy, stress, and relationship quality remain relevant.
        /// </summary>
        public static double SexualEncounterAcceptanceBias(AttractionProfile? profile, SexBiology? targetBiology)
        {
            if (profile is null || targetBiology is null)
            {
                return 0.0;
            }

            var weight = TargetAttractionWeight(profile, targetBiology);
            return Math.Clamp((weight - 0.5) * 0.20, -0.09, 0.09);
        }

        /// <summary>
        /// Applies orientation as a target-ranking modifier for intimacy candidates.
        /// Relationship history remains dominant; this only shapes ambiguous choices.
        /// </summary>
        public static double IntimacyTargetScoreMultiplier(AttractionProfile? profile, RelationshipEdge? relationship)
            => IntimacyTargetScoreMultiplier(profile, relationship?.TargetBiology);

        /// <summary>
        /// Applies orientation as a target-ranking modifier for intimacy candidates when only target biology is known.
        /// </summary>
        public static double IntimacyTargetScoreMultiplier(AttractionProfile? profile, SexBiology? targetBiology)
        {
            if (profile is null || targetBiology is null)
            {
                return 1.0;
            }

            var weight = TargetAttractionWeight(profile, targetBiology);
            return Math.Clamp(0.55 + weight * 0.45, 0.55, 1.0);
        }

        #endregion Runtime behavior
    }
}
