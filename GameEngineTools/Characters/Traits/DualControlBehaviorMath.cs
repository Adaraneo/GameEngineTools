// DualControlBehaviorMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using System;

    /// <summary>
    /// Behavioral weights derived from the Dual Control Model (Bancroft &amp; Janssen 2000).
    /// </summary>
    /// <remarks>
    /// DCM modeluje sexuální odezvu jako rovnováhu excitačního systému (SES) a dvou
    /// nezávislých inhibičních systémů (SIS1: výkonová úzkost, SIS2: kontextové riziko).
    /// Tato třída překládá profil <see cref="SexualResponsiveness"/> do bias/multiplier hodnot
    /// kompatibilních s behavior enginem (AddBias / Multiply pattern).
    /// </remarks>
    public static class DualControlBehaviorMath
    {
        /// <summary>
        /// SIS1 efekt: suprese InviteIntimacy při zvýšeném stresu.
        /// Osoby s vysokým SIS1 jsou silně inhibovány při HPA aktivaci (výkonová úzkost).
        /// Vrací multiplikátor [0,3 .. 1,0] — 1,0 = žádná suprese.
        /// </summary>
        /// <param name="dcm">DCM profil; null = populační průměr (SIS1 = 0,5).</param>
        /// <param name="stressNormalized">Aktuální stres [0..1] (Stress / 100).</param>
        public static double StressSuppressionMultiplier(SexualResponsiveness? dcm, double stressNormalized)
        {
            var sis1 = dcm?.SIS1 ?? 0.5;
            // Práh kde začíná suprese: nízký SIS1 (0) → stress > 0,8; vysoký SIS1 (1) → stress > 0,3
            var threshold = 0.8 - sis1 * 0.5;
            if (stressNormalized <= threshold) return 1.0;
            var supression = (stressNormalized - threshold) / 0.4 * sis1;
            return Math.Clamp(1.0 - supression, 0.3, 1.0);
        }

        /// <summary>
        /// SIS2 efekt: suprese InviteIntimacy v rizikovém kontextu (crowding, přítomnost pozorovatelů).
        /// Osoby s vysokým SIS2 jsou silně inhibovány při percipovaném sociálním riziku.
        /// Vrací multiplikátor [0,2 .. 1,0].
        /// </summary>
        /// <param name="dcm">DCM profil; null = populační průměr (SIS2 = 0,5).</param>
        /// <param name="vulnerabilitySafety">
        /// Míra kontextové bezpečnosti [0..1] — 1,0 = plné soukromí bez pozorovatelů.
        /// Odpovídá <c>SocialTargetScore.VulnerabilitySafety</c>.
        /// </param>
        public static double ContextSuppressionMultiplier(SexualResponsiveness? dcm, double vulnerabilitySafety)
        {
            var sis2 = dcm?.SIS2 ?? 0.5;
            // Bezpečnost < threshold → suprese; threshold klesá s nižším SIS2
            var threshold = 0.2 + sis2 * 0.6;  // 0,2 (nízký SIS2) .. 0,8 (vysoký SIS2)
            var contextRisk = 1.0 - vulnerabilitySafety;
            if (contextRisk <= 1.0 - threshold) return 1.0;
            var suppression = (contextRisk - (1.0 - threshold)) / 0.4 * sis2;
            return Math.Clamp(1.0 - suppression, 0.2, 1.0);
        }

        /// <summary>
        /// SES efekt: bias InviteIntimacy na základě excitační citlivosti.
        /// Vrací aditivní bias [-2,0 .. +2,0] (symetrický kolem SES = 0,5).
        /// </summary>
        /// <param name="dcm">DCM profil; null = populační průměr → bias 0.</param>
        public static double ExcitationBias(SexualResponsiveness? dcm)
            => ((dcm?.SES ?? 0.5) - 0.5) * 4.0;

        /// <summary>
        /// Kombinovaný multiplikátor SIS1 × SIS2 pro InviteIntimacy utility.
        /// </summary>
        public static double CombinedSuppressionMultiplier(
            SexualResponsiveness? dcm,
            double stressNormalized,
            double vulnerabilitySafety)
            => StressSuppressionMultiplier(dcm, stressNormalized)
             * ContextSuppressionMultiplier(dcm, vulnerabilitySafety);
    }
}
