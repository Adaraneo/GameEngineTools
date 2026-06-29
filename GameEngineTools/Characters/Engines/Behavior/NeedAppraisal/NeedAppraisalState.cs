// NeedAppraisalState.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.NeedAppraisal
{
    /// <summary>
    /// Derived experiential appraisal of SDT's three basic psychological needs, computed each tick from
    /// existing goal/relationship/regulatory-focus signals — NOT a new raw drive. Each need carries an
    /// independent Satisfaction and Frustration channel (asymmetric: high Frustration implies low
    /// Satisfaction, but low Satisfaction does NOT imply high Frustration — Bartholomew, Ntoumanis, Ryan
    /// &amp; Thøgersen-Ntoumani 2011).
    /// </summary>
    /// <remarks>
    /// Sources: Deci &amp; Ryan (2000); Vansteenkiste, Ryan &amp; Soenens (2020, <i>Motivation and
    /// Emotion</i> 44:1–31); Chen et al. (2015, <i>Motivation and Emotion</i> 39(2):216–236);
    /// Bartholomew et al. (2011, <i>JSEP</i> 33:75–102; <i>PSPB</i> 37:1459–1473).
    /// </remarks>
    public sealed record NeedAppraisalState(
        NeedChannel Competence,
        NeedChannel Relatedness,
        NeedChannel Autonomy)
    {
        /// <summary>Default/empty state for newly created characters — all channels neutral.</summary>
        public static NeedAppraisalState Empty { get; } = new(
            NeedChannel.Neutral, NeedChannel.Neutral, NeedChannel.Neutral);

        /// <summary>
        /// Global need-fulfillment factor — per bifactor-ESEM evidence (Tóth-Király et al. 2018; Garn,
        /// Morin &amp; Lonsdale 2019), a strong shared factor coexists with the three specific factors.
        /// Computed as the satisfaction-minus-frustration balance averaged across all three channels.
        /// </summary>
        public double GlobalBalance =>
            (Competence.Balance + Relatedness.Balance + Autonomy.Balance) / 3.0;
    }

    /// <summary>
    /// Satisfaction/Frustration pair for one basic need, with the empirically-required asymmetric
    /// coupling (frustration entails low satisfaction; the reverse does not hold).
    /// </summary>
    /// <param name="Satisfaction">Felt fulfillment [0..1]. Feeds well-being/vitality outputs.</param>
    /// <param name="Frustration">Felt thwarting [0..1]. Feeds ill-being/defensiveness outputs; weighted
    /// more heavily than Satisfaction for negative outcomes (asymmetry analogous to
    /// LossAversionModifier's λ).</param>
    public sealed record NeedChannel(double Satisfaction, double Frustration)
    {
        /// <summary>Neutral channel: moderate satisfaction, no frustration.</summary>
        public static NeedChannel Neutral { get; } = new(0.5, 0.0);

        /// <summary>Net balance = Satisfaction − FrustrationWeight × Frustration.</summary>
        public double Balance => Satisfaction - FrustrationWeight * Frustration;

        // Source: Bartholomew et al. (2011) — frustration predicts ill-being more strongly than
        // satisfaction predicts well-being; structurally analogous to BehaviorConfig.LossAversionLambda
        // (1.96), but scoped to need appraisal, not utility.
        private const double FrustrationWeight = 1.5;
    }
}
