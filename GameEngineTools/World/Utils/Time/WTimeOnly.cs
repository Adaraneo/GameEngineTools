namespace GameEngineTools.World.Utils.Time
{
    /// <summary>Čas dne bez data. 0..TicksPerDay-1. Bez BCL TimeOnly.</summary>
    public readonly struct WTimeOnly :
        IEquatable<WTimeOnly>, IComparable<WTimeOnly>, IFormattable
    {
        public WTimeOnly(long ticksOfDay)
        {
            long tpd = WDateTime.Spec.TicksPerDay;
            if ((uint)ticksOfDay >= (uint)tpd) throw new ArgumentOutOfRangeException(nameof(ticksOfDay));
            TicksOfDay = ticksOfDay;
        }

        // Komponenty
        public int Hour
        {
            get
            {
                var s = WDateTime.Spec;
                return (int)(TicksOfDay / s.TicksPerHour);
            }
        }

        public int Millisecond => (int)((SubTick * 1000L) / WDateTime.Spec.TicksPerSecond);

        public int Minute
        {
            get
            {
                var s = WDateTime.Spec;
                return (int)((TicksOfDay % s.TicksPerHour) / s.TicksPerMinute);
            }
        }

        public int Second
        {
            get
            {
                var s = WDateTime.Spec;
                return (int)((TicksOfDay % s.TicksPerMinute) / s.TicksPerSecond);
            }
        }

        public long SubTick => TicksOfDay % WDateTime.Spec.TicksPerSecond;
        public long TicksOfDay { get; } // [0, TicksPerDay)

        public static WTimeOnly FromParts(int hour, int minute, int second, long subTick = 0)
        {
            var s = WDateTime.Spec;
            if (hour < 0 || hour >= s.HoursPerDay) throw new ArgumentOutOfRangeException(nameof(hour));
            if (minute < 0 || minute >= s.MinutesPerHour) throw new ArgumentOutOfRangeException(nameof(minute));
            if (second < 0 || second >= s.SecondsPerMinute) throw new ArgumentOutOfRangeException(nameof(second));
            if (subTick < 0 || subTick >= s.TicksPerSecond) throw new ArgumentOutOfRangeException(nameof(subTick));

            long ticks = hour * s.TicksPerHour
                       + minute * s.TicksPerMinute
                       + second * s.TicksPerSecond
                       + subTick;
            return new WTimeOnly(ticks);
        }

        public static bool operator !=(WTimeOnly a, WTimeOnly b) => !a.Equals(b);

        public static bool operator <(WTimeOnly a, WTimeOnly b) => a.TicksOfDay < b.TicksOfDay;

        public static bool operator <=(WTimeOnly a, WTimeOnly b) => a.TicksOfDay <= b.TicksOfDay;

        public static bool operator ==(WTimeOnly a, WTimeOnly b) => a.Equals(b);

        public static bool operator >(WTimeOnly a, WTimeOnly b) => a.TicksOfDay > b.TicksOfDay;

        public static bool operator >=(WTimeOnly a, WTimeOnly b) => a.TicksOfDay >= b.TicksOfDay;

        public static WTimeOnly Parse(string text)
                    => TryParse(text, out var v) ? v : throw new FormatException($"Neplatný WTimeOnly: '{text}'.");

        public static bool TryParse(string? text, out WTimeOnly value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.AsSpan().Trim();

            var dot = s.IndexOf('.');
            ReadOnlySpan<char> main = dot >= 0 ? s[..dot] : s;
            ReadOnlySpan<char> frac = dot >= 0 ? s[(dot + 1)..] : ReadOnlySpan<char>.Empty;

            if (main.Length < 8 || main[2] != ':' || main[5] != ':') return false;

            if (!TryInt(main[..2], 0, WDateTime.Spec.HoursPerDay - 1, out int hh)) return false;
            if (!TryInt(main.Slice(3, 2), 0, WDateTime.Spec.MinutesPerHour - 1, out int mm)) return false;
            if (!TryInt(main.Slice(6, 2), 0, WDateTime.Spec.SecondsPerMinute - 1, out int ss)) return false;

            long sub = 0;
            if (!frac.IsEmpty)
            {
                // bereme subticky v surové podobě (round-trip s ToString)
                for (int i = 0; i < frac.Length; i++)
                {
                    char c = frac[i]; if (c < '0' || c > '9') return false;
                }
                if (!TryInt64(frac, 0, WDateTime.Spec.TicksPerSecond - 1, out sub)) return false;
            }

            try { value = FromParts(hh, mm, ss, sub); return true; }
            catch { return false; }

            static bool TryInt(ReadOnlySpan<char> sp, int min, int max, out int v)
            {
                long acc = 0;
                for (int i = 0; i < sp.Length; i++)
                {
                    char c = sp[i]; if (c < '0' || c > '9') { v = 0; return false; }
                    acc = acc * 10 + (c - '0'); if (acc > int.MaxValue) { v = 0; return false; }
                }
                v = (int)acc; return v >= min && v <= max;
            }
            static bool TryInt64(ReadOnlySpan<char> sp, long min, long max, out long v)
            {
                long acc = 0;
                for (int i = 0; i < sp.Length; i++)
                {
                    char c = sp[i]; if (c < '0' || c > '9') { v = 0; return false; }
                    long d = c - '0';
                    if (acc > (long.MaxValue - d) / 10) { v = 0; return false; }
                    acc = acc * 10 + d;
                }
                v = acc; return v >= min && v <= max;
            }
        }

        // Aritmetika (wrap přes den)
        public WTimeOnly Add(WTimeSpan span)
        {
            long t = TicksOfDay + span.Ticks;
            long tpd = WDateTime.Spec.TicksPerDay;
            t %= tpd; if (t < 0) t += tpd;
            return new WTimeOnly(t);
        }

        public WTimeOnly AddHours(double h) => Add(WTimeSpan.FromHours(h));

        public WTimeOnly AddMinutes(double m) => Add(WTimeSpan.FromMinutes(m));

        public WTimeOnly AddSeconds(double s) => Add(WTimeSpan.FromSeconds(s));

        // Porovnání
        public int CompareTo(WTimeOnly other) => TicksOfDay.CompareTo(other.TicksOfDay);

        /// <summary>Hrubý rozdíl (bez wrapu): this - other, v tickách dne.</summary>
        public WTimeSpan Diff(WTimeOnly other) => new WTimeSpan(this.TicksOfDay - other.TicksOfDay);

        public bool Equals(WTimeOnly other) => TicksOfDay == other.TicksOfDay;

        public override bool Equals(object? obj) => obj is WTimeOnly t && Equals(t);

        public override int GetHashCode() => TicksOfDay.GetHashCode();

        // Formátování/parsování: "HH:MM:SS[.sub]" nebo ".sub" vynechej
        public override string ToString() => ToString("O", null);

        public string ToString(string? format, IFormatProvider? _)
        {
            var s = WDateTime.Spec;
            long rem = TicksOfDay;
            int hh = (int)(rem / s.TicksPerHour); rem -= (long)hh * s.TicksPerHour;
            int mm = (int)(rem / s.TicksPerMinute); rem -= (long)mm * s.TicksPerMinute;
            int ss = (int)(rem / s.TicksPerSecond); rem -= (long)ss * s.TicksPerSecond;
            if (rem != 0) return $"{hh:00}:{mm:00}:{ss:00}.{rem}";
            return $"{hh:00}:{mm:00}:{ss:00}";
        }
    }
}