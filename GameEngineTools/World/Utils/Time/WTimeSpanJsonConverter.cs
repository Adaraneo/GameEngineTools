using System.Text.Json;
using System.Text.Json.Serialization;
using GameEngineTools.World.Core.Time;

namespace GameEngineTools.World.Utils.Time
{
    public sealed class WTimeSpanJsonConverter : JsonConverter<WTimeSpan>
    {
        private readonly WorldTimeContext _wtctx;

        public WTimeSpanJsonConverter(WorldTimeContext wtctx) => _wtctx = wtctx;
        private bool TryParseGeneral(string s, out WTimeSpan span)
        {
            span = default;
            s = s.Trim();
            int sign = 1;
            if (s.StartsWith("-", StringComparison.Ordinal)) { sign = -1; s = s[1..]; }

            string[] partsD = s.Split('.', 2); // [d] . rest
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

            string[] parts = rest.Split(':');
            if (parts.Length != 3) return false;

            if (!long.TryParse(parts[0], out var hh)) return false;
            if (!long.TryParse(parts[1], out var mm)) return false;

            string[] secSub = parts[2].Split('.', 2);
            if (!long.TryParse(secSub[0], out var ss)) return false;
            long subticks = 0;
            if (secSub.Length == 2)
            {
                if (!long.TryParse(secSub[1], out subticks)) return false; // bereme raw subticks (v jednotce worldTicku)
            }

            // validace v rámci spec
            var spec = _wtctx.Spec;
            if (hh < 0 || hh >= spec.HoursPerDay) return false;
            if (mm < 0 || mm >= spec.MinutesPerHour) return false;
            if (ss < 0 || ss >= spec.SecondsPerMinute) return false;
            if (subticks < 0 || subticks >= spec.TicksPerSecond) return false;

            long ticks = days * spec.TicksPerDay
                       + hh * spec.TicksPerHour
                       + mm * spec.TicksPerMinute
                       + ss * spec.TicksPerSecond
                       + subticks;
            span = new WTimeSpan(sign * ticks);
            return true;
        }

        public override WTimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Akceptujeme číslo (ticks) nebo string ve formátu "t" (číslo), případně "g" (d.hh:mm:ss[.sub]).
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out long ticks)) return new WTimeSpan(ticks);
                    throw new JsonException("WTimeSpan: neočekávaný číselný formát.");
                case JsonTokenType.String:
                    {
                        var s = reader.GetString();
                        if (string.IsNullOrWhiteSpace(s)) throw new JsonException("WTimeSpan: prázdný řetězec.");

                        // Zkus čisté číslo (ticks)
                        if (long.TryParse(s, out var asTicks)) return new WTimeSpan(asTicks);

                        // Zkus "d.hh:mm:ss[.sub]" nebo "hh:mm:ss[.sub]"
                        if (TryParseGeneral(s!, out var span)) return span;

                        throw new JsonException($"WTimeSpan: neplatný formát '{s}'.");
                    }
                default:
                    throw new JsonException("WTimeSpan: očekáván number nebo string.");
            }
        }

        public override void Write(Utf8JsonWriter writer, WTimeSpan value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value.Ticks);
    }
}
