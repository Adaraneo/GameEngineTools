// WTimeSpanJsonConverter.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Utils.Time
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using GameEngineTools.World.Core.Time;

    /// <summary>
    /// JSON konverter pro <see cref="WTimeSpan"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Akceptuje dva formáty při deserializaci:
    /// <list type="bullet">
    ///   <item><description><b>číslo (int64)</b> — raw worldTicky, round-trip formát</description></item>
    ///   <item><description><b>string</b> — buď čisté číslo jako string, nebo <c>[-]d.hh:mm:ss[.sub]</c></description></item>
    /// </list>
    /// Serializuje vždy jako číslo (raw ticky) — kompaktní a bezeztrátové.
    /// </para>
    /// <para>
    /// Zaregistruj přes <see cref="JsonSerializerOptions.Converters"/> v DI — viz
    /// <see cref="WDateTimeJsonConverter"/> pro příklad registrace.
    /// </para>
    /// </remarks>
    public sealed class WTimeSpanJsonConverter : JsonConverter<WTimeSpan>
    {
        #region Soukromá pole

        private readonly WorldTimeContext _ctx;

        #endregion

        #region Konstrukce

        /// <summary>
        /// Inicializuje konverter s kontextem světového času.
        /// </summary>
        /// <param name="ctx">Kontext potřebný pro validaci a výpočet tiků z lidských jednotek.</param>
        public WTimeSpanJsonConverter(WorldTimeContext ctx) => _ctx = ctx;

        #endregion

        #region JsonConverter<WTimeSpan>

        /// <inheritdoc/>
        /// <exception cref="JsonException">
        /// Pokud token není číslo ani string nebo hodnotu nelze naparsovat.
        /// </exception>
        public override WTimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out long ticks)) return new WTimeSpan(ticks);
                    throw new JsonException("WTimeSpan: neočekávaný číselný formát.");

                case JsonTokenType.String:
                {
                    var s = reader.GetString();
                    if (string.IsNullOrWhiteSpace(s))
                        throw new JsonException("WTimeSpan: prázdný řetězec.");

                    // Zkus čisté číslo (ticky)
                    if (long.TryParse(s, out var asTicks))
                        return new WTimeSpan(asTicks);

                    // Zkus "[-]d.hh:mm:ss[.sub]" nebo "[-]hh:mm:ss[.sub]"
                    if (TryParseGeneral(s!, out var span))
                        return span;

                    throw new JsonException($"WTimeSpan: neplatný formát '{s}'.");
                }

                default:
                    throw new JsonException("WTimeSpan: očekáván number nebo string.");
            }
        }

        /// <inheritdoc/>
        /// <remarks>Serializuje jako raw int64 ticky — kompaktní a round-trip bezeztrátové.</remarks>
        public override void Write(Utf8JsonWriter writer, WTimeSpan value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value.Ticks);

        #endregion

        #region Privátní parsování

        /// <summary>
        /// Parsuje string ve formátu <c>[-]d.hh:mm:ss[.sub]</c> nebo <c>[-]hh:mm:ss[.sub]</c>.
        /// Složky validuje vůči <see cref="WorldTimeContext.Spec"/>.
        /// </summary>
        /// <param name="s">Vstupní string (bez null).</param>
        /// <param name="span">Výsledný interval, pokud se parsování podaří.</param>
        /// <returns><c>true</c> pokud se parsování podařilo.</returns>
        private bool TryParseGeneral(string s, out WTimeSpan span)
        {
            span = default;
            s    = s.Trim();

            int sign = 1;
            if (s.StartsWith("-", StringComparison.Ordinal)) { sign = -1; s = s[1..]; }

            // Rozděl na [dny] . [rest] — tečka jako oddělovač dnů
            string[] partsD = s.Split('.', 2);
            long   days = 0;
            string rest;

            if (partsD.Length == 2)
            {
                if (!long.TryParse(partsD[0], out days)) return false;
                rest = partsD[1];
            }
            else
            {
                rest = partsD[0];
            }

            // rest = hh:mm:ss[.sub]
            string[] parts = rest.Split(':');
            if (parts.Length != 3) return false;

            if (!long.TryParse(parts[0], out var hh)) return false;
            if (!long.TryParse(parts[1], out var mm)) return false;

            // sekundy a volitelné subticky
            string[] secSub = parts[2].Split('.', 2);
            if (!long.TryParse(secSub[0], out var ss)) return false;
            long subticks = 0;
            if (secSub.Length == 2)
            {
                // Bereme raw subticky v jednotce worldTicku (ne zlomek sekundy)
                if (!long.TryParse(secSub[1], out subticks)) return false;
            }

            // Validace vůči spec — přistupujeme přes _ctx, ne přes WDateTime.Spec (global state)
            var spec = _ctx.Spec;
            if (hh       < 0 || hh       >= spec.HoursPerDay)     return false;
            if (mm       < 0 || mm       >= spec.MinutesPerHour)   return false;
            if (ss       < 0 || ss       >= spec.SecondsPerMinute) return false;
            if (subticks < 0 || subticks >= spec.TicksPerSecond)   return false;

            long totalTicks = days     * spec.TicksPerDay
                            + hh       * spec.TicksPerHour
                            + mm       * spec.TicksPerMinute
                            + ss       * spec.TicksPerSecond
                            + subticks;

            span = new WTimeSpan(sign * totalTicks);
            return true;
        }

        #endregion
    }
}
