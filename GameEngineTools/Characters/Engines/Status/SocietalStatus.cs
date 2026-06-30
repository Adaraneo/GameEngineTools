// SocietalStatus.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Status
{
    /// <summary>
    /// A character's emergent social standing along two orthogonal routes to rank.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Status is modelled as <b>two independent axes</b>, not one scale: <see cref="DominanceStatus"/>
    /// (rank gained by inducing fear / coercion) and <see cref="PrestigeStatus"/> (rank freely conferred
    /// by others through admiration). The two are empirically near-orthogonal — Cheng et al. (2013,
    /// <i>JPSP</i> 104(1):103–125) report r ≈ −.03 between them — so they must never be collapsed into a
    /// single "prestige" number.
    /// </para>
    /// <para>
    /// A <see cref="SocietalStatus"/> is <b>not a self-attribute</b>. It is an aggregation computed by
    /// <see cref="StatusLedger"/> from how everyone else perceives the character
    /// (<see cref="GameEngineTools.Characters.Engines.Relationships.RelationshipEdge.PerceivedDominance"/>
    /// and <see cref="GameEngineTools.Characters.Engines.Relationships.RelationshipEdge.PerceivedPrestige"/>).
    /// Anderson, Hildreth &amp; Howland (2015, <i>Psych. Bull.</i> 141:574): status is conferred by others.
    /// </para>
    /// </remarks>
    /// <param name="DominanceStatus">Fear/coercion-based standing, 0..100 (neutral 50).</param>
    /// <param name="PrestigeStatus">Freely-conferred admiration standing, 0..100 (neutral 50).</param>
    public readonly record struct SocietalStatus(double DominanceStatus, double PrestigeStatus)
    {
        /// <summary>The neutral standing held by an unknown / unranked character (both axes 50).</summary>
        public static SocietalStatus Neutral { get; } = new(50.0, 50.0);

        /// <summary>
        /// A single salience scalar blending the two axes by their relative status betas
        /// (Cheng et al. 2013: Dominance γ ≈ .70, Prestige γ ≈ .57). Used only where a one-dimensional
        /// ordering is unavoidable (e.g. hierarchy-stability ranking); the two axes stay separate everywhere else.
        /// </summary>
        public double Salience => (DominanceStatus * 0.70 + PrestigeStatus * 0.57) / (0.70 + 0.57);
    }
}
