// AppraisalDimensions.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology.Appraisal
{
    /// <summary>
    /// Who or what is held accountable for an appraised event — the agency / accountability
    /// stimulus-evaluation check of Scherer's Component Process Model (CPM).
    /// </summary>
    /// <remarks>
    /// Agency is the single most important discriminator between negatively-valenced emotions:
    /// other-accountability drives anger, self-accountability drives guilt/pride, and
    /// circumstantial accountability drives sadness/fear (Roseman 1996; Scherer 2001).
    /// </remarks>
    public enum AppraisalAgency
    {
        /// <summary>No clear agent (the event is ambient or its cause is unknown).</summary>
        None,

        /// <summary>The character themselves caused the event (self-accountability).</summary>
        Self,

        /// <summary>Another agent caused the event (other-accountability).</summary>
        Other,

        /// <summary>Impersonal circumstances caused the event (circumstantial accountability).</summary>
        Circumstance
    }

    /// <summary>
    /// Result of running Scherer's Component Process Model stimulus-evaluation checks (SECs)
    /// over an incoming <see cref="GameEngineTools.Characters.Core.IDomainEvent"/>. This is the
    /// <i>mechanism</i> that drives the PAD state and selects a discrete emotion, replacing the
    /// former direct PAD→emotion inference table as the emotion <i>generator</i>.
    /// </summary>
    /// <remarks>
    /// Each continuous check is normalised to a documented range. The downstream
    /// <see cref="AppraisalEmotionMap"/> converts these checks into a labelled emotion plus a
    /// coherent PAD delta. Source: Scherer (2001) CPM; weights from Yeo &amp; Ong (2024,
    /// <i>Psychological Bulletin</i> 150(12)).
    /// </remarks>
    /// <param name="Relevance">
    /// How relevant the event is to the character's needs/goals, [0..1]. 0 = ignore entirely.
    /// </param>
    /// <param name="Novelty">Suddenness / unexpectedness of the event, [0..1].</param>
    /// <param name="IntrinsicPleasantness">
    /// Intrinsic pleasantness of the stimulus independent of goals, [−1..+1].
    /// </param>
    /// <param name="GoalConduciveness">
    /// Whether the event helps (+) or obstructs (−) the character's active goals, [−1..+1].
    /// </param>
    /// <param name="Agency">Who/what is held accountable for the event.</param>
    /// <param name="Certainty">Confidence about what the event means / will lead to, [0..1].</param>
    /// <param name="CopingPotential">
    /// Perceived ability to deal with the event, [0..1]. Low coping + threat → fear; high
    /// coping + obstruction → anger (Scherer 2001; Lazarus 1991).
    /// </param>
    /// <param name="NormCompatibility">
    /// Compatibility of the event/action with internal and social norms, [−1..+1].
    /// Self-caused norm violations drive guilt/shame.
    /// </param>
    public sealed record AppraisalOutcome(
        double Relevance,
        double Novelty,
        double IntrinsicPleasantness,
        double GoalConduciveness,
        AppraisalAgency Agency,
        double Certainty,
        double CopingPotential,
        double NormCompatibility)
    {
        /// <summary>
        /// A neutral, fully-irrelevant appraisal — used as the "no appraisal applies" sentinel.
        /// </summary>
        public static AppraisalOutcome Irrelevant { get; } = new(
            Relevance: 0.0,
            Novelty: 0.0,
            IntrinsicPleasantness: 0.0,
            GoalConduciveness: 0.0,
            Agency: AppraisalAgency.None,
            Certainty: 1.0,
            CopingPotential: 0.5,
            NormCompatibility: 0.0);

        /// <summary>True when the event is relevant enough to generate an emotional response.</summary>
        /// <param name="threshold">Minimum relevance required (default 0.05).</param>
        public bool IsRelevant(double threshold = 0.05) => Relevance > threshold;
    }
}
