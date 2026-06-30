// StatusConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Status
{
    /// <summary>
    /// Tuning parameters for the social-hierarchy subsystem. Bound from <c>Characters:Status</c>.
    /// </summary>
    /// <remarks>
    /// All weights are order-of-magnitude calibrations, <b>not physical constants</b> — the underlying
    /// studies use WEIRD samples and report standardised betas, so these are deliberately tunable.
    /// <list type="bullet">
    ///   <item>Cheng et al. (2013) <i>JPSP</i> 104:103 — Dominance/Prestige orthogonal (r=−.03);
    ///         status betas Dominance γ=.70, Prestige γ=.57; R²=.77.</item>
    ///   <item>Knight &amp; Mehta / Cheng et al. (2017) <i>PNAS</i> 114 — status×stability moderates stress;
    ///         Gesquiere et al. (2011) <i>Science</i> 333:357 — the alpha pays a stress cost.</item>
    ///   <item>Marmot (1991) <i>Lancet</i> 337; Marmot &amp; Shipley (1996) <i>BMJ</i> 313:1177 — Whitehall
    ///         health gradient: low control is the key chronic-stress driver.</item>
    ///   <item>Henrich &amp; Gil-White (2001) — deference is freely conferred toward prestige.</item>
    /// </list>
    /// </remarks>
    public sealed record StatusConfig(
        // ── Consensus aggregation ──────────────────────────────────────────────
        /// <summary>Minimum edge Familiarity for an observer's perception to count toward consensus. Default 5.</summary>
        double MinObserverFamiliarity = 5.0,

        /// <summary>
        /// Weight floor so even a barely-acquainted observer contributes a little to consensus.
        /// Observer weight = <c>FamiliarityWeightFloor + Familiarity/100</c>. Default 0.25.
        /// </summary>
        double FamiliarityWeightFloor = 0.25,

        // ── Hierarchy stability ────────────────────────────────────────────────
        /// <summary>
        /// Mean per-character salience change (0..100) between folds that maps to a fully <i>unstable</i>
        /// hierarchy (stability → 0). Smaller churn scales linearly toward stability 1. Default 12.
        /// </summary>
        double StabilityChurnScale = 12.0,

        /// <summary>
        /// Exponential smoothing factor applied to the per-tick stability estimate so a single
        /// reshuffle does not flip the hierarchy from stable to unstable. Default 0.2.
        /// </summary>
        double StabilitySmoothing = 0.2,

        // ── Status × stability → stress (Psychology) ───────────────────────────
        /// <summary>
        /// Stress per hour a high-status character accrues when the hierarchy is unstable — the
        /// "cost of the top": defending rank under threat. Scales by (status above 50) × instability.
        /// Default 3.0. Source: Gesquiere 2011; Knight/Cheng 2017.
        /// </summary>
        double TopInstabilityStressPerHour = 3.0,

        /// <summary>
        /// Stress <i>relief</i> per hour a high-status character enjoys when the hierarchy is stable —
        /// secure rank buffers stress. Scales by (status above 50) × stability. Default 1.5.
        /// </summary>
        double HighStatusStableReliefPerHour = 1.5,

        /// <summary>
        /// Chronic stress per hour for a low-status character with low perceived control in a stable
        /// hierarchy (the Whitehall low-control gradient → allostatic burden). Scales by
        /// (status below 50) × (1 − control) × stability. Default 2.0. Source: Marmot 1991.
        /// </summary>
        double LowStatusLowControlStressPerHour = 2.0,

        // ── Deference (Behavior target selection) ──────────────────────────────
        /// <summary>
        /// Per-point bias added to a candidate's reach-out attractiveness for each point of the
        /// candidate's prestige above the actor's own prestige status (freely-conferred deference:
        /// people preferentially approach/affiliate up the prestige ladder). Default 0.015.
        /// Source: Henrich &amp; Gil-White 2001.
        /// </summary>
        double PrestigeDeferenceWeight = 0.015,

        /// <summary>
        /// Per-point bias <i>subtracted</i> from a candidate's reach-out attractiveness for each point
        /// of the candidate's dominance above the actor's own dominance status (avoidance of coercive
        /// superiors — dominance drives compliance/avoidance, not voluntary approach). Default 0.010.
        /// Source: Cheng et al. 2013.
        /// </summary>
        double DominanceAvoidanceWeight = 0.010,

        // ── Ascribed status (role / occupation / lineage prior) ────────────────
        /// <summary>
        /// How strongly an ascribed prior persists against the emergent consensus, in [0,1]: the blended
        /// status is <c>consensus·(1−p) + prior·p</c>. A <b>society parameter, not a fixed law</b> — modern
        /// mobility ≈ 0.4–0.47, traditional ≈ 0.75. Default 0.45. Source: Corak 2013; Clark 2014.
        /// </summary>
        double AscribedPersistence = 0.45,

        /// <summary>Dominance prior for an <see cref="AscribedRole.Official"/> (vedoucí). Default 55.</summary>
        double OfficialDominancePrior = 55.0,
        /// <summary>Prestige prior for an <see cref="AscribedRole.Official"/>. Default 62.</summary>
        double OfficialPrestigePrior = 62.0,
        /// <summary>Dominance prior for an <see cref="AscribedRole.Leader"/> (radní). Default 62.</summary>
        double LeaderDominancePrior = 62.0,
        /// <summary>Prestige prior for an <see cref="AscribedRole.Leader"/>. Default 75.</summary>
        double LeaderPrestigePrior = 75.0,
        /// <summary>Dominance prior for an <see cref="AscribedRole.Elder"/> (low coercion). Default 52.</summary>
        double ElderDominancePrior = 52.0,
        /// <summary>Prestige prior for an <see cref="AscribedRole.Elder"/> (high respect). Default 72.</summary>
        double ElderPrestigePrior = 72.0)
    {
        /// <summary>Parameterless constructor required by DI options binding — all fields use defaults.</summary>
        public StatusConfig() : this(MinObserverFamiliarity: 5.0)
        {
        }

        /// <summary>
        /// The ascribed <see cref="SocietalStatus"/> prior for a role, or <c>null</c> for
        /// <see cref="AscribedRole.Commoner"/> (no ascribed advantage).
        /// </summary>
        public SocietalStatus? PriorForRole(AscribedRole role) => role switch
        {
            AscribedRole.Official => new SocietalStatus(OfficialDominancePrior, OfficialPrestigePrior),
            AscribedRole.Leader => new SocietalStatus(LeaderDominancePrior, LeaderPrestigePrior),
            AscribedRole.Elder => new SocietalStatus(ElderDominancePrior, ElderPrestigePrior),
            _ => null
        };
    }
}
