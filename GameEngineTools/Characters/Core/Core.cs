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

    public sealed class HumanIdJsonConverter : JsonConverter<HumanId>
    {
        public override HumanId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new HumanId(Guid.Parse(reader.GetString()!));

        public override void Write(Utf8JsonWriter writer, HumanId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value.ToString());

        // Tyhle dvě metody umožní použití jako klíč slovníku
        public override HumanId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new HumanId(Guid.Parse(reader.GetString()!));

        public override void WriteAsPropertyName(Utf8JsonWriter writer, HumanId value, JsonSerializerOptions options)
            => writer.WritePropertyName(value.Value.ToString());
    }

    public interface IHuman : IComparable<IHuman>
    {
        HumanId Id { get; }
        Identity Identity { get; }
        SexBiology Biology { get; }   // Female/Male/Intersex etc.
        Personality Personality { get; } // Trait modul (nemění se rychle)
        PsychologicalProfile PsychologyProfile { get; }
        PhysicalAppearance PhysicalAppearance { get; }

        /// <summary>
        /// Immutable genetic blueprint — null for test stubs that do not exercise appearance projection.
        /// </summary>
        GeneticBlueprint? GeneticBlueprint => null;

        AttractionProfile? AttractionProfile { get; }

        // Přístup ke stavům (read-only snapshoty)
        EnginesSnapshot Snapshot { get; }

        IReadOnlyList<IDomainEvent> LastOutbox { get; }

        int Age { get; }

        StadiumType Stadium { get; }

        // Orchestrace jednoho kroku simulace
        void Tick(WDateTime now, WTimeSpan dt);

        // Příjem externích podnětů (environment, jiné postavy)
        void ReceiveEvent(IDomainEvent @event);

        void RestoreSnapshot(EnginesSnapshot snapshot, WDateOnly today = default);

        /// <summary>
        /// Immediately processes all queued inbox events without advancing time.
        /// Use at world-setup time after FamilyBuilder.Wire() to make seeded edges
        /// immediately visible in Snapshot.Relationships.
        /// </summary>
        void FlushInbox();

        /// <summary>
        /// Injektuje ambientní stav prostředí před tickem postavy.
        /// Volá <see cref="SimulationScene"/> jednou za tick, před <c>Tick()</c>.
        /// Výchozí implementace je no-op — pro testovací stuby.
        /// </summary>
        void SetAmbientContext(double ambientTempC, CelestialContext? celestial) { }

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

        void SetLastName(IHuman partner) { }
    }

    public readonly record struct HumanId(Guid Value);
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

    public enum SexBiology
    { Female, Male, Intersex, Unknown }

    public enum GenderIdentity
    { Woman, Man, Nonbinary, Other, Unspecified }

    // Společný kontrakt pro všechny enginy
    public interface IEngine<TState, TConfig>
    {
        TState State { get; }
        TConfig Config { get; }

        // Deterministický průchod na základě vstupů a času
        void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox);

        // Reakce na doménové události (z jiných enginů / prostředí)
        void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox);

        void RestoreState(TState state);
    }

    // Kontext postavy, který enginy potřebují: pouze read-only rozhraní + služby
    public interface IHumanContext
    {
        HumanId Id { get; }
        Identity Identity { get; }
        SexBiology Biology { get; }
        Personality Personality { get; }
        PsychologicalProfile PsychologyProfile { get; }
        AttractionProfile? AttractionProfile { get; }

        EnginesSnapshot Snapshot { get; }

        /// <summary>
        /// Combined bitmask of all body/mind channels currently occupied by non-expired actions.
        /// Set by OrchestratedHuman before each behavior tick.
        /// </summary>
        ActionSlotMask OccupiedSlots { get; }

        IEventBus EventBus { get; }
        IScheduler Scheduler { get; }
        IRandomSource Random { get; }
        ILogger Logger { get; }
    }

    // Lehké rozhraní pro sběr událostí v rámci kroku (publikace až po Tick)
    public interface IEventCollector
    {
        void Add(IDomainEvent @event);

        IReadOnlyList<IDomainEvent> Drain(); // použije orchestrátor
    }

    // Event bus / plánování
    public interface IDomainEvent
    { WDateTime OccurredAt { get; } }

    public interface IEventBus
    {
        void Publish(IDomainEvent @event);

        IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class, IDomainEvent;
    }

    public interface IScheduler
    {
        ScheduledId ScheduleAt(WDateTime when, ScheduledAction action, string? tag = null);

        ScheduledId ScheduleAfter(WDateTime now, WTimeSpan delay, ScheduledAction action, string? tag = null);

        bool Cancel(ScheduledId id);

        // Pull model pro orchestrátor, aby spouštěl akce v Ticku
        IEnumerable<(ScheduledId id, ScheduledAction action)> Due(WDateTime now);
    }

    public readonly record struct ScheduledId(Guid Value);

    public delegate void ScheduledAction(IHumanContext ctx, IEventCollector outbox);

    // Deterministické RNG (per-postava seed)
    public interface IRandomSource
    {
        int Next(int minInclusive, int maxExclusive);

        double NextUnit(); // <0,1)

        bool Chance(double p);
    }

    // Sjednocený snapshot stavů (pro dotazy)
    public sealed record EnginesSnapshot(
        PhysiologyState Physiology,
        PsychologyState Psychology,
        BehaviorState Behavior,
        InteractionSurface InteractionSurface,
        RelationshipState Relationships,
        MemoryIndex Memory,
        SemanticMemoryState? SemanticMemory = null,
        /// <summary>
        /// Teplota prostředí v místě postavy (°C). Nastaveno simulační vrstvou per-tick
        /// z <c>LocationDescriptor</c>. Výchozí 20 °C = neutrální interiér.
        /// Čteno Psychology enginem (vedro → agrese, mírný chlad → afiliace).
        /// </summary>
        double AmbientTemperature = 20.0,
        /// <summary>
        /// Nadmořská výška v místě postavy (m). Nastaveno simulační vrstvou per-tick.
        /// &gt;2000 m → hypoxie (Energy↓, CogLoad↑). &gt;4000 m → AMS (Pain↑).
        /// Výchozí 0 = hladina moře.
        /// </summary>
        double AltitudeMeters = 0.0,
        /// <summary>
        /// Astronomický kontext pro aktuální tick — ozáření, délka dne, východ/západ Slunce,
        /// sezóna a teplota prostředí. Nastaveno <see cref="SimulationScene"/> přes
        /// <see cref="IHuman.SetAmbientContext"/> před tickem každé postavy.
        /// <c>null</c> pokud astronomická logika není nakonfigurována.
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
        GameEngineTools.Characters.Engines.Interests.InterestState? Interests = null);
}
