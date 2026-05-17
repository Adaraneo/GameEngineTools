// CharacterDied.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>Primary cause of a character's death.</summary>
    public enum DeathCause
    {
        /// <summary>Health reached zero from damage.</summary>
        Combat,
        /// <summary>Terminal hunger + thirst.</summary>
        Starvation,
        /// <summary>Terminal energy depletion with extreme sleep debt.</summary>
        Exhaustion,
        /// <summary>Allostatic overload or systemic immune failure.</summary>
        SystemicFailure,
        /// <summary>Natural end-of-lifespan (Gompertz curve).</summary>
        OldAge
    }

    /// <summary>
    /// Domain event emitted when a character dies — either from combat damage
    /// (<see cref="DeathCause.Combat"/>) or from natural physiology failure.
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
    /// <param name="Cause">Primary cause of death.</param>
    /// <param name="FinalDamageTaken">Amount of the killing blow; 0 for natural death.</param>
    public sealed record CharacterDied(
        WDateTime OccurredAt,
        HumanId VictimId,
        DeathCause Cause,
        double FinalDamageTaken = 0.0) : IDomainEvent;
}
