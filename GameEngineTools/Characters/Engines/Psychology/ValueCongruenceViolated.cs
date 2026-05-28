// ValueCongruenceViolated.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Domain event emitted by <see cref="GameEngineTools.Characters.Engines.Behavior.Modifiers.ValuesBehaviorModifier"/>
    /// when a character commits an action whose value-congruence with their
    /// <see cref="GameEngineTools.Characters.Traits.ValuesProfile"/> falls below the guilt threshold.
    /// </summary>
    /// <remarks>
    /// Consumed by <see cref="DefaultPsychologyEngine"/>,
    /// which applies a Guilt spike proportional to the violation magnitude.
    /// <para>
    /// Theoretical basis: Tangney &amp; Dearing (2002) — guilt is triggered when an action violates
    /// one's own moral standards, specifically Benevolence and Universalism values
    /// (self-transcendence pole of the Schwartz circumplex).
    /// </para>
    /// </remarks>
    /// <param name="OccurredAt">Simulation timestamp.</param>
    /// <param name="Actor">The character who committed the value-violating action.</param>
    /// <param name="ActionName">The name of the committed action.</param>
    /// <param name="Congruence">
    /// The computed congruence score [−1..+1]. Values below −0.30 trigger this event.
    /// More negative = stronger violation = larger Guilt spike.
    /// </param>
    /// <param name="DominantViolatedValue">
    /// Human-readable name of the value most strongly violated by this action.
    /// Used for logging and future narrative formatting.
    /// </param>
    public sealed record ValueCongruenceViolated(
        WDateTime OccurredAt,
        HumanId Actor,
        string ActionName,
        double Congruence,
        string DominantViolatedValue) : IDomainEvent;
}
