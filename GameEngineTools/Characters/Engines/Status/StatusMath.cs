// StatusMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Status
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Pure, stateless math for the social-hierarchy subsystem: consensus aggregation,
    /// hierarchy-stability churn, and the status×stability×control stress term.
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="StatusLedger"/> (which owns the mutable per-scene state) so the
    /// numerical model is unit-testable in isolation, mirroring <c>SocialComparisonMath</c>.
    /// </remarks>
    internal static class StatusMath
    {
        /// <summary>
        /// Weighted consensus of how observers perceive a target along each status axis.
        /// Returns <see cref="SocietalStatus.Neutral"/> when no observer qualifies.
        /// </summary>
        /// <param name="observations">(dominance, prestige, weight) triples, one per qualifying observer.</param>
        internal static SocietalStatus Consensus(IReadOnlyList<(double Dominance, double Prestige, double Weight)> observations)
        {
            var totalWeight = 0.0;
            var domSum = 0.0;
            var presSum = 0.0;

            foreach (var (dom, pres, w) in observations)
            {
                if (w <= 0.0)
                    continue;

                totalWeight += w;
                domSum += dom * w;
                presSum += pres * w;
            }

            if (totalWeight <= 0.0)
                return SocietalStatus.Neutral;

            return new SocietalStatus(
                Math.Clamp(domSum / totalWeight, 0.0, 100.0),
                Math.Clamp(presSum / totalWeight, 0.0, 100.0));
        }

        /// <summary>
        /// Maps the mean per-character salience change between two folds into a hierarchy-stability
        /// estimate in [0,1] (1 = perfectly stable). Linear in churn up to <paramref name="churnScale"/>.
        /// </summary>
        internal static double StabilityFromChurn(double meanAbsSalienceChange, double churnScale)
        {
            if (churnScale <= 0.0)
                return 1.0;

            return Math.Clamp(1.0 - meanAbsSalienceChange / churnScale, 0.0, 1.0);
        }

        /// <summary>
        /// The net status-driven stress rate (points per hour; negative = relief) for a character given
        /// its own status, the local hierarchy stability, and its perceived control.
        /// </summary>
        /// <remarks>
        /// Three additive terms, each grounded in the stress literature:
        /// <list type="bullet">
        ///   <item><b>Cost of the top</b> — high status under instability raises stress (Gesquiere 2011).</item>
        ///   <item><b>Secure-rank buffer</b> — high status under stability lowers stress.</item>
        ///   <item><b>Low-control gradient</b> — low status + low control + stability → chronic burden
        ///         (Marmot/Whitehall).</item>
        /// </list>
        /// </remarks>
        /// <param name="status">The character's own emergent status.</param>
        /// <param name="stability">Local hierarchy stability [0,1].</param>
        /// <param name="perceivedControl">Perceived control [0,1] (e.g. PAD Dominance).</param>
        /// <param name="cfg">Tuning parameters.</param>
        internal static double StatusStressPerHour(
            SocietalStatus status, double stability, double perceivedControl, StatusConfig cfg)
        {
            stability = Math.Clamp(stability, 0.0, 1.0);
            perceivedControl = Math.Clamp(perceivedControl, 0.0, 1.0);
            var instability = 1.0 - stability;

            // Normalised standing on [-1,1]: +1 = top, -1 = bottom.
            var z = (status.Salience - 50.0) / 50.0;
            var above = Math.Max(0.0, z);   // how far above neutral
            var below = Math.Max(0.0, -z);  // how far below neutral

            var topCost = cfg.TopInstabilityStressPerHour * above * instability;
            var secureBuffer = cfg.HighStatusStableReliefPerHour * above * stability;     // subtracted below
            var lowControl = cfg.LowStatusLowControlStressPerHour * below * (1.0 - perceivedControl) * stability;

            return topCost + lowControl - secureBuffer;
        }

        /// <summary>
        /// Deference bias added to a reach-out candidate's attractiveness given the actor's own status
        /// and the candidate's status: approach is drawn <i>up</i> the prestige ladder (admiration) but
        /// pushed <i>away</i> from coercive dominance (avoidance). Positive = more likely to approach.
        /// </summary>
        internal static double DeferenceBias(SocietalStatus self, SocietalStatus candidate, StatusConfig cfg)
        {
            var prestigeGap = candidate.PrestigeStatus - self.PrestigeStatus;
            var dominanceGap = candidate.DominanceStatus - self.DominanceStatus;

            return cfg.PrestigeDeferenceWeight * prestigeGap
                 - cfg.DominanceAvoidanceWeight * Math.Max(0.0, dominanceGap);
        }
    }
}
