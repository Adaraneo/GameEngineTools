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
    /// Always serializes as raw <c>int64</c> ticks — compact and lossless.
    /// Deserializes from a number or string (<c>ticks</c> or <c>[-]d.hh:mm:ss[.sub]</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ambient design.</b> Internally uses <see cref="WWorld.Spec"/> instead of
    /// an explicitly passed <c>WorldTimeContext</c>.
    /// The backward-compatible constructor is kept but ignores its parameter.
    /// </para>
    /// </remarks>
    public sealed class WTimeSpanJsonConverter : JsonConverter<WTimeSpan>
    {
        #region Konstrukce

        /// <summary>Initializes the converter — requires <see cref="WWorld"/> to be configured.</summary>
        public WTimeSpanJsonConverter()
        { }

        #endregion Konstrukce

        #region JsonConverter<WTimeSpan>

        /// <inheritdoc/>
        /// <exception cref="JsonException">If the token is neither a number nor a string, or the value cannot be parsed.</exception>
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

                        // Try a plain number (ticks)
                        if (long.TryParse(s, out var asTicks))
                            return new WTimeSpan(asTicks);

                        // Zkus [-]d.hh:mm:ss[.sub]
                        if (TryParseGeneral(s!, out var span))
                            return span;

                        throw new JsonException($"WTimeSpan: neplatný formát '{s}'.");
                    }

                default:
                    throw new JsonException("WTimeSpan: očekáván number nebo string.");
            }
        }

        /// <inheritdoc/>
        /// <remarks>Serializes as raw int64 — compact and lossless round-trip.</remarks>
        public override void Write(Utf8JsonWriter writer, WTimeSpan value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value.Ticks);

        #endregion JsonConverter<WTimeSpan>

        #region Privátní parsování

        /// <summary>
        /// Parses a string in the format <c>[-]d.hh:mm:ss[.sub]</c> or <c>[-]hh:mm:ss[.sub]</c>.
        /// Validates the components against <see cref="WWorld.Spec"/>.
        /// </summary>
        private static bool TryParseGeneral(string s, out WTimeSpan span)
        {
            span = default;
            s = s.Trim();

            int sign = 1;
            if (s.StartsWith("-", StringComparison.Ordinal)) { sign = -1; s = s[1..]; }

            // Split into [days] . [rest]
            string[] partsD = s.Split('.', 2);
            long days = 0;
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

            string[] secSub = parts[2].Split('.', 2);
            if (!long.TryParse(secSub[0], out var ss)) return false;
            long subticks = 0;
            if (secSub.Length == 2)
            {
                if (!long.TryParse(secSub[1], out subticks)) return false;
            }

            // Validate against WWorld.Spec
            var spec = WWorld.Spec;
            if (hh < 0 || hh >= spec.HoursPerDay) return false;
            if (mm < 0 || mm >= spec.MinutesPerHour) return false;
            if (ss < 0 || ss >= spec.SecondsPerMinute) return false;
            if (subticks < 0 || subticks >= spec.TicksPerSecond) return false;

            long totalTicks = days * spec.TicksPerDay
                            + hh * spec.TicksPerHour
                            + mm * spec.TicksPerMinute
                            + ss * spec.TicksPerSecond
                            + subticks;

            span = new WTimeSpan(sign * totalTicks);
            return true;
        }

        #endregion Privátní parsování
    }
}
