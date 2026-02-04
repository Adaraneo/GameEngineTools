namespace GameEngineTools.World.Utils.Time
{
    /// <summary>Datum bez času. 0 = 1/1/1 (světová epocha). Bez BCL DateOnly.</summary>
    public readonly struct WDateOnly :
        IEquatable<WDateOnly>, IComparable<WDateOnly>, IFormattable
    {
        public WDateOnly(long dayIndex)
        {
            if (dayIndex < 0) throw new ArgumentOutOfRangeException(nameof(dayIndex));
            DayIndex = dayIndex;
        }

        public int Day { get { Deconstruct(out _, out _, out var d); return d; } }
        public long DayIndex { get; } // 0-based dny od světové epochy
        public int Month { get { Deconstruct(out _, out var m, out _); return m; } }

        public int Year { get { Deconstruct(out var y, out _, out _); return y; } }

        public static WDateOnly FromDateTime(WDateTime dt) => dt.DateOnly;

        public static WDateOnly FromParts(int year, int month, int day)
        {
            var di = WDateTime.Spec.Calendar.DaysFromDate(year, month, day);
            return new WDateOnly(di);
        }

        public static bool operator !=(WDateOnly a, WDateOnly b) => !a.Equals(b);

        public static bool operator <(WDateOnly a, WDateOnly b) => a.DayIndex < b.DayIndex;

        public static bool operator <=(WDateOnly a, WDateOnly b) => a.DayIndex <= b.DayIndex;

        public static bool operator ==(WDateOnly a, WDateOnly b) => a.Equals(b);

        public static bool operator >(WDateOnly a, WDateOnly b) => a.DayIndex > b.DayIndex;

        public static bool operator >=(WDateOnly a, WDateOnly b) => a.DayIndex >= b.DayIndex;

        public static WDateOnly Parse(string text)
                    => TryParse(text, out var v) ? v : throw new FormatException($"Neplatný WDateOnly: '{text}'.");

        public static bool TryParse(string? text, out WDateOnly value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.AsSpan().Trim();
            if (s.Length < 10 || s[4] != '-' || s[7] != '-') return false;

            if (!TryInt(s[..4], 1, int.MaxValue, out int y)) return false;
            if (!TryInt(s.Slice(5, 2), 1, 99, out int mo)) return false;
            if (!TryInt(s.Slice(8, 2), 1, 99, out int da)) return false;

            try { value = FromParts(y, mo, da); return true; }
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
        }

        // Aritmetika
        public WDateOnly AddDays(long days) => new WDateOnly(checked(DayIndex + days));

        public WDateOnly AddMonths(int months)
        {
            Deconstruct(out var y, out var m, out var d);
            m += months;
            while (m < 1) { m += 12; y -= 1; }
            while (m > 12) { m -= 12; y += 1; }
            var daysInMonth = WDateTime.Spec.Calendar.DaysInMonth(y, m);
            if (d > daysInMonth) d = daysInMonth;
            return FromParts(y, m, d);
        }

        public WDateOnly AddYears(int years)
        {
            Deconstruct(out var y, out var m, out var d);
            y += years;
            var daysInMonth = WDateTime.Spec.Calendar.DaysInMonth(y, m);
            if (d > daysInMonth) d = daysInMonth;
            return FromParts(y, m, d);
        }

        public WDateTime At(WTimeOnly time) =>
                    new WDateTime(checked(DayIndex * WDateTime.Spec.TicksPerDay + time.TicksOfDay));

        // Převody
        public WDateTime AtStartOfDay() => new WDateTime(checked(DayIndex * WDateTime.Spec.TicksPerDay));

        // Porovnání
        public int CompareTo(WDateOnly other) => DayIndex.CompareTo(other.DayIndex);

        public long DaysUntil(WDateOnly other) => other.DayIndex - DayIndex;

        // Komponenty
        public void Deconstruct(out int year, out int month, out int day)
            => (year, month, day) = WDateTime.Spec.Calendar.DateFromDays(DayIndex);

        public bool Equals(WDateOnly other) => DayIndex == other.DayIndex;

        public override bool Equals(object? obj) => obj is WDateOnly d && Equals(d);

        public override int GetHashCode() => DayIndex.GetHashCode();

        // Formátování/parsování: "YYYY-MM-DD"
        public override string ToString() => ToString("O", null);

        public string ToString(string? format, IFormatProvider? _)
        {
            var (y, m, d) = WDateTime.Spec.Calendar.DateFromDays(DayIndex);
            return $"{y:0000}-{m:00}-{d:00}";
        }
    }
}
