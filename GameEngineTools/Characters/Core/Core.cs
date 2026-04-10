// Core.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Characters.Engines.Behavior;
    using Characters.Engines.Interactions;
    using Characters.Engines.Physiology;
    using Characters.Engines.Psychology;
    using Characters.Engines.Relationships;
    using Characters.Traits;
    using GameEngineTools.Characters.Engines.Memory;
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

    public interface IHuman
    {
        HumanId Id { get; }
        Identity Identity { get; }
        SexBiology Biology { get; }   // Female/Male/Intersex etc.
        Personality Personality { get; } // Trait modul (nemění se rychle)
        PsychologicalProfile PsychologyProfile { get; }
        PhysicalAppearance PhysicalAppearance { get; }

        AttractionProfile AttractionProfile { get; }

        // Přístup ke stavům (read-only snapshoty)
        EnginesSnapshot Snapshot { get; }

        IReadOnlyList<IDomainEvent> LastOutbox { get; }

        int Age { get; }

        // Orchestrace jednoho kroku simulace
        void Tick(WDateTime now, WTimeSpan dt);

        // Příjem externích podnětů (environment, jiné postavy)
        void ReceiveEvent(IDomainEvent @event);

        void RestoreSnapshot(EnginesSnapshot snapshot);

        bool EqualsByIdentity(IHuman? other) => other is not null && Id == other.Id;
    }

    public readonly record struct HumanId(Guid Value);
    public sealed record Identity(Name FirstName, Surname LastName, WDateOnly BirthDate);

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

        EnginesSnapshot Snapshot { get; }

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
        MemoryIndex Memory);
}
