// EntityRef.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Contracts
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Objects;

    /// <summary>Which kind of world entity an <see cref="EntityId"/> points at.</summary>
    public enum EntityKind
    {
        /// <summary>A character (<see cref="HumanId"/>, Guid-backed).</summary>
        Human,

        /// <summary>A world object (<see cref="WorldObject.Id"/>, string-backed).</summary>
        Object
    }

    /// <summary>
    /// A stable, serialization-safe identifier for a world entity, unifying the Guid-backed
    /// <see cref="HumanId"/> and the string-backed <see cref="WorldObject.Id"/> behind one value type.
    /// </summary>
    /// <remarks>
    /// Never holds a live object reference — memory (with Ebbinghaus decay) must not keep the world
    /// alive, and world export/import must round-trip. Resolving to a live entity is the job of
    /// <see cref="IEntityResolver"/>.
    /// </remarks>
    /// <param name="Kind">Whether <paramref name="Value"/> is a human or an object id.</param>
    /// <param name="Value">The canonical string form of the underlying id.</param>
    [JsonConverter(typeof(EntityIdJsonConverter))]
    public readonly record struct EntityId(EntityKind Kind, string Value)
    {
        /// <summary>Creates an <see cref="EntityId"/> from a character id.</summary>
        public static EntityId Of(HumanId id) => new(EntityKind.Human, id.Value.ToString());

        /// <summary>Creates an <see cref="EntityId"/> from a world object.</summary>
        public static EntityId Of(WorldObject obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
            return new EntityId(EntityKind.Object, obj.Id);
        }

        /// <summary>Creates an <see cref="EntityId"/> from a raw world-object id.</summary>
        public static EntityId ForObject(string objectId) => new(EntityKind.Object, objectId);

        /// <summary>
        /// Recovers the wrapped <see cref="HumanId"/> when this id refers to a human.
        /// </summary>
        /// <param name="id">The recovered human id when the method returns <c>true</c>.</param>
        /// <returns><c>true</c> if this is a human id and parsed successfully; otherwise <c>false</c>.</returns>
        public bool TryAsHumanId(out HumanId id)
        {
            if (Kind == EntityKind.Human && Guid.TryParse(Value, out var guid))
            {
                id = new HumanId(guid);
                return true;
            }

            id = default;
            return false;
        }
    }

    /// <summary>
    /// A late-resolving reference to a world entity used inside a <see cref="SpeechAct"/>. Carries the
    /// stable <see cref="EntityId"/> plus the nominal lemma at the moment of the act, so the GM side
    /// can later run referring-expression generation without a live object graph.
    /// </summary>
    /// <param name="Id">Stable identifier of the referent.</param>
    /// <param name="LemmaSnapshot">Nominal lemma captured at act time (may be empty until REG is wired).</param>
    public readonly record struct EntityRef(EntityId Id, string LemmaSnapshot)
    {
        /// <summary>Builds a reference to a character with an optional captured lemma.</summary>
        public static EntityRef ForHuman(HumanId id, string lemma = "") => new(EntityId.Of(id), lemma);

        /// <summary>Builds a reference to a world object with an optional captured lemma.</summary>
        public static EntityRef ForObject(WorldObject obj, string lemma = "") => new(EntityId.Of(obj), lemma);
    }

    /// <summary>
    /// Resolves an <see cref="EntityRef"/> to a live entity against a per-listener knowledge base.
    /// Resolving is intentionally listener-relative — a listener may resolve a reference differently
    /// from the speaker's intent (mis-resolution is a modelled feature, wired in a later phase).
    /// </summary>
    public interface IEntityResolver
    {
        /// <summary>
        /// Attempts to resolve <paramref name="reference"/> to a live <see cref="HumanId"/> known to
        /// the listener.
        /// </summary>
        /// <param name="reference">The reference carried by a speech act.</param>
        /// <param name="human">The resolved character id when the method returns <c>true</c>.</param>
        /// <returns><c>true</c> if the reference resolves to a known character; otherwise <c>false</c>.</returns>
        bool TryResolveHuman(EntityRef reference, out HumanId human);
    }

    /// <summary>
    /// <see cref="JsonConverter{T}"/> for <see cref="EntityId"/> — serializes as a compact
    /// <c>"Kind:Value"</c> string and supports use as a dictionary key. Mirrors
    /// <see cref="HumanIdJsonConverter"/>.
    /// </summary>
    public sealed class EntityIdJsonConverter : JsonConverter<EntityId>
    {
        /// <inheritdoc/>
        public override EntityId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Parse(reader.GetString());

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, EntityId value, JsonSerializerOptions options)
            => writer.WriteStringValue(Format(value));

        /// <inheritdoc/>
        public override EntityId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Parse(reader.GetString());

        /// <inheritdoc/>
        public override void WriteAsPropertyName(Utf8JsonWriter writer, EntityId value, JsonSerializerOptions options)
            => writer.WritePropertyName(Format(value));

        private static string Format(EntityId value) => $"{value.Kind}:{value.Value}";

        private static EntityId Parse(string? raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                throw new JsonException("EntityId string was null or empty.");
            }

            var sep = raw.IndexOf(':');
            if (sep < 0 || !Enum.TryParse<EntityKind>(raw.AsSpan(0, sep), out var kind))
            {
                throw new JsonException($"Malformed EntityId '{raw}'.");
            }

            return new EntityId(kind, raw[(sep + 1)..]);
        }
    }
}
