// ReputationMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Reputation
{
    using System;
    using GameEngineTools.Characters.Engines.Relationships;

    /// <summary>
    /// Pure, stateless math for community reputation: recency-weighted image scoring,
    /// reputation spread, newcomer trust priors, and cooperation stability.
    /// </summary>
    /// <remarks>
    /// Indirect reciprocity (Nowak &amp; Sigmund 2005): cooperation is evolutionarily stable only when
    /// the probability that a recipient's reputation is known (<c>q</c> = <see cref="CommunityReputation.Spread"/>)
    /// exceeds the cost/benefit ratio <c>c/b</c>. Reputation uses <b>stern judging</b> (negativity bias),
    /// not naive image scoring, which is unstable against defector invasion.
    /// </remarks>
    public static class ReputationMath
    {
        /// <summary>Half-life of an observation's influence, expressed in interactions (recency). Default 7.</summary>
        public const double DefaultHalfLifeInteractions = 7.0;

        /// <summary>Baseline trust prior for a subject with no known reputation. Default 0.4.</summary>
        public const double DefaultTrustPrior = 0.4;

        /// <summary>Trust prior at maximally positive, fully-spread reputation. Default 0.7.</summary>
        public const double PositiveTrustPrior = 0.7;

        /// <summary>Trust prior at maximally negative, fully-spread reputation. Default 0.15.</summary>
        public const double NegativeTrustPrior = 0.15;

        /// <summary>Stern-judging negativity bias: bad acts move reputation 1.5× as hard. Default 1.5.</summary>
        public const double NegativityBias = 1.5;

        /// <summary>How fast reputation spreads through the community per observation. Default 0.15.</summary>
        public const double SpreadGrowthRate = 0.15;

        /// <summary>Per-observation EMA retention factor for a given half-life (in interactions).</summary>
        public static double DecayPerObservation(double halfLifeInteractions)
            => Math.Pow(0.5, 1.0 / Math.Max(1e-6, halfLifeInteractions));

        /// <summary>Recency weight of an observation that occurred <paramref name="stepsAgo"/> interactions ago.</summary>
        public static double RecencyWeight(double stepsAgo, double halfLifeInteractions)
            => Math.Pow(0.5, stepsAgo / Math.Max(1e-6, halfLifeInteractions));

        /// <summary>
        /// Updates the image score from one observation using a recency-weighted EMA with a
        /// stern-judging negativity bias. Intimate observations are reputation-neutral.
        /// </summary>
        public static double UpdateScore(double oldScore, ThirdPartyObservationType type, double halfLifeInteractions)
        {
            if (type == ThirdPartyObservationType.IntimateAct)
                return oldScore;

            var alpha = 1.0 - DecayPerObservation(halfLifeInteractions);

            double target;
            switch (type)
            {
                case ThirdPartyObservationType.PositiveAct:
                    target = +1.0;
                    break;
                case ThirdPartyObservationType.NegativeAct:
                case ThirdPartyObservationType.Betrayal:
                    target = -1.0;
                    alpha = Math.Min(1.0, alpha * NegativityBias); // stern judging
                    break;
                default:
                    return oldScore;
            }

            return Math.Clamp(oldScore + alpha * (target - oldScore), -1.0, 1.0);
        }

        /// <summary>Grows reputation spread toward full saturation as observations accumulate.</summary>
        public static double UpdateSpread(double oldSpread)
            => Math.Clamp(oldSpread + (1.0 - oldSpread) * SpreadGrowthRate, 0.0, 1.0);

        /// <summary>
        /// Maps a (score, spread) reputation to a newcomer's initial trust prior in
        /// [<see cref="NegativeTrustPrior"/>..<see cref="PositiveTrustPrior"/>], centred on
        /// <see cref="DefaultTrustPrior"/>. Effect scales with how widely the reputation is held.
        /// </summary>
        public static double InitialTrustPrior(double score, double spread)
        {
            var effective = Math.Clamp(score * spread, -1.0, 1.0);
            return effective >= 0
                ? DefaultTrustPrior + (PositiveTrustPrior - DefaultTrustPrior) * effective
                : DefaultTrustPrior + (DefaultTrustPrior - NegativeTrustPrior) * effective;
        }

        /// <summary>
        /// Nowak–Sigmund stability: cooperation is sustained only when reputation spread
        /// <paramref name="spread"/> (q) exceeds the cost/benefit ratio <paramref name="costBenefitRatio"/> (c/b).
        /// </summary>
        public static bool CooperationStable(double spread, double costBenefitRatio)
            => spread > costBenefitRatio;
    }
}
