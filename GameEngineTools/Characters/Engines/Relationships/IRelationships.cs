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
        double Attraction,
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
    public sealed record FirstImpressionFormed(
        WDateTime OccurredAt, HumanId A, HumanId B,
        double Like, double Attraction) : IDomainEvent;

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
