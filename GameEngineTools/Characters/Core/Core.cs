// Core.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
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
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// <see cref="JsonConverter{T}"/> for <see cref="HumanId"/> — serializes the wrapped
    /// <see cref="Guid"/> as a string and supports use as a dictionary key.
    /// </summary>
    public sealed class HumanIdJsonConverter : JsonConverter<HumanId>
    {
        /// <inheritdoc/>
        public override HumanId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new HumanId(Guid.Parse(reader.GetString()!));

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, HumanId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value.ToString());

        /// <inheritdoc/>
        public override HumanId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new HumanId(Guid.Parse(reader.GetString()!));

        /// <inheritdoc/>
        public override void WriteAsPropertyName(Utf8JsonWriter writer, HumanId value, JsonSerializerOptions options)
            => writer.WritePropertyName(value.Value.ToString());
    }

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
        /// Changes the character's occupation and re-seeds the daily schedule.
        /// Existing scheduled slots for the current and future days are replaced.
        /// </summary>
        void ChangeOccupation(string? newOccupationId) { }

        /// <summary>Adopts <paramref name="partner"/>'s surname (e.g. on marriage). Default no-op.</summary>
        void SetLastName(IHuman partner) { }
    }

    /// <summary>Stable unique identifier for a character, wrapping a <see cref="Guid"/>.</summary>
    public readonly record struct HumanId(Guid Value);

    /// <summary>A character's name and birth date plus optional home-location and ancestry hints.</summary>
    /// <param name="FirstName">Given name.</param>
    /// <param name="LastName">Surname.</param>
    /// <param name="BirthDate">Date of birth.</param>
    public sealed record Identity(Name FirstName, Surname LastName, WDateOnly BirthDate)
    {
        /// <summary>
        /// Location ID that this character considers "home territory".
        /// When <c>InteractionSurface.Location</c> matches this value, ambient noise is treated
        /// as controllable → noise-induced stress is reduced by <c>HomeNoiseStressMultiplier</c>
        /// (Glass &amp; Singer 1972: controllable noise has dramatically lower cortisol response).
        /// <c>null</c> means no home is assigned (e.g. travellers, characters without fixed residence).
        /// </summary>
        public string? HomeLocationId { get; init; }

        /// <summary>
        /// Optional ancestry hint used by the portrait and narrative systems.
        /// Expressed as a short natural-language descriptor understood by image generation models.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Examples: <c>"East Asian ancestry"</c>, <c>"West African features"</c>,
        /// <c>"Northern European ancestry"</c>, <c>"South Asian features"</c>.
        /// </para>
        /// <para>
        /// Set once at character-creation time by the identity generator.
        /// The portrait pipeline forwards this verbatim to the image model prompt.
        /// <c>null</c> means ancestry is unspecified — the prompt omits the hint entirely.
        /// </para>
        /// </remarks>
        public string? AncestryHint { get; init; }
    }

    /// <summary>Biological sex.</summary>
    public enum SexBiology
    {
        /// <summary>Female.</summary>
        Female,

        /// <summary>Male.</summary>
        Male,

        /// <summary>Intersex.</summary>
        Intersex,

        /// <summary>Unknown / unspecified.</summary>
        Unknown
    }

    /// <summary>Gender identity.</summary>
    public enum GenderIdentity
    {
        /// <summary>Woman.</summary>
        Woman,

        /// <summary>Man.</summary>
        Man,

        /// <summary>Nonbinary.</summary>
        Nonbinary,

        /// <summary>Another identity.</summary>
        Other,

        /// <summary>Unspecified.</summary>
        Unspecified
    }

    /// <summary>
    /// Common contract for all character engines: exposes immutable <see cref="State"/> and
    /// <see cref="Config"/> and the deterministic tick/handle/restore lifecycle.
    /// </summary>
    /// <typeparam name="TState">The engine's state record type.</typeparam>
    /// <typeparam name="TConfig">The engine's configuration record type.</typeparam>
    public interface IEngine<TState, TConfig>
    {
        /// <summary>Current engine state.</summary>
        TState State { get; }

        /// <summary>Engine configuration.</summary>
        TConfig Config { get; }

        /// <summary>Deterministically advances state from the given inputs and elapsed time.</summary>
        /// <param name="now">Current game time.</param>
        /// <param name="dt">Elapsed time since the previous tick.</param>
        /// <param name="ctx">Read-only character context and services.</param>
        /// <param name="outbox">Collector for events emitted this tick.</param>
        void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox);

        /// <summary>Reacts to a domain event from another engine or the environment.</summary>
        /// <param name="event">The event to handle.</param>
        /// <param name="ctx">Read-only character context and services.</param>
        /// <param name="outbox">Collector for events emitted in response.</param>
        void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox);

        /// <summary>Restores the engine to a previously persisted state.</summary>
        /// <param name="state">The state to restore.</param>
        void RestoreState(TState state);
    }

    /// <summary>
    /// Immutable per-tick context handed to every engine: read-only identity/traits, the
    /// engines snapshot, occupied action slots, and the four shared services.
    /// </summary>
    public interface IHumanContext
    {
        /// <summary>Character identifier.</summary>
        HumanId Id { get; }

        /// <summary>Name and birth-date identity.</summary>
        Identity Identity { get; }

        /// <summary>Biological sex.</summary>
        SexBiology Biology { get; }

        /// <summary>Big Five personality traits.</summary>
        Personality Personality { get; }

        /// <summary>Psychological trait profile.</summary>
        PsychologicalProfile PsychologyProfile { get; }

        /// <summary>Attraction-relevant traits, or <c>null</c>.</summary>
        AttractionProfile? AttractionProfile { get; }

        /// <summary>Read-only snapshot of all engine states.</summary>
        EnginesSnapshot Snapshot { get; }

        /// <summary>
        /// Combined bitmask of all body/mind channels currently occupied by non-expired actions.
        /// Set by OrchestratedHuman before each behavior tick.
        /// </summary>
        ActionSlotMask OccupiedSlots { get; }

        /// <summary>Event bus for publishing/subscribing to domain events.</summary>
        IEventBus EventBus { get; }

        /// <summary>Scheduler for deferred actions.</summary>
        IScheduler Scheduler { get; }

        /// <summary>Deterministic per-character random source.</summary>
        IRandomSource Random { get; }

        /// <summary>Logger.</summary>
        ILogger Logger { get; }
    }

    /// <summary>
    /// Lightweight per-tick event collector. Engines add events during a tick; the
    /// orchestrator drains and publishes them afterwards.
    /// </summary>
    public interface IEventCollector
    {
        /// <summary>Adds an event to the collector.</summary>
        /// <param name="event">The event to add.</param>
        void Add(IDomainEvent @event);

        /// <summary>Removes and returns all collected events.</summary>
        IReadOnlyList<IDomainEvent> Drain(); // used by the orchestrator
    }

    /// <summary>Base contract for all domain events — every event carries the time it occurred.</summary>
    public interface IDomainEvent
    {
        /// <summary>Game time at which the event occurred.</summary>
        WDateTime OccurredAt { get; }
    }

    /// <summary>In-process publish/subscribe event bus.</summary>
    public interface IEventBus
    {
        /// <summary>Publishes an event to all subscribers.</summary>
        /// <param name="event">The event to publish.</param>
        void Publish(IDomainEvent @event);

        /// <summary>Subscribes a handler to events of type <typeparamref name="TEvent"/>.</summary>
        /// <typeparam name="TEvent">Event type to subscribe to.</typeparam>
        /// <param name="handler">Handler invoked for each matching event.</param>
        /// <returns>A disposable that cancels the subscription.</returns>
        IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class, IDomainEvent;
    }

    /// <summary>Schedules actions to run at or after a given game time.</summary>
    public interface IScheduler
    {
        /// <summary>Schedules an action at an absolute game time.</summary>
        /// <param name="when">When to run the action.</param>
        /// <param name="action">The action to run.</param>
        /// <param name="tag">Optional tag for grouping/cancellation.</param>
        /// <returns>An identifier that can be used to cancel the action.</returns>
        ScheduledId ScheduleAt(WDateTime when, ScheduledAction action, string? tag = null);

        /// <summary>Schedules an action after a delay relative to <paramref name="now"/>.</summary>
        /// <param name="now">Reference time.</param>
        /// <param name="delay">Delay before the action runs.</param>
        /// <param name="action">The action to run.</param>
        /// <param name="tag">Optional tag for grouping/cancellation.</param>
        /// <returns>An identifier that can be used to cancel the action.</returns>
        ScheduledId ScheduleAfter(WDateTime now, WTimeSpan delay, ScheduledAction action, string? tag = null);

        /// <summary>Cancels a scheduled action.</summary>
        /// <param name="id">Identifier of the action to cancel.</param>
        /// <returns><c>true</c> if an action was cancelled.</returns>
        bool Cancel(ScheduledId id);

        /// <summary>Returns the actions due at or before <paramref name="now"/> (pull model).</summary>
        /// <param name="now">Current game time.</param>
        IEnumerable<(ScheduledId id, ScheduledAction action)> Due(WDateTime now);
    }

    /// <summary>Identifier for a scheduled action.</summary>
    public readonly record struct ScheduledId(Guid Value);

    /// <summary>Delegate run by the scheduler when a scheduled action fires.</summary>
    /// <param name="ctx">Character context and services.</param>
    /// <param name="outbox">Collector for events emitted by the action.</param>
    public delegate void ScheduledAction(IHumanContext ctx, IEventCollector outbox);

    /// <summary>Deterministic random source seeded per character.</summary>
    public interface IRandomSource
    {
        /// <summary>Returns a random integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
        /// <param name="minInclusive">Inclusive lower bound.</param>
        /// <param name="maxExclusive">Exclusive upper bound.</param>
        int Next(int minInclusive, int maxExclusive);

        /// <summary>Returns a random double in [0, 1).</summary>
        double NextUnit(); // <0,1)

        /// <summary>Returns <c>true</c> with probability <paramref name="p"/>.</summary>
        /// <param name="p">Probability in [0, 1].</param>
        bool Chance(double p);
    }

    /// <summary>
    /// Unified read-only snapshot of every engine's state plus ambient environment context.
    /// Used for Phase-A event delivery and persistence.
    /// </summary>
    /// <param name="Physiology">Physiology engine state.</param>
    /// <param name="Psychology">Psychology engine state.</param>
    /// <param name="Behavior">Behavior engine state.</param>
    /// <param name="InteractionSurface">Current interaction surface.</param>
    /// <param name="Relationships">Relationship graph state.</param>
    /// <param name="Memory">Episodic memory index.</param>
    /// <param name="SemanticMemory">Semantic (belief) memory state, or <c>null</c>.</param>
    public sealed record EnginesSnapshot(
        PhysiologyState Physiology,
        PsychologyState Psychology,
        BehaviorState Behavior,
        InteractionSurface InteractionSurface,
        RelationshipState Relationships,
        MemoryIndex Memory,
        SemanticMemoryState? SemanticMemory = null,
        /// <summary>
        /// Ambient temperature at the character's location (°C). Set by the simulation layer per tick
        /// from <c>LocationDescriptor</c>. Default 20 °C = neutral indoors.
        /// Read by the Psychology engine (heat → aggression, mild cold → affiliation).
        /// </summary>
        double AmbientTemperature = 20.0,
        /// <summary>
        /// Altitude at the character's location (m). Set by the simulation layer per tick.
        /// &gt;2000 m → hypoxie (Energy↓, CogLoad↑). &gt;4000 m → AMS (Pain↑).
        /// Default 0 = sea level.
        /// </summary>
        double AltitudeMeters = 0.0,
        /// <summary>
        /// Astronomical context for the current tick — irradiance, day length, sunrise/sunset,
        /// season and ambient temperature. Set by <see cref="GameEngineTools.World.Simulation.SimulationScene"/> via
        /// <see cref="IHuman.SetAmbientContext"/> before each character's tick.
        /// <c>null</c> if the astronomical logic is not configured.
        /// </summary>
        CelestialContext? Celestial = null,
        /// <summary>
        /// Persistent long-term goals for this character.
        /// <c>null</c> for characters created before this field existed (backward compatibility).
        /// </summary>
        GoalState? Goals = null,
        /// <summary>
        /// Daily schedule state for this character.
        /// <c>null</c> for characters created before this field existed (backward compatibility).
        /// </summary>
        DailyScheduleState? Schedule = null,

        /// <summary>
        /// Schwartz Basic Human Values state for this character: the drifting <c>Current</c>
        /// profile plus the immutable <c>Baseline</c> seeded from BigFive at creation.
        /// <c>null</c> for characters created before this field existed (backward compatibility).
        /// Read by <see cref="GameEngineTools.Characters.Engines.Behavior.Modifiers.ValuesBehaviorModifier"/>;
        /// evolved by <see cref="GameEngineTools.Characters.Engines.Values.DefaultValuesEngine"/>.
        /// </summary>
        GameEngineTools.Characters.Engines.Values.ValuesState? Values = null,

        /// <summary>
        /// The character's self-concept (perceived Big Five, ideal subset, self-esteem, discrepancy).
        /// <c>null</c> for characters created before this field existed (backward compatibility).
        /// Evolved by <see cref="GameEngineTools.Characters.Engines.SelfConcept.DefaultSelfConceptEngine"/>.
        /// </summary>
        GameEngineTools.Characters.Engines.SelfConcept.SelfConcept? SelfConcept = null,

        /// <summary>
        /// The character's RIASEC interest state (drifting Current + immutable Baseline).
        /// <c>null</c> for characters created before this field existed (backward compatibility).
        /// Evolved by <see cref="GameEngineTools.Characters.Engines.Interests.DefaultInterestEngine"/>.
        /// </summary>
        GameEngineTools.Characters.Engines.Interests.InterestState? Interests = null,

        /// <summary>
        /// The character's social comparison state (throttle timestamp for the reflective
        /// comparison cadence). <c>null</c> for characters created before this field existed
        /// or when the engine is not wired (backward compatibility).
        /// Evolved by <see cref="GameEngineTools.Characters.Engines.Social.DefaultSocialComparisonEngine"/>.
        /// </summary>
        GameEngineTools.Characters.Engines.Social.SocialComparisonState? SocialComparison = null,

        /// <summary>
        /// Derived SDT basic-need appraisal (Competence/Relatedness/Autonomy satisfaction &amp;
        /// frustration), computed each tick from goals/relationships/regulatory-focus.
        /// <c>null</c> for characters created before this field existed or when the appraisal engine
        /// is not wired; the engine initialises it to <see cref="GameEngineTools.Characters.Engines.Behavior.NeedAppraisal.NeedAppraisalState.Empty"/>
        /// on its first tick (backward compatibility).
        /// </summary>
        GameEngineTools.Characters.Engines.Behavior.NeedAppraisal.NeedAppraisalState? NeedAppraisal = null);
}
