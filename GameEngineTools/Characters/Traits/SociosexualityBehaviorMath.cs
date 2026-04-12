// SociosexualityBehaviorMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using System;
    using GameEngineTools.Characters.Engines.Relationships;

    /// <summary>
    /// Contextual behavior weights for sociosexuality.
    /// Sexuality controls drive intensity; sociosexuality controls threshold and context sensitivity.
    /// </summary>
    public static class SociosexualityBehaviorMath
    {
        #region Behavior scoring

        public static double InviteIntimacyTraitBias(Sociosexuality sociosexuality)
            => sociosexuality switch
            {
                Sociosexuality.Restricted => -1.0,
                Sociosexuality.Unrestricted => +1.0,
                _ => 0.0
            };

        public static double IntimacyTargetScoreAdjustment(
            Sociosexuality sociosexuality,
            RelationshipEdge? relationship,
            double vulnerabilitySafety,
            double rejectionRisk,
            double expectedAcceptance)
        {
            var trust = Normalize(relationship?.Trust, fallback: 30.0);
            var comfort = Normalize(relationship?.Comfort, fallback: 30.0);
            var closeness = Normalize(relationship?.Closeness, fallback: 0.0);
            var attraction = IntimacyAttraction(relationship);

            return sociosexuality switch
            {
                Sociosexuality.Restricted => Math.Clamp(
                    vulnerabilitySafety * 0.16
                    + trust * 0.08
                    + comfort * 0.08
                    + closeness * 0.07
                    - rejectionRisk * 0.22,
                    -0.24,
                    0.18),

                Sociosexuality.Unrestricted => Math.Clamp(
                    attraction * 0.14
                    + expectedAcceptance * 0.06
                    - rejectionRisk * 0.10
                    - Math.Max(0.0, 0.22 - vulnerabilitySafety) * 0.20,
                    -0.16,
                    0.18),

                _ => 0.0
            };
        }

        public static double InviteIntimacyUtilityMultiplier(
            Sociosexuality sociosexuality,
            RelationshipEdge? relationship,
            double vulnerabilitySafety,
            double rejectionRisk,
            double expectedAcceptance)
        {
            var trust = Normalize(relationship?.Trust, fallback: 30.0);
            var comfort = Normalize(relationship?.Comfort, fallback: 30.0);
            var attraction = IntimacyAttraction(relationship);

            return sociosexuality switch
            {
                Sociosexuality.Restricted => Math.Clamp(
                    0.68
                    + vulnerabilitySafety * 0.24
                    + expectedAcceptance * 0.12
                    + trust * 0.12
                    + comfort * 0.10
                    - rejectionRisk * 0.30,
                    0.45,
                    1.08),

                Sociosexuality.Unrestricted => Math.Clamp(
                    0.96
                    + attraction * 0.16
                    + expectedAcceptance * 0.10
                    - rejectionRisk * 0.12,
                    0.78,
                    1.22),

                _ => 1.0
            };
        }

        public static bool BlocksIntimacy(
            Sociosexuality sociosexuality,
            RelationshipEdge? relationship,
            double vulnerabilitySafety,
            double rejectionRisk)
        {
            var trust = Normalize(relationship?.Trust, fallback: 30.0);
            var comfort = Normalize(relationship?.Comfort, fallback: 30.0);
            var closeness = Normalize(relationship?.Closeness, fallback: 0.0);

            return sociosexuality switch
            {
                Sociosexuality.Restricted => vulnerabilitySafety < 0.50
                    || rejectionRisk > 0.60
                    || closeness < 0.50
                    || trust < 0.42
                    || comfort < 0.45,

                Sociosexuality.Unrestricted => vulnerabilitySafety < 0.28
                    || rejectionRisk > 0.82
                    || closeness < 0.24,

                _ => vulnerabilitySafety < 0.38 || rejectionRisk > 0.72 || closeness < 0.40
            };
        }

        #endregion

        #region Interaction acceptance

        public static double InviteAcceptanceBias(
            Sociosexuality sociosexuality,
            RelationshipEdge? relationship,
            double expectedAcceptance,
            bool hasPrivacy)
        {
            var trust = Normalize(relationship?.Trust, fallback: 30.0);
            var comfort = Normalize(relationship?.Comfort, fallback: 30.0);
            var closeness = Normalize(relationship?.Closeness, fallback: 0.0);
            var attraction = IntimacyAttraction(relationship);

            return sociosexuality switch
            {
                Sociosexuality.Restricted => Math.Clamp(
                    -0.18
                    + trust * 0.07
                    + comfort * 0.06
                    + closeness * 0.06
                    + expectedAcceptance * 0.05
                    + (hasPrivacy ? 0.03 : -0.04),
                    -0.22,
                    0.08),

                Sociosexuality.Unrestricted => Math.Clamp(
                    -0.03
                    + attraction * 0.10
                    + expectedAcceptance * 0.05
                    + (hasPrivacy ? 0.02 : 0.0),
                    -0.08,
                    0.14),

                _ => 0.0
            };
        }

        public static double IntimateTouchAcceptanceBias(
            Sociosexuality sociosexuality,
            RelationshipEdge? relationship,
            bool hasPrivacy)
        {
            var trust = Normalize(relationship?.Trust, fallback: 30.0);
            var comfort = Normalize(relationship?.Comfort, fallback: 30.0);
            var attraction = IntimacyAttraction(relationship);

            return sociosexuality switch
            {
                Sociosexuality.Restricted => Math.Clamp(-0.16 + trust * 0.06 + comfort * 0.06 + (hasPrivacy ? 0.04 : -0.05), -0.20, 0.08),
                Sociosexuality.Unrestricted => Math.Clamp(-0.03 + attraction * 0.12 + (hasPrivacy ? 0.03 : 0.0), -0.08, 0.14),
                _ => 0.0
            };
        }

        public static bool BlocksIntimateTouch(
            Sociosexuality sociosexuality,
            RelationshipEdge? relationship)
        {
            var closeness = relationship?.Closeness ?? 0.0;
            var comfort = relationship?.Comfort ?? 0.0;
            var intimacyInterest = relationship is null
                ? 0.0
                : (relationship.SexualInterest * 0.65) + (relationship.RomanticInterest * 0.35);

            return sociosexuality switch
            {
                Sociosexuality.Restricted => closeness < 70 || comfort < 60 || intimacyInterest < 65,
                Sociosexuality.Unrestricted => closeness < 55 || intimacyInterest < 50,
                _ => closeness < 60 || intimacyInterest < 55
            };
        }

        #endregion

        #region Relationship deltas

        public static double SexualInterestDeltaMultiplier(Sociosexuality sociosexuality)
            => sociosexuality switch
            {
                Sociosexuality.Restricted => 0.65,
                Sociosexuality.Unrestricted => 1.35,
                _ => 1.0
            };

        public static double RomanticInviteDeltaMultiplier(Sociosexuality sociosexuality)
            => sociosexuality switch
            {
                Sociosexuality.Restricted => 1.25,
                Sociosexuality.Unrestricted => 0.85,
                _ => 1.0
            };

        public static double ComfortInviteDelta(Sociosexuality sociosexuality)
            => sociosexuality switch
            {
                Sociosexuality.Restricted => 0.4,
                Sociosexuality.Unrestricted => -0.1,
                _ => 0.0
            };

        #endregion

        #region Helpers

        private static double Normalize(double? value, double fallback)
            => Math.Clamp((value ?? fallback) / 100.0, 0.0, 1.0);

        private static double IntimacyAttraction(RelationshipEdge? relationship)
        {
            if (relationship is null)
            {
                return 0.0;
            }

            return Math.Clamp(
                relationship.SexualInterest * 0.40
                + relationship.PhysicalAttraction * 0.25
                + relationship.AestheticAttraction * 0.20
                + relationship.RomanticInterest * 0.15,
                0.0,
                100.0) / 100.0;
        }

        #endregion
    }
}
