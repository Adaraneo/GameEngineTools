// HumanIdJsonConverter.cs
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
}
