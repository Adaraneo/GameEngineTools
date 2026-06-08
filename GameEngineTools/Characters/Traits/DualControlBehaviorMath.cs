// DualControlBehaviorMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using System;

    /// <summary>
    /// Behavioral weights derived from the Dual Control Model (Bancroft &amp; Janssen 2000).
    /// </summary>
    /// <remarks>
    /// The DCM models sexual response as a balance of the excitation system (SES) and two
    /// independent inhibition systems (SIS1: performance anxiety, SIS2: contextual risk).
    /// This class translates the <see cref="SexualResponsiveness"/> profile into bias/multiplier values
    /// compatible with the behavior engine (AddBias / Multiply pattern).
    /// </remarks>
    public static class DualControlBehaviorMath
    {
        /// <summary>
        /// SIS1 effect: suppression of InviteIntimacy under elevated stress.
        /// People with high SIS1 are strongly inhibited during HPA activation (performance anxiety).
        /// Returns a multiplier [0.3 .. 1.0] — 1.0 = no suppression.
        /// </summary>
        /// <param name="dcm">DCM profile; null = population average (SIS1 = 0.5).</param>
        /// <param name="stressNormalized">Current stress [0..1] (Stress / 100).</param>
        public static double StressSuppressionMultiplier(SexualResponsiveness? dcm, double stressNormalized)
        {
            var sis1 = dcm?.SIS1 ?? 0.5;
            // Threshold where suppression begins: low SIS1 (0) → stress > 0.8; high SIS1 (1) → stress > 0.3
            var threshold = 0.8 - sis1 * 0.5;
            if (stressNormalized <= threshold) return 1.0;
            var supression = (stressNormalized - threshold) / 0.4 * sis1;
            return Math.Clamp(1.0 - supression, 0.3, 1.0);
        }

        /// <summary>
        /// SIS2 effect: suppression of InviteIntimacy in a risky context (crowding, presence of observers).
        /// People with high SIS2 are strongly inhibited under perceived social risk.
        /// Returns a multiplier [0.2 .. 1.0].
        /// </summary>
        /// <param name="dcm">DCM profile; null = population average (SIS2 = 0.5).</param>
        /// <param name="vulnerabilitySafety">
        /// Degree of contextual safety [0..1] — 1.0 = full privacy with no observers.
        /// Corresponds to <c>SocialTargetScore.VulnerabilitySafety</c>.
        /// </param>
        public static double ContextSuppressionMultiplier(SexualResponsiveness? dcm, double vulnerabilitySafety)
        {
            var sis2 = dcm?.SIS2 ?? 0.5;
            // Safety < threshold → suppression; the threshold decreases with lower SIS2
            var threshold = 0.2 + sis2 * 0.6;  // 0,2 (nízký SIS2) .. 0,8 (vysoký SIS2)
            var contextRisk = 1.0 - vulnerabilitySafety;
            if (contextRisk <= 1.0 - threshold) return 1.0;
            var suppression = (contextRisk - (1.0 - threshold)) / 0.4 * sis2;
            return Math.Clamp(1.0 - suppression, 0.2, 1.0);
        }

        /// <summary>
        /// SES effect: biases InviteIntimacy based on excitatory sensitivity.
        /// Returns an additive bias [-2.0 .. +2.0] (symmetric around SES = 0.5).
        /// </summary>
        /// <param name="dcm">DCM profile; null = population average → bias 0.</param>
        public static double ExcitationBias(SexualResponsiveness? dcm)
            => ((dcm?.SES ?? 0.5) - 0.5) * 4.0;

        /// <summary>
        /// Combined SIS1 × SIS2 multiplier for InviteIntimacy utility.
        /// </summary>
        public static double CombinedSuppressionMultiplier(
            SexualResponsiveness? dcm,
            double stressNormalized,
            double vulnerabilitySafety)
            => StressSuppressionMultiplier(dcm, stressNormalized)
             * ContextSuppressionMultiplier(dcm, vulnerabilitySafety);
    }
}
