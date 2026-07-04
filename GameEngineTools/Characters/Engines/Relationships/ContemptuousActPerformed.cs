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
    /// <para>
    /// <b>Predictive-accuracy caveat (research-gate, resolved):</b> Gottman's original predictive
    /// claims (~90–96% divorce-prediction accuracy) do NOT hold under crossvalidation and are
    /// explicitly NOT relied upon anywhere in this engine. Source: Heyman &amp; Smith Slep (2001,
    /// <i>Journal of Marriage and Family</i>, 63(2), 473–479) — accuracy dropped from 90% to 69% on
    /// a held-out crossvalidation split; Kim, Capaldi &amp; Crosby (2007, <i>JMF</i>, 69(1), 55–72) —
    /// independent replication (85 couples) failed to confirm the original predictive claims;
    /// DeKay, Greeno &amp; Houck (2002, <i>Family Process</i>, 41(1), 97–103) — the underlying
    /// Gottman &amp; Levenson (2002) duration model rests on 15 divorcing cases including one
    /// high-influence outlier. Only the descriptive Four-Horsemen taxonomy is adopted here.
    /// </para>
    /// </remarks>
    public sealed record ContemptuousActPerformed(
        WDateTime OccurredAt,

        /// <summary>The character who expressed contempt.</summary>
        HumanId From,

        /// <summary>The character who received the contempt.</summary>
        HumanId To) : IDomainEvent;
}
