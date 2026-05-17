// CharacterDied.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Domain event emitted by <see cref="GameEngineTools.Characters.GameObjects.CharacterBase"/>
    /// when a character's health reaches zero.
    /// </summary>
    /// <remarks>
    /// Placed in the Physiology namespace because death is the terminal state of
    /// physical integrity — the same domain that owns <see cref="InjuryReceived"/>,
    /// <see cref="InjuryHealed"/>, and related body-state events.
    /// Subscribers (SimulationScene, narrative systems) should react by removing
    /// the character from active rosters. The event is never emitted more than once
    /// per character — see <see cref="GameEngineTools.Characters.GameObjects.CharacterBase.IsDead"/>.
    /// </remarks>
    /// <param name="OccurredAt">World time at which the character died.</param>
    /// <param name="VictimId">The character who died.</param>
    /// <param name="FinalDamageTaken">Amount of the killing blow — useful for narrative formatting.</param>
    public sealed record CharacterDied(
        WDateTime OccurredAt,
        HumanId VictimId,
        double FinalDamageTaken) : IDomainEvent;
}
