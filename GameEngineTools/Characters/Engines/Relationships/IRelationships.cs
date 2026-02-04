// IRelationships.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{

    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    public sealed record RelationshipsConfig(double DecayPerDay, double RepairGain, double RupturePenalty);

    public sealed record RelationshipEdge(
        HumanId A, HumanId B,
        double Like, double Trust, double Attraction, double Closeness, double Respect, double Comfort,
        DomainBreakdown Breakdown);

    public sealed record DomainBreakdown(
        double Intellect, double Humor, double Aesthetics, double Values, double Physical);

    public sealed record RelationshipState(
        IReadOnlyDictionary<HumanId, RelationshipEdge> Edges);

    public interface IRelationshipsEngine : IEngine<RelationshipState, RelationshipsConfig> { }

    // Události
    public sealed record FirstImpressionFormed(WDateTime OccurredAt, HumanId A, HumanId B, double Like, double Attraction) : IDomainEvent;
    public sealed record MicroPositive(WDateTime OccurredAt, HumanId A, HumanId B, string What) : IDomainEvent;
    public sealed record MicroNegative(WDateTime OccurredAt, HumanId A, HumanId B, string What) : IDomainEvent;
    public sealed record RepairAttempt(WDateTime OccurredAt, HumanId A, HumanId B, bool Accepted) : IDomainEvent;
}
