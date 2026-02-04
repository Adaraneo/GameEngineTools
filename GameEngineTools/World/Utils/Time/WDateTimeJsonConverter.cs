// WDateTimeJsonConverter.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Utils.Time
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;

    public sealed class WDateTimeJsonConverter : JsonConverter<WDateTime>
    {
        public override WDateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("WDateTime očekává JSON string.");
            var s = reader.GetString();
            if (s is null || !WDateTime.TryParse(s, out var v))
                throw new JsonException($"Neplatný WDateTime: '{s}'.");
            return v;
        }

        public override void Write(Utf8JsonWriter writer, WDateTime value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }
}
