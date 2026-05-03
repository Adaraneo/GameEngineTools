// ContemptuousActPerformed.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Emitted when a character directs contempt toward another.
    /// Unlike a MicroNegative, contempt is a <b>terminal relationship marker</b>.
    /// </summary>
    /// <remarks>
    /// Gottman (1994, <i>What Predicts Divorce</i>): among the Four Horsemen
    /// (criticism, contempt, defensiveness, stonewalling), <b>contempt is the strongest
    /// predictor of relationship dissolution</b> and the only one that is practically
    /// irreversible in the model. Criticism and stonewalling are reversible;
    /// contempt sets an immutable flag on the directed edge.
    /// <para>
    /// Effect: large step-drop in Trust and Like, sets
    /// <see cref="RelationshipEdge.IsContemptuouslyDestroyed"/> = <c>true</c>.
    /// RepairAttempts can no longer rebuild the relationship above a hard ceiling.
    /// </para>
    /// </remarks>
    public sealed record ContemptuousActPerformed(
        WDateTime OccurredAt,

        /// <summary>The character who expressed contempt.</summary>
        HumanId From,

        /// <summary>The character who received the contempt.</summary>
        HumanId To) : IDomainEvent;
}
