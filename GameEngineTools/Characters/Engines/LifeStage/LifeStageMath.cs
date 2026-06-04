// LifeStageMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.LifeStage
{
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Pure, stateless math for life-stage transition effects.
    /// </summary>
    /// <remarks>
    /// Strong skepticism baked in (research plan §5): no age-locked crisis; episodes are
    /// event-triggered with modest lifetime probability; the mid-life mood dip is tiny
    /// (~0.05–0.1 SD); empty nest is, by default, a small <b>positive</b>.
    /// </remarks>
    public static class LifeStageMath
    {
        /// <summary>
        /// Probability that a given transition triggers a life-evaluation episode.
        /// Broad transitions (entering mid-life) ≈ 0.15; others ≈ 0.08 (strict base rate).
        /// </summary>
        public static double EvaluationEpisodeProbability(StadiumType from, StadiumType to)
            => (from, to) switch
            {
                (StadiumType.Adult, StadiumType.MidAged) => 0.15,
                (StadiumType.Teenager, StadiumType.Adult) => 0.12,
                (StadiumType.MidAged, StadiumType.Old) => 0.10,
                _ => 0.08
            };

        /// <summary>
        /// Small MoodBaseline dip applied when entering mid-life (U-curve trough), in MoodBaseline
        /// points [0..100]. ~0.05–0.1 SD ≈ 1–2 points. Other transitions: none.
        /// </summary>
        public static double MidlifeMoodDip(StadiumType from, StadiumType to)
            => (from, to) == (StadiumType.Adult, StadiumType.MidAged) ? 1.5 : 0.0;

        /// <summary>
        /// Fraction of empty-nesters with a strong parenting identity who experience a transient
        /// negative instead of the default small positive (~15–20%).
        /// </summary>
        public const double ParentingIdentityNegativeFraction = 0.18;

        /// <summary>Default empty-nest valence shift (small positive; Bouchard 2014, d ≈ +0.2–0.3).</summary>
        public const double EmptyNestPositiveValence = 0.12;

        /// <summary>Transient negative empty-nest valence shift for strong parenting identities.</summary>
        public const double EmptyNestNegativeValence = -0.18;

        /// <summary>
        /// Returns the empty-nest valence shift for a character given whether they hold a strong
        /// parenting identity (proxy: high NeedCare or an active ProtectFamily goal).
        /// </summary>
        public static double EmptyNestValenceShift(bool strongParentingIdentity)
            => strongParentingIdentity ? EmptyNestNegativeValence : EmptyNestPositiveValence;
    }
}
