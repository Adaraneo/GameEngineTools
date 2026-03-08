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
    /// Serializuje jako string ve formátu <c>YYYY-MM-DDTHH:MM:SS[.subW]</c>,
    /// deserializuje zpět přes <see cref="WorldTimeContext.TryParse(string?, out WDateTime)"/>.
    /// </summary>
    /// <remarks>
    /// Vyžaduje <see cref="WorldTimeContext"/> — nelze použít jako atribut na <c>WDateTime</c>.
    /// Zaregistruj přes <see cref="JsonSerializerOptions.Converters"/> v DI:
    /// <code>
    /// services.AddSingleton&lt;JsonSerializerOptions&gt;(sp =>
    /// {
    ///     var ctx = sp.GetRequiredService&lt;WorldTimeContext&gt;();
    ///     return new JsonSerializerOptions
    ///     {
    ///         Converters = { new WDateTimeJsonConverter(ctx) }
    ///     };
    /// });
    /// </code>
    /// </remarks>
    public sealed class WDateTimeJsonConverter : JsonConverter<WDateTime>
    {
        #region Soukromá pole

        private readonly WorldTimeContext _ctx;

        #endregion

        #region Konstrukce

        /// <summary>
        /// Inicializuje konverter s kontextem světového času.
        /// </summary>
        /// <param name="ctx">Kontext potřebný pro parsování a formátování.</param>
        public WDateTimeJsonConverter(WorldTimeContext ctx) => _ctx = ctx;

        #endregion

        #region JsonConverter<WDateTime>

        /// <inheritdoc/>
        /// <exception cref="JsonException">
        /// Pokud token není string nebo hodnota nelze naparsovat jako <see cref="WDateTime"/>.
        /// </exception>
        public override WDateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("WDateTime očekává JSON string.");

            var s = reader.GetString();
            if (s is null || !_ctx.TryParse(s, out var v))
                throw new JsonException($"Neplatný WDateTime: '{s}'.");

            return v;
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, WDateTime value, JsonSerializerOptions options)
            => writer.WriteStringValue(_ctx.Format(value));

        #endregion
    }
}
