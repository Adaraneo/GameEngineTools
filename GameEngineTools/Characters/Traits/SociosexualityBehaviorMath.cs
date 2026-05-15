// SociosexualityBehaviorMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using System;
    using GameEngineTools.Characters.Engines.Relationships;

    /// <summary>
    /// Contextual behavior weights for sociosexuality.
    /// Sexuality controls drive intensity; sociosexuality controls threshold and context sensitivity.
    /// Each method uses the SOI-R facet most relevant to its function:
    /// Desire → initiative and utility; Attitude → thresholds and blocking; Behavior → relationship deltas.
    /// </summary>
    public static class SociosexualityBehaviorMath
    {
        #region Behavior scoring

        public static double InviteIntimacyTraitBias(Sociosexuality soi)
            => (soi.Desire - 0.5) * 2.0;

        public static double IntimacyTargetScoreAdjustment(
            Sociosexuality soi,
            RelationshipEdge? relationship,
            double vulnerabilitySafety,
            double rejectionRisk,
            double expectedAcceptance)
        {
            var trust = Normalize(relationship?.Trust, fallback: 30.0);
            var comfort = Normalize(relationship?.Comfort, fallback: 30.0);
            var closeness = Normalize(relationship?.Closeness, fallback: 0.0);
            var attraction = IntimacyAttraction(relationship);

            var restrictedVal = Math.Clamp(
                vulnerabilitySafety * 0.16
                + trust * 0.08
                + comfort * 0.08
                + closeness * 0.07
                - rejectionRisk * 0.22,
                -0.24,
                0.18);

            var unrestrictedVal = Math.Clamp(
                attraction * 0.14
                + expectedAcceptance * 0.06
                - rejectionRisk * 0.10
                - Math.Max(0.0, 0.22 - vulnerabilitySafety) * 0.20,
                -0.16,
                0.18);

            return Lerp(restrictedVal, unrestrictedVal, soi.Attitude);
        }

        public static double InviteIntimacyUtilityMultiplier(
            Sociosexuality soi,
            RelationshipEdge? relationship,
            double vulnerabilitySafety,
            double rejectionRisk,
            double expectedAcceptance)
        {
            var trust = Normalize(relationship?.Trust, fallback: 30.0);
            var comfort = Normalize(relationship?.Comfort, fallback: 30.0);
            var attraction = IntimacyAttraction(relationship);

            var restrictedVal = Math.Clamp(
                0.68
                + vulnerabilitySafety * 0.24
                + expectedAcceptance * 0.12
                + trust * 0.12
                + comfort * 0.10
                - rejectionRisk * 0.30,
                0.45,
                1.08);

            var unrestrictedVal = Math.Clamp(
                0.96
                + attraction * 0.16
                + expectedAcceptance * 0.10
                - rejectionRisk * 0.12,
                0.78,
                1.22);

            return Lerp(restrictedVal, unrestrictedVal, (soi.Desire + soi.Attitude) / 2.0);
        }

        public static bool BlocksIntimacy(
            Sociosexuality soi,
            RelationshipEdge? relationship,
            double vulnerabilitySafety,
            double rejectionRisk)
        {
            var trust = Normalize(relationship?.Trust, fallback: 30.0);
            var comfort = Normalize(relationship?.Comfort, fallback: 30.0);
            var closeness = Normalize(relationship?.Closeness, fallback: 0.0);

            var minVulSafety  = Lerp(0.50, 0.28, soi.Attitude);
            var maxRejRisk    = Lerp(0.60, 0.82, soi.Attitude);
            var minCloseness  = Lerp(0.50, 0.24, soi.Attitude);
            var minTrust      = Lerp(0.42, 0.0,  soi.Attitude);
            var minComfort    = Lerp(0.45, 0.0,  soi.Attitude);

            return vulnerabilitySafety < minVulSafety
                || rejectionRisk > maxRejRisk
                || closeness < minCloseness
                || (minTrust  > 0.01 && trust   < minTrust)
                || (minComfort > 0.01 && comfort < minComfort);
        }

        #endregion

        #region Interaction acceptance

        public static double InviteAcceptanceBias(
            Sociosexuality soi,
            RelationshipEdge? relationship,
            double expectedAcceptance,
            bool hasPrivacy)
        {
            var trust = Normalize(relationship?.Trust, fallback: 30.0);
            var comfort = Normalize(relationship?.Comfort, fallback: 30.0);
            var closeness = Normalize(relationship?.Closeness, fallback: 0.0);
            var attraction = IntimacyAttraction(relationship);

            var restrictedVal = Math.Clamp(
                -0.18
                + trust * 0.07
                + comfort * 0.06
                + closeness * 0.06
                + expectedAcceptance * 0.05
                + (hasPrivacy ? 0.03 : -0.04),
                -0.22,
                0.08);

            var unrestrictedVal = Math.Clamp(
                -0.03
                + attraction * 0.10
                + expectedAcceptance * 0.05
                + (hasPrivacy ? 0.02 : 0.0),
                -0.08,
                0.14);

            return Lerp(restrictedVal, unrestrictedVal, soi.Attitude);
        }

        public static double IntimateTouchAcceptanceBias(
            Sociosexuality soi,
            RelationshipEdge? relationship,
            bool hasPrivacy)
        {
            var trust = Normalize(relationship?.Trust, fallback: 30.0);
            var comfort = Normalize(relationship?.Comfort, fallback: 30.0);
            var attraction = IntimacyAttraction(relationship);

            var restrictedVal   = Math.Clamp(-0.16 + trust * 0.06 + comfort * 0.06 + (hasPrivacy ? 0.04 : -0.05), -0.20, 0.08);
            var unrestrictedVal = Math.Clamp(-0.03 + attraction * 0.12 + (hasPrivacy ? 0.03 : 0.0), -0.08, 0.14);

            return Lerp(restrictedVal, unrestrictedVal, soi.Attitude);
        }

        public static bool BlocksIntimateTouch(
            Sociosexuality soi,
            RelationshipEdge? relationship)
        {
            var closeness = relationship?.Closeness ?? 0.0;
            var comfort = relationship?.Comfort ?? 0.0;
            var intimacyInterest = relationship is null
                ? 0.0
                : (relationship.SexualInterest * 0.65) + (relationship.IntimateAffinity * 0.35);

            var minCloseness        = Lerp(70.0, 55.0, soi.Attitude);
            var minComfort          = Lerp(60.0,  0.0, soi.Attitude);
            var minIntimacyInterest = Lerp(65.0, 50.0, soi.Attitude);

            return closeness < minCloseness
                || (minComfort > 0.01 && comfort < minComfort)
                || intimacyInterest < minIntimacyInterest;
        }

        #endregion

        #region Relationship deltas

        public static double SexualInterestDeltaMultiplier(Sociosexuality soi)
            => 0.65 + ((soi.Behavior + soi.Desire) / 2.0) * 0.70;

        public static double RomanticInviteDeltaMultiplier(Sociosexuality soi)
            => soi.Behavior <= 0.5
                ? Lerp(1.25, 1.0, soi.Behavior * 2.0)
                : Lerp(1.0, 0.85, (soi.Behavior - 0.5) * 2.0);

        public static double ComfortInviteDelta(Sociosexuality soi)
            => soi.Behavior <= 0.5
                ? Lerp(0.4, 0.0, soi.Behavior * 2.0)
                : Lerp(0.0, -0.1, (soi.Behavior - 0.5) * 2.0);

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
                + relationship.IntimateAffinity * 0.15,
                0.0,
                100.0) / 100.0;
        }

        private static double Lerp(double a, double b, double t)
            => a + (b - a) * Math.Clamp(t, 0.0, 1.0);

        #endregion
    }
}
