// ObserverNormReaction.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interactions
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Reaction type an observer has when witnessing a norm violation.
    /// </summary>
    /// <remarks>
    /// Based on Hartsough, Ginther &amp; Marois (2020) and Lickel et al. (2005):
    /// <list type="table">
    ///   <item><term><see cref="Anger"/></term><description>Direct victim or addressee — second-party reaction.</description></item>
    ///   <item><term><see cref="MoralOutrage"/></term><description>Uninvolved third party — drives altruistic punishment.</description></item>
    ///   <item><term><see cref="VicariousShame"/></term><description>Observer who shares group identity with the actor — distancing motivation.</description></item>
    /// </list>
    /// </remarks>
    public enum ObserverReactionKind
    {
        /// <summary>Observer is the victim or addressee of the act.</summary>
        Anger,

        /// <summary>Observer is an uninvolved third party — moral outrage, punishment motivation.</summary>
        MoralOutrage,

        /// <summary>Observer shares in-group identity with the violating actor — vicarious shame, distancing.</summary>
        VicariousShame
    }

    /// <summary>
    /// Domain event emitted once per observer who witnesses a norm violation.
    /// </summary>
    /// <remarks>
    /// Routed by <see cref="GameEngineTools.Characters.Engines.Interactions.DefaultInteractionEngine"/>
    /// to each character in <c>InteractionSurface.Observers</c>.
    /// Consumed by <c>DefaultPsychologyEngine</c> and <c>RelationshipsEngine</c> on each observer's tick.
    /// </remarks>
    /// <param name="OccurredAt">Simulation timestamp.</param>
    /// <param name="Observer">The character who witnessed the violation.</param>
    /// <param name="Actor">The character who committed the violation.</param>
    /// <param name="Victim">
    /// The addressee of the norm-violating act, or <c>null</c> if there is no direct victim
    /// (e.g., public conduct violation with no specific target).
    /// </param>
    /// <param name="NormKind">The violated norm category.</param>
    /// <param name="ReactionKind">The type of emotional reaction the observer experiences.</param>
    /// <param name="ViolationScore">Propagated score for downstream magnitude calculation.</param>
    public sealed record ObserverNormReaction(
        WDateTime OccurredAt,
        HumanId Observer,
        HumanId Actor,
        HumanId? Victim,
        SocialNormKind NormKind,
        ObserverReactionKind ReactionKind,
        double ViolationScore) : IDomainEvent;
}
