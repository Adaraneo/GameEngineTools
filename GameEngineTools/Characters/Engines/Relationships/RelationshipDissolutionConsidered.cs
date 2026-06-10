// RelationshipDissolutionConsidered.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Emitted once when felt <see cref="RelationshipEdge.Commitment"/> toward a partner
    /// falls below <see cref="RelationshipsConfig.DissolutionCommitmentThreshold"/> —
    /// the Rusbult Investment Model "leaving" decision point.
    /// </summary>
    /// <remarks>
    /// Rusbult's model treats commitment as the proximal predictor of stay/leave behaviour:
    /// when satisfaction and investments can no longer offset the pull of available
    /// alternatives, commitment collapses and the bond becomes a candidate for dissolution.
    /// <para>
    /// This event marks the <em>consideration</em> of dissolution, not the dissolution itself.
    /// It fires a single time on the downward threshold crossing (guarded by
    /// <see cref="RelationshipEdge.DissolutionConsidered"/>) and re-arms once commitment
    /// recovers above the threshold. No engine consumes it yet — downstream reactions
    /// (psychology distress, behavioural leave-pressure) are intentionally deferred.
    /// </para>
    /// </remarks>
    public sealed record RelationshipDissolutionConsidered(
        WDateTime OccurredAt,

        /// <summary>The character whose commitment collapsed.</summary>
        HumanId Self,

        /// <summary>The partner the commitment was directed toward.</summary>
        HumanId Partner,

        /// <summary>The (post-tick) commitment value that crossed below the threshold.</summary>
        double Commitment) : IDomainEvent;
}
