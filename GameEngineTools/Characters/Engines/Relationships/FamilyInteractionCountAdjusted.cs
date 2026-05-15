// FamilyInteractionCountAdjusted.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Domain event fired by <see cref="GameEngineTools.Characters.Generation.NuclearFamilyGenerator"/>
    /// to adjust <see cref="RelationshipEdge.PositiveInteractionCount"/> on a specific edge
    /// to reflect years of shared history for prebuilt families with older children.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A newborn's parent-child edge is seeded with a baseline interaction count by
    /// <see cref="GameEngineTools.Characters.Generation.FamilyBuilder"/>.
    /// For older children (teenagers, young adults), this count needs to be higher
    /// to correctly seed the MereExposure attraction bonus and semantic memory baseline.
    /// </para>
    /// <para>
    /// <b>Handler location:</b><br/>
    /// <see cref="DefaultRelationshipsEngine.Handle"/> must include a case for this event
    /// that adds <see cref="Bonus"/> to <see cref="RelationshipEdge.PositiveInteractionCount"/>
    /// on the edge toward <see cref="TargetId"/>, capped at a reasonable maximum (200).
    /// </para>
    /// </remarks>
    /// <param name="OccurredAt">World time at which the adjustment was applied.</param>
    /// <param name="OwnerId">The character whose edge is adjusted.</param>
    /// <param name="TargetId">The target character whose edge entry receives the bonus.</param>
    /// <param name="Bonus">Number of additional interactions to add.</param>
    public sealed record FamilyInteractionCountAdjusted(
        WDateTime OccurredAt,
        HumanId OwnerId,
        HumanId TargetId,
        int Bonus) : IDomainEvent;
}
