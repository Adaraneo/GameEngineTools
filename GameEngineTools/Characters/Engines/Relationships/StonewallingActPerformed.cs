// StonewallingActPerformed.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Emitted when a character withdraws from a conflict interaction — refusing to engage,
    /// going silent, or physically disengaging.
    /// </summary>
    /// <remarks>
    /// Gottman (1994): the fourth "Horseman", used descriptively only (see
    /// <see cref="DefensiveActPerformed"/> remarks for the same predictive-accuracy caveat).
    /// Reversible — no irreversible flag. Effect: Closeness drop, plus feeds
    /// <see cref="RelationshipEdge.DemandWithdrawScore"/> — the pattern-level (not single-event)
    /// representation of a recurring demand/withdraw dynamic (Schrodt, Witt &amp; Shimkowski 2014).
    /// </remarks>
    public sealed record StonewallingActPerformed(
        WDateTime OccurredAt,

        /// <summary>The character who withdrew from the interaction.</summary>
        HumanId From,

        /// <summary>The character who was stonewalled.</summary>
        HumanId To) : IDomainEvent;
}
