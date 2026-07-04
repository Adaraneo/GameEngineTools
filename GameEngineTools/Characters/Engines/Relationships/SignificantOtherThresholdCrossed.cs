// SignificantOtherThresholdCrossed.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Emitted when a relationship's Commitment first crosses the significant-other threshold
    /// (<see cref="RelationshipsConfig.SignificantOtherCommitmentThreshold"/>), making it eligible
    /// for a <see cref="SemanticMemory.SignificantOtherImprint"/> capture.
    /// </summary>
    /// <remarks>
    /// Reuses the existing Investment Model Commitment integrator (Topic A) as the significance
    /// measure — deliberately does not introduce a second, parallel "how important is this person"
    /// metric. Guarded by a one-shot latch (<see cref="RelationshipEdge.SignificantOtherImprinted"/>),
    /// analogous to <see cref="RelationshipEdge.DissolutionConsidered"/>'s pattern, to avoid
    /// re-capturing on every tick above threshold — unlike that latch, this one does NOT re-arm.
    /// <para>
    /// Emitted self-scoped (no appearance data attached) because <see cref="DefaultRelationshipsEngine"/>
    /// only ever sees its own character's <see cref="IHumanContext"/> — it cannot resolve <c>Other</c>'s
    /// <c>PhysicalAppearance</c>/<c>Personality</c> directly. Resolution happens at
    /// <c>DefaultSceneOrchestrator.RouteSignificantOtherImprints</c>, the one place in the codebase
    /// that holds multiple full <c>IHuman</c> instances simultaneously (the same pattern
    /// <c>FireFirstImpressions</c> already uses for <c>IAttractionCalculator</c>).
    /// </para>
    /// </remarks>
    public sealed record SignificantOtherThresholdCrossed(
        WDateTime OccurredAt,
        HumanId Self,
        HumanId Other,
        double Commitment) : IDomainEvent;
}
