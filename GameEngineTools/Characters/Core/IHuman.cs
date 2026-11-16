// IHuman.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using System;
    using Characters.Engines.Behavior;
    using Characters.Engines.Goals;
    using Characters.Engines.Interactions;
    using Characters.Engines.Physiology;
    using Characters.Engines.Psychology;
    using Characters.Engines.Relationships;
    using Characters.Engines.Schedule;
    using Characters.Engines.SemanticMemory;
    using Characters.Traits;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.World.Core.Astro;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Read-only contract for a simulated character: stable identity and traits, the current
    /// engines snapshot, and the orchestration entry points (<see cref="Tick"/>, <see cref="ReceiveEvent"/>).
    /// </summary>
    public interface IHuman : IComparable<IHuman>
    {
        /// <summary>Stable unique identifier.</summary>
        HumanId Id { get; }

        /// <summary>Name and birth-date identity.</summary>
        Identity Identity { get; }

        /// <summary>Biological sex.</summary>
        SexBiology Biology { get; }   // Female/Male/Intersex etc.

        /// <summary>Big Five personality traits (slow-changing).</summary>
        Personality Personality { get; } // Trait module (does not change quickly)

        /// <summary>Psychological trait profile (attachment, values, sexual responsiveness, …).</summary>
        PsychologicalProfile PsychologyProfile { get; }

        /// <summary>Static physical appearance traits.</summary>
        PhysicalAppearance PhysicalAppearance { get; }

        /// <summary>
        /// Immutable genetic blueprint — null for test stubs that do not exercise appearance projection.
        /// </summary>
        GeneticBlueprint? GeneticBlueprint => null;

        /// <summary>Attraction-relevant traits, or <c>null</c> when not modelled.</summary>
        AttractionProfile? AttractionProfile { get; }

        // Access to state (read-only snapshots)

        /// <summary>Read-only snapshot of all engine states.</summary>
        EnginesSnapshot Snapshot { get; }

        /// <summary>Domain events emitted during the most recent tick.</summary>
        IReadOnlyList<IDomainEvent> LastOutbox { get; }

        /// <summary>Current age in game years.</summary>
        int Age { get; }

        /// <summary>Current life stage.</summary>
        StadiumType Stadium { get; }

        /// <summary>Advances the character one simulation step.</summary>
        /// <param name="now">Current game time.</param>
        /// <param name="dt">Elapsed time since the previous tick.</param>
        void Tick(WDateTime now, WTimeSpan dt);

        /// <summary>Queues an external stimulus (from the environment or other characters) for delivery.</summary>
        /// <param name="event">The incoming domain event.</param>
        void ReceiveEvent(IDomainEvent @event);

        /// <summary>Restores a previously persisted snapshot.</summary>
        /// <param name="snapshot">The snapshot to restore.</param>
        /// <param name="today">Current game date, used to revalidate age-dependent subsystems.</param>
        void RestoreSnapshot(EnginesSnapshot snapshot, WDateOnly today = default);

        /// <summary>
        /// Immediately processes all queued inbox events without advancing time.
        /// Use at world-setup time after FamilyBuilder.Wire() to make seeded edges
        /// immediately visible in Snapshot.Relationships.
        /// </summary>
        void FlushInbox();

        /// <summary>
        /// Injects the ambient environment state before the character's tick.
        /// Called by <see cref="GameEngineTools.World.Simulation.SimulationScene"/> once per tick, before <c>Tick()</c>.
        /// The default implementation is a no-op — for test stubs.
        /// </summary>
        void SetAmbientContext(double ambientTempC, CelestialContext? celestial) { }

        /// <summary>Returns <c>true</c> when <paramref name="other"/> has the same <see cref="Id"/>.</summary>
        bool EqualsByIdentity(IHuman? other) => other is not null && Id == other.Id;

        /// <summary>
        /// Updates the character's home location at runtime.
        /// The next tick immediately uses the new value — no restart needed.
        /// <c>null</c> removes the home assignment (traveller, displaced character).
        /// </summary>
        void SetHomeLocation(string? locationId) { }

        /// <summary>
        /// Remembers the character's present state (engines, identity) as the state a later <see cref="ResetToInitialState"/> returns to.
        /// The factory calls it when it creates the character; a host that changes the character afterwards (wired kin, a settled home) calls it again once the world is set up.
        /// The default does nothing, for test stubs.
        /// </summary>
        void CaptureInitialState() { }

        /// <summary>
        /// Returns the character to the state it was captured in: every engine, the identity (home, surname), the inbox, scheduled events, running action slots and sleep.
        /// Where the character stands, the objects it holds and the world clock belong to the host and are not touched here.
        /// </summary>
        /// <param name="now">Current game time; age-dependent subsystems are revalidated against its date, and the daily schedule is registered from it.</param>
        void ResetToInitialState(WDateTime now) { }

        /// <summary>
        /// Changes the character's occupation and re-seeds the daily schedule.
        /// Existing scheduled slots for the current and future days are replaced.
        /// </summary>
        void ChangeOccupation(string? newOccupationId) { }

        /// <summary>Adopts <paramref name="partner"/>'s surname (e.g. on marriage). Default no-op.</summary>
        void SetLastName(IHuman partner) { }

        /// <summary>
        /// Injects the character's emergent social status and the local hierarchy stability before its
        /// tick. Called by the scene orchestrator once per tick after folding the
        /// <see cref="GameEngineTools.Characters.Engines.Status.StatusLedger"/>, so Psychology reads an
        /// up-to-date status×stability signal. The default implementation is a no-op — for test stubs.
        /// </summary>
        void SetSocietalStatus(GameEngineTools.Characters.Engines.Status.SocietalStatus status, double hierarchyStability) { }
    }
}
