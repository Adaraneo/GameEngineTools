// WDateTimeJsonConverter.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Utils.Time
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using GameEngineTools.World.Core.Time;

    /// <summary>
    /// JSON konverter pro <see cref="WDateTime"/>.
    /// Serializes as a string in the format <c>YYYY-MM-DDTHH:MM:SS[.subW]</c>,
    /// and deserializes back via <see cref="WDateTime.TryParse(string?, out WDateTime)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ambient design.</b> The converter now requires <see cref="WWorld"/> to be configured
    /// instead of an explicitly passed <c>WorldTimeContext</c>. This lets it be
    /// used directly as the attribute <c>[JsonConverter(typeof(WDateTimeJsonConverter))]</c>
    /// na <see cref="WDateTime"/> bez DI.
    /// </para>
    /// <para>
    /// Backward compatibility: the constructor accepting a <c>WorldTimeContext</c>
    /// is kept but ignored — internally <see cref="WWorld.Spec"/> is always used.
    /// </para>
    /// </remarks>
    public sealed class WDateTimeJsonConverter : JsonConverter<WDateTime>
    {
        #region Konstrukce

        /// <summary>
        /// Initializes the converter — requires <see cref="WWorld"/> to be configured.
        /// Use as the attribute: <c>[JsonConverter(typeof(WDateTimeJsonConverter))]</c>.
        /// </summary>
        public WDateTimeJsonConverter()
        { }

        #endregion Konstrukce

        #region JsonConverter<WDateTime>

        /// <inheritdoc/>
        /// <exception cref="JsonException">If the token is not a string, or the value cannot be parsed.</exception>
        public override WDateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("WDateTime očekává JSON string.");

            var s = reader.GetString();
            if (s is null || !WDateTime.TryParse(s, out var v))
                throw new JsonException($"Neplatný WDateTime: '{s}'.");

            return v;
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, WDateTime value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());

        #endregion JsonConverter<WDateTime>
    }
}
