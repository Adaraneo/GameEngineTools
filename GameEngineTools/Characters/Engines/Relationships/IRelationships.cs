// IRelationships.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Configuration for <see cref="IRelationshipsEngine"/>.
    /// </summary>
    public sealed record RelationshipsConfig(
        double DecayPerDay           = 1.5,
        double RepairGain            = 6.0,
        double RupturePenalty        = 8.0,
        double MereExposureMaxBoost  = 15.0,
        int    MereExposureSaturation = 20)
    {
        /// <summary>Parameterless constructor required by DI options binding.</summary>
        public RelationshipsConfig() : this(1.5, 6.0, 8.0, 15.0, 20) { }
    }

    /// <summary>
    /// A directed edge in the relationship graph — how character A perceives character B.
    /// </summary>
    /// <remarks>
    /// The graph is asymmetric: A may like B more than B likes A.
    /// All numeric dimensions are in [0, 100].
    /// </remarks>
    public sealed record RelationshipEdge(
        HumanId A,
        HumanId B,
        double Like,
        double Trust,
        /// <summary>
        /// Legacy aggregate attraction score derived from the explicit relationship signals.
        /// Kept temporarily for backward compatibility with older callers.
        /// </summary>
        double Attraction,
        /// <summary>
        /// Accumulated familiarity from repeated exposure and accepted contact.
        /// Higher values mean A feels more acquainted with B.
        /// </summary>
        double Familiarity,
        /// <summary>
        /// Perceived aesthetic appeal driven mainly by taste and preference matching.
        /// </summary>
        double AestheticAttraction,
        /// <summary>
        /// Perceived physical appeal driven mainly by baseline appearance cues.
        /// </summary>
        double PhysicalAttraction,
        /// <summary>
        /// Romantic inclination toward B.
        /// More context-dependent than raw physical attraction.
        /// </summary>
        double RomanticInterest,
        /// <summary>
        /// Sexual inclination toward B.
        /// Strongly shaped by physical attraction plus comfort and intimacy context.
        /// </summary>
        double SexualInterest,
        double Closeness,
        double Respect,
        double Comfort,
        DomainBreakdown Breakdown,

        /// <summary>
        /// Running count of positive (accepted) interactions between A and B.
        /// Used to compute the mere-exposure attraction bonus in
        /// <see cref="DefaultRelationshipsEngine"/>.
        /// </summary>
        int PositiveInteractionCount = 0);

    /// <summary>
    /// Per-domain breakdown of <em>why</em> A values B.
    /// All values in [0, 100].
    /// </summary>
    public sealed record DomainBreakdown(
        double Intellect,
        double Humor,
        double Aesthetics,
        double Values,
        double Physical);

    /// <summary>Snapshot of the full relationship graph for one character.</summary>
    public sealed record RelationshipState(
        IReadOnlyDictionary<HumanId, RelationshipEdge> Edges);

    /// <summary>Engine that manages the directed relationship graph for a single character.</summary>
    public interface IRelationshipsEngine : IEngine<RelationshipState, RelationshipsConfig>
    { }

    // ── Domain events ────────────────────────────────────────────────────────────

    /// <summary>Fired when character A meets B for the first time and forms an initial impression.</summary>
    /// <param name="Like">Initial like score derived from the halo effect.</param>
    /// <param name="Attraction">Overall attraction score in [0, 100].</param>
    /// <param name="BasePhysical">
    /// Evolutionary baseline component from <c>DefaultAttractionCalculator</c> — WHR, height, symmetry.
    /// Range: [0, 40]. Used to seed the <c>Physical</c> domain in <see cref="DomainBreakdown"/>.
    /// </param>
    /// <param name="PreferenceMatch">
    /// Personal preference match component from <c>DefaultAttractionCalculator</c>.
    /// Range: [0, 35]. Used to seed the <c>Aesthetics</c> domain in <see cref="DomainBreakdown"/>.
    /// </param>
    public sealed record FirstImpressionFormed(
        WDateTime OccurredAt,
        HumanId A,
        HumanId B,
        double Like,
        double Attraction,
        double BasePhysical    = 0.0,
        double PreferenceMatch = 0.0) : IDomainEvent;

    /// <summary>A brief positive gesture — smile, compliment, small act of help.</summary>
    public sealed record MicroPositive(
        WDateTime OccurredAt, HumanId A, HumanId B, string What) : IDomainEvent;

    /// <summary>A brief negative gesture — criticism, being ignored, cold response.</summary>
    public sealed record MicroNegative(
        WDateTime OccurredAt, HumanId A, HumanId B, string What) : IDomainEvent;

    /// <summary>An attempt to repair a damaged relationship, accepted or rejected.</summary>
    public sealed record RepairAttempt(
        WDateTime OccurredAt, HumanId A, HumanId B, bool Accepted) : IDomainEvent;
}
