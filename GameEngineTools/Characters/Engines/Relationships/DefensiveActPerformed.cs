// DefensiveActPerformed.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Emitted when a character responds to a grievance by denying responsibility or
    /// counter-blaming, instead of acknowledging it.
    /// </summary>
    /// <remarks>
    /// Gottman (1994, <i>What Predicts Divorce</i>): one of the "Four Horsemen" negative-behavior
    /// categories, used here <b>descriptively only</b> — the categorization is widely cited and
    /// useful, but the original predictive-accuracy claims tied to it are rejected as a citable
    /// source (see <see cref="ContemptuousActPerformed"/> remarks for the crossvalidation critique:
    /// Heyman &amp; Smith Slep 2001; Kim, Capaldi &amp; Crosby 2007).
    /// <para>
    /// Unlike contempt, defensiveness is <b>reversible</b> in Gottman's own framework — no
    /// irreversible flag is set. Effect: moderate Comfort drop (the grievance-raiser feels unheard)
    /// and a small Trust drop, feeding <see cref="RelationshipEdge.TransgressionResidue"/>.
    /// </para>
    /// </remarks>
    public sealed record DefensiveActPerformed(
        WDateTime OccurredAt,

        /// <summary>The character who responded defensively.</summary>
        HumanId From,

        /// <summary>The character whose grievance was deflected.</summary>
        HumanId To) : IDomainEvent;
}
