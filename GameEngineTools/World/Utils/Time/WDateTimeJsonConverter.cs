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
    /// deserializuje zpět přes <see cref="WDateTime.TryParse(string?, out WDateTime)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ambient design.</b> Konverter nyní vyžaduje nakonfigurovaný <see cref="WWorld"/>
    /// místo explicitně předaného <see cref="WorldTimeContext"/>. Díky tomu ho lze
    /// použít přímo jako atribut <c>[JsonConverter(typeof(WDateTimeJsonConverter))]</c>
    /// na <see cref="WDateTime"/> bez DI.
    /// </para>
    /// <para>
    /// Zpětná kompatibilita: konstruktor přijímající <see cref="WorldTimeContext"/>
    /// je zachován, ale ignoruje se — interně se vždy použije <see cref="WWorld.Spec"/>.
    /// </para>
    /// </remarks>
    public sealed class WDateTimeJsonConverter : JsonConverter<WDateTime>
    {
        #region Konstrukce

        /// <summary>
        /// Inicializuje konverter — vyžaduje nakonfigurovaný <see cref="WWorld"/>.
        /// Použij jako atribut: <c>[JsonConverter(typeof(WDateTimeJsonConverter))]</c>.
        /// </summary>
        public WDateTimeJsonConverter()
        { }

        #endregion Konstrukce

        #region JsonConverter<WDateTime>

        /// <inheritdoc/>
        /// <exception cref="JsonException">Pokud token není string nebo hodnota nelze naparsovat.</exception>
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
