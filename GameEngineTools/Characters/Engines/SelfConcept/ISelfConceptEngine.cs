// ISelfConceptEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SelfConcept
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;

    #region SelfConcept

    /// <summary>
    /// A character's mutable self-view: how they perceive their own Big Five traits, who they
    /// ideally want to be (a subset), their global self-esteem, and the discrepancy between
    /// perceived and ideal self.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="Personality"/> (the <i>actual</i> traits). The perceived self drifts
    /// from social feedback via self-verification (Swann 1983): feedback that <b>confirms</b> the
    /// current self-view is accepted readily; <b>disconfirming</b> feedback is heavily discounted.
    /// The actual→perceived link is loose (r ≈ .20), perceived→metaperception tighter (r ≈ .50).
    /// </para>
    /// <para>
    /// <see cref="SelfDiscrepancy"/> (Higgins 1987, used only as a general discrepancy→distress
    /// signal — the specific ideal→dejection / ought→agitation mapping is <b>not</b> modelled,
    /// per Mason 2019 non-replication) drives identity-work: when it crosses a threshold the engine
    /// seeds a <see cref="Goals.PersistentGoalKind.BuildIdentity"/> goal.
    /// </para>
    /// <para>
    /// <see cref="IdealExtraversion"/>/<see cref="IdealAgreeableness"/>/<see cref="IdealConscientiousness"/>
    /// are <b>mutable but statically initialised</b> here — the life-transition hook that shifts them
    /// (R6) is wired in a later phase, which breaks the R3↔R6 dependency cycle.
    /// </para>
    /// </remarks>
    public sealed record SelfConcept(
        double PerceivedOpenness,
        double PerceivedConscientiousness,
        double PerceivedExtraversion,
        double PerceivedAgreeableness,
        double PerceivedNeuroticism,

        double IdealExtraversion,
        double IdealAgreeableness,
        double IdealConscientiousness,

        /// <summary>Global self-esteem [0..1]. Highly stable (per-year rank-order r ≈ .85–.95).</summary>
        double SelfEsteem,

        /// <summary>Mean absolute gap between ideal and perceived self over the ideal subset [0..1].</summary>
        double SelfDiscrepancy)
    {
        /// <summary>Neutral self-concept (all perceptions at midpoint). Used before seeding.</summary>
        public static SelfConcept Neutral { get; } =
            new(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.0);
    }

    #endregion SelfConcept

    #region MetaperceptionUpdated

    /// <summary>
    /// Diagnostic event emitted when the perceived self shifts meaningfully from feedback.
    /// </summary>
    public sealed record MetaperceptionUpdated(
        WDateTime OccurredAt,
        HumanId Human,
        double PerceivedExtraversion,
        double PerceivedAgreeableness,
        double SelfEsteem,
        double SelfDiscrepancy) : IDomainEvent;

    #endregion MetaperceptionUpdated

    #region ISelfConceptEngine

    /// <summary>Owns and evolves a character's <see cref="SelfConcept"/>.</summary>
    public interface ISelfConceptEngine : IEngine<SelfConcept, SelfConceptConfig>
    {
        /// <summary>
        /// Seeds the perceived and ideal self from actual personality. Call once after construction.
        /// </summary>
        void SeedFromPersonality(Personality personality);
    }

    #endregion ISelfConceptEngine
}
