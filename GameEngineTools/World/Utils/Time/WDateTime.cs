// WDateTime.cs
// Copyright (c) 50PSoftware

using System;
using System.Text.Json.Serialization;
using GameEngineTools.World.Core.Calendars;
using GameEngineTools.World.Core.Time;

namespace GameEngineTools.World.Utils.Time
{
    /// <summary>
    /// Okamžik v herním světě, uložený jako počet worldTicků od světové epochy 0001-01-01T00:00:00.
    /// Jednotky i kalendář jsou řízené přes <see cref="WorldTimeSpec"/> (např. 26h den, vlastní rok atd.).
    /// Bez přímé závislosti na BCL DateTime.
    /// </summary>
    [JsonConverter(typeof(WDateTimeJsonConverter))]
    public readonly struct WDateTime :
        IEquatable<WDateTime>, IComparable<WDateTime>, IFormattable
    {
        // --- Spec & Clock ----------------------------------------------------

        private static WorldTimeSpec DefaultSpec()
        {
            // Příklad “herního” světa: 1 světová sekunda = 1 reálná sekunda, den = 26 h,
            // rok = 10×36 + přestup 5 dní / 4. rok.
            var cal = new FixedMonthsCalendar(
                new[] { 36, 36, 36, 36, 36, 36, 36, 36, 36, 36 },
                y => (y % 4 == 0) ? 5 : 0
            );

            return new WorldTimeSpec(
                ticksPerSecond: 10_000_000, // 1 worldTick = 100 ns (čistě interní jednotka)
                secondsPerMinute: 60,
                minutesPerHour: 60,
                hoursPerDay: 26,
                calendar: cal
            );
        }

        public static WorldTimeSpec Spec { get; private set; } = DefaultSpec();

        /// <summary>Volitelné napojení na reálný čas.</summary>
        public static WorldClock? Clock { get; private set; }

        public static void Use(WorldTimeSpec spec) => Spec = spec;

        public static void UseClock(WorldClock clock) => Clock = clock;

        // --- Konstrukce ------------------------------------------------------

        public WDateTime(long worldTicks) => WorldTicks = worldTicks;

        /// <summary>
        /// Vytvoří okamžik ze světových částí (year, month, day, hour, minute, second).
        /// Validace probíhá podle aktuálního <see cref="Spec"/> a jeho kalendáře.
        /// </summary>
        public WDateTime(int year, int month, int day, int hour, int minute, int second)
            : this(FromParts(year, month, day, hour, minute, second).WorldTicks)
        { }

        private static void ValidateYMDHMS(int year, int month, int day, int hour, int minute, int second)
        {
            if (year < 1 || month < 1 || day < 1)
                throw new ArgumentOutOfRangeException();

            if (hour < 0 || hour >= Spec.HoursPerDay)
                throw new ArgumentOutOfRangeException(nameof(hour));
            if (minute < 0 || minute >= Spec.MinutesPerHour)
                throw new ArgumentOutOfRangeException(nameof(minute));
            if (second < 0 || second >= Spec.SecondsPerMinute)
                throw new ArgumentOutOfRangeException(nameof(second));

            if (day > Spec.Calendar.DaysInMonth(year, month))
                throw new ArgumentOutOfRangeException(nameof(day));
        }

        // --- Základní statické členy -----------------------------------------

        public static WDateTime MinValue => new WDateTime(0);

        /// <summary>
        /// Maximální hodnota zarovnaná na začátek dne, aby se vešla do long.MaxValue worldTicků.
        /// </summary>
        public static WDateTime MaxValue
        {
            get
            {
                long fullDays = long.MaxValue / Spec.TicksPerDay;
                long ticksAt00 = fullDays * Spec.TicksPerDay;
                return new WDateTime(ticksAt00);
            }
        }

        /// <summary>Aktuální okamžik podle připojeného <see cref="Clock"/>.</summary>
        public static WDateTime Now
            => Clock is null
                ? throw new InvalidOperationException("WorldClock is not set.")
                : new WDateTime(Clock.NowWorldTicks());

        // --- Scalar uložená hodnota ------------------------------------------

        /// <summary>
        /// Počet worldTicků od světové epochy (0001-01-01T00:00:00).
        /// Je to jediný zdroj pravdy; veškeré další vlastnosti se z něj dopočítávají.
        /// </summary>
        public long WorldTicks { get; }

        // --- Datum/časové komponenty -----------------------------------------

        /// <summary>Datum zarovnané na začátek dne (00:00:00).</summary>
        public WDateTime Date
        {
            get
            {
                long days = Math.DivRem(WorldTicks, Spec.TicksPerDay, out long _);
                return new WDateTime(days * Spec.TicksPerDay);
            }
        }

        /// <summary>Datum bez času (index dne od světové epochy).</summary>
        public WDateOnly DateOnly
        {
            get
            {
                long dayIndex = Math.DivRem(WorldTicks, Spec.TicksPerDay, out _);
                return new WDateOnly(dayIndex);
            }
        }

        public int Year
        {
            get { Deconstruct(out var y, out _, out _, out _, out _, out _, out _); return y; }
        }

        public int Month
        {
            get { Deconstruct(out _, out var m, out _, out _, out _, out _, out _); return m; }
        }

        public int Day
        {
            get { Deconstruct(out _, out _, out var d, out _, out _, out _, out _); return d; }
        }

        /// <summary>
        /// Den v roce (1-based). Výpočet jde přes kalendář: rozdíl between dnem v roce a prvním dnem roku.
        /// </summary>
        public int DayOfYear
        {
            get
            {
                long dayIndex = Math.DivRem(WorldTicks, Spec.TicksPerDay, out _);
                var (year, _, _) = Spec.Calendar.DateFromDays(dayIndex);
                long firstDayOfYear = Spec.Calendar.DaysFromDate(year, 1, 1);
                return checked((int)(dayIndex - firstDayOfYear + 1));
            }
        }

        public int Hour
        {
            get { Deconstruct(out _, out _, out _, out var h, out _, out _, out _); return h; }
        }

        public int Minute
        {
            get { Deconstruct(out _, out _, out _, out _, out var m, out _, out _); return m; }
        }

        public int Second
        {
            get { Deconstruct(out _, out _, out _, out _, out _, out var s, out _); return s; }
        }

        /// <summary>
        /// Millisecond v rámci světové sekundy (0..999), dopočítaný z podticků.
        /// </summary>
        public int Millisecond
        {
            get
            {
                Deconstruct(out _, out _, out _, out _, out _, out _, out var subTick);
                return (int)((subTick * 1000L) / Spec.TicksPerSecond);
            }
        }

        /// <summary>
        /// Čas dne jako interval od začátku dne (0..TicksPerDay-1).
        /// </summary>
        public WTimeSpan TimeOfDay
        {
            get
            {
                long rem = WorldTicks % Spec.TicksPerDay;
                if (rem < 0) rem += Spec.TicksPerDay;
                return new WTimeSpan(rem);
            }
        }

        /// <summary>Čas dne jako <see cref="WTimeOnly"/>.</summary>
        public WTimeOnly TimeOnly
        {
            get
            {
                Math.DivRem(WorldTicks, Spec.TicksPerDay, out long rem);
                if (rem < 0) rem += Spec.TicksPerDay;
                return new WTimeOnly(rem);
            }
        }

        // --- Factory metody --------------------------------------------------

        public static WDateTime From(WDateOnly date)
            => new WDateTime(checked(date.DayIndex * Spec.TicksPerDay));

        public static WDateTime From(WDateOnly date, WTimeOnly time)
            => new WDateTime(checked(date.DayIndex * Spec.TicksPerDay + time.TicksOfDay));

        public static WDateTime FromParts(
            int year, int month, int day,
            int hour = 0, int minute = 0, int second = 0, long subTick = 0)
        {
            ValidateYMDHMS(year, month, day, hour, minute, second);

            long days = Spec.Calendar.DaysFromDate(year, month, day);
            long ticks = days * Spec.TicksPerDay
                       + hour * Spec.TicksPerHour
                       + minute * Spec.TicksPerMinute
                       + second * Spec.TicksPerSecond
                       + subTick;

            return new WDateTime(ticks);
        }

        // --- Aritmetika / porovnání ------------------------------------------

        public static WTimeSpan Difference(WDateTime a, WDateTime b) => new(a.WorldTicks - b.WorldTicks);

        // date ± span / ± ticks
        public static WDateTime operator +(WDateTime t, WTimeSpan d) => new(t.WorldTicks + d.Ticks);
        public static WDateTime operator +(WTimeSpan d, WDateTime t) => new(t.WorldTicks + d.Ticks);
        public static WDateTime operator +(WDateTime t, long ticks) => new(t.WorldTicks + ticks);
        public static WDateTime operator +(long ticks, WDateTime t) => new(t.WorldTicks + ticks);

        public static WDateTime operator -(WDateTime t, WTimeSpan d) => new(t.WorldTicks - d.Ticks);
        public static WDateTime operator -(WDateTime t, long ticks) => new(t.WorldTicks - ticks);
        public static WTimeSpan operator -(WDateTime a, WDateTime b) => new(a.WorldTicks - b.WorldTicks);

        // inkrement/dekrement po 1 worldTicku
        public static WDateTime operator ++(WDateTime a) => new(a.WorldTicks + 1);
        public static WDateTime operator --(WDateTime a) => new(a.WorldTicks - 1);

        // relační operátory
        public static bool operator ==(WDateTime left, WDateTime right) => left.WorldTicks == right.WorldTicks;
        public static bool operator !=(WDateTime left, WDateTime right) => left.WorldTicks != right.WorldTicks;
        public static bool operator <(WDateTime left, WDateTime right) => left.WorldTicks < right.WorldTicks;
        public static bool operator <=(WDateTime left, WDateTime right) => left.WorldTicks <= right.WorldTicks;
        public static bool operator >(WDateTime left, WDateTime right) => left.WorldTicks > right.WorldTicks;
        public static bool operator >=(WDateTime left, WDateTime right) => left.WorldTicks >= right.WorldTicks;

        public static WDateTime Min(WDateTime a, WDateTime b) => a.WorldTicks <= b.WorldTicks ? a : b;
        public static WDateTime Max(WDateTime a, WDateTime b) => a.WorldTicks >= b.WorldTicks ? a : b;

        public WDateTime Add(WTimeSpan span) => new(WorldTicks + span.Ticks);
        public WDateTime AddTicks(long ticks) => new(WorldTicks + ticks);

        public WDateTime AddDays(long d) => new(WorldTicks + d * Spec.TicksPerDay);
        public WDateTime AddDays(double d) => this + WTimeSpan.FromDays(d);

        public WDateTime AddHours(long h) => new(WorldTicks + h * Spec.TicksPerHour);
        public WDateTime AddHours(double h) => this + WTimeSpan.FromHours(h);

        public WDateTime AddMinutes(long m) => new(WorldTicks + m * Spec.TicksPerMinute);
        public WDateTime AddMinutes(double m) => this + WTimeSpan.FromMinutes(m);

        public WDateTime AddSeconds(long s) => new(WorldTicks + s * Spec.TicksPerSecond);
        public WDateTime AddSeconds(double s) => this + WTimeSpan.FromSeconds(s);

        public WDateTime Clamp(in WDateTime min, in WDateTime max)
        {
            if (min.WorldTicks > max.WorldTicks) throw new ArgumentException("min > max");
            if (WorldTicks < min.WorldTicks) return min;
            if (WorldTicks > max.WorldTicks) return max;
            return this;
        }

        public int CompareTo(WDateTime other) => WorldTicks.CompareTo(other.WorldTicks);

        public WTimeSpan Diff(WDateTime other) => new(WorldTicks - other.WorldTicks);

        public bool Equals(WDateTime other) => WorldTicks == other.WorldTicks;

        public override bool Equals(object? obj) => obj is WDateTime d && Equals(d);

        public override int GetHashCode() => WorldTicks.GetHashCode();

        // --- Deconstruct -----------------------------------------------------

        public void Deconstruct(
            out int year, out int month, out int day,
            out int hour, out int minute, out int second, out long subTick)
        {
            long dayIndex = Math.DivRem(WorldTicks, Spec.TicksPerDay, out long rest);
            (year, month, day) = Spec.Calendar.DateFromDays(dayIndex);

            hour = (int)(rest / Spec.TicksPerHour); rest -= hour * Spec.TicksPerHour;
            minute = (int)(rest / Spec.TicksPerMinute); rest -= minute * Spec.TicksPerMinute;
            second = (int)(rest / Spec.TicksPerSecond); rest -= second * Spec.TicksPerSecond;
            subTick = rest;
        }

        // --- Parsing / ToString ----------------------------------------------

        public static bool TryParse(ReadOnlySpan<char> input, out WDateTime value)
        {
            value = default;

            // trim bez alokací
            int start = 0, end = input.Length;
            while (start < end && char.IsWhiteSpace(input[start])) start++;
            while (end > start && char.IsWhiteSpace(input[end - 1])) end--;
            var s = input.Slice(start, end - start);
            if (s.Length == 0) return false;

            // volitelné koncové 'Z'/'z' ignorujeme (aby se to nepletlo s UTC na Zemi)
            if (s.Length > 0)
            {
                char last = s[^1];
                if (last == 'Z' || last == 'z') s = s[..^1];
            }

            // rozdělíme na datum a čas podle 'T' nebo mezery
            int iT = s.IndexOf('T');
            int iSP = s.IndexOf(' ');
            int sep = (iT >= 0 && iSP >= 0) ? Math.Min(iT, iSP) : Math.Max(iT, iSP);

            ReadOnlySpan<char> datePart = s;
            ReadOnlySpan<char> timePart = ReadOnlySpan<char>.Empty;
            bool hasTime = sep >= 0;
            if (hasTime)
            {
                datePart = s[..sep];
                timePart = s[(sep + 1)..];
                // ořízni whitespace
                int ts = 0, te = timePart.Length;
                while (ts < te && char.IsWhiteSpace(timePart[ts])) ts++;
                while (te > ts && char.IsWhiteSpace(timePart[te - 1])) te--;
                timePart = timePart.Slice(ts, te - ts);
                if (timePart.Length == 0) hasTime = false;
            }

            // DATE: "YYYY-MM-DD"
            if (datePart.Length < 10 || datePart[4] != '-' || datePart[7] != '-')
                return false;

            if (!TryParseInt(datePart[..4], 1, int.MaxValue, out int year)) return false;
            if (!TryParseInt(datePart.Slice(5, 2), 1, 99, out int month)) return false;
            if (!TryParseInt(datePart.Slice(8, 2), 1, 99, out int day)) return false;

            // TIME: "HH:MM:SS[.frac]" nebo "HH:MM:SS[.subticks]W"
            int hour = 0, minute = 0, second = 0;
            long subTick = 0;

            if (hasTime)
            {
                ReadOnlySpan<char> main = timePart;
                ReadOnlySpan<char> frac = ReadOnlySpan<char>.Empty;

                int dot = timePart.IndexOf('.');
                if (dot >= 0)
                {
                    main = timePart[..dot];
                    frac = timePart[(dot + 1)..];
                }

                if (main.Length < 8 || main[2] != ':' || main[5] != ':')
                    return false;

                if (!TryParseInt(main[..2], 0, Spec.HoursPerDay - 1, out hour)) return false;
                if (!TryParseInt(main.Slice(3, 2), 0, Spec.MinutesPerHour - 1, out minute)) return false;
                if (!TryParseInt(main.Slice(6, 2), 0, Spec.SecondsPerMinute - 1, out second)) return false;

                if (!frac.IsEmpty)
                {
                    bool rawTicks = false;
                    if (frac[^1] == 'W')
                    {
                        rawTicks = true;
                        frac = frac[..^1];
                    }

                    if (frac.Length == 0) return false;

                    // jen číslice
                    for (int i = 0; i < frac.Length; i++)
                    {
                        char c = frac[i];
                        if (c < '0' || c > '9') return false;
                    }

                    if (rawTicks)
                    {
                        if (!TryParseInt64(frac, 0, Spec.TicksPerSecond - 1, out subTick)) return false;
                    }
                    else
                    {
                        int n = Math.Min(frac.Length, 18);
                        if (!TryParseInt64(frac[..n], 0, long.MaxValue, out long fracVal)) return false;
                        long pow10 = Pow10(n);

                        subTick = (fracVal * Spec.TicksPerSecond) / pow10;
                        if (subTick >= Spec.TicksPerSecond) subTick = Spec.TicksPerSecond - 1;
                    }
                }
            }

            long days;
            try
            {
                days = Spec.Calendar.DaysFromDate(year, month, day);
            }
            catch
            {
                return false;
            }

            long ticks = days * Spec.TicksPerDay
                       + hour * Spec.TicksPerHour
                       + minute * Spec.TicksPerMinute
                       + second * Spec.TicksPerSecond
                       + subTick;

            value = new WDateTime(ticks);
            return true;

            static bool TryParseInt(ReadOnlySpan<char> span, int min, int max, out int val)
            {
                long acc = 0;
                for (int i = 0; i < span.Length; i++)
                {
                    int d = span[i] - '0';
                    if ((uint)d > 9U) { val = 0; return false; }
                    acc = acc * 10 + d;
                    if (acc > int.MaxValue) { val = 0; return false; }
                }
                val = (int)acc;
                return val >= min && val <= max;
            }

            static bool TryParseInt64(ReadOnlySpan<char> span, long min, long max, out long val)
            {
                long acc = 0;
                for (int i = 0; i < span.Length; i++)
                {
                    int d = span[i] - '0';
                    if ((uint)d > 9U) { val = 0; return false; }
                    if (acc > (long.MaxValue - d) / 10) { val = 0; return false; }
                    acc = acc * 10 + d;
                }
                val = acc;
                return val >= min && val <= max;
            }

            static long Pow10(int n)
            {
                long p = 1;
                for (int i = 0; i < n; i++) p *= 10;
                return p;
            }
        }

        /// <summary>Convenience wrapper nad <see cref="TryParse(ReadOnlySpan{char}, out WDateTime)"/>.</summary>
        public static bool TryParse(string? text, out WDateTime value)
            => string.IsNullOrWhiteSpace(text)
                ? (value = default, false).Item2
                : TryParse(text.AsSpan(), out value);

        public override string ToString() => ToString("O", null);

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            // "O": YYYY-MM-DDTHH:MM:SS[.subW]
            Deconstruct(out var y, out var m, out var d, out var hh, out var mm, out var ss, out var sub);

            var sb = new System.Text.StringBuilder(32);
            Append4(sb, y); sb.Append('-'); Append2(sb, m); sb.Append('-'); Append2(sb, d);
            sb.Append('T'); Append2(sb, hh); sb.Append(':'); Append2(sb, mm); sb.Append(':'); Append2(sb, ss);

            if (sub != 0)
            {
                sb.Append('.');
                sb.Append(sub.ToString()); // raw subticky; round-trip s "W"
                sb.Append('W');
            }

            return sb.ToString();

            static void Append2(System.Text.StringBuilder b, int v)
            { b.Append((char)('0' + v / 10)); b.Append((char)('0' + v % 10)); }

            static void Append4(System.Text.StringBuilder b, int v)
            {
                b.Append((char)('0' + (v / 1000) % 10));
                b.Append((char)('0' + (v / 100) % 10));
                b.Append((char)('0' + (v / 10) % 10));
                b.Append((char)('0' + v % 10));
            }
        }

        // --- Withery ---------------------------------------------------------

        public WDateTime WithDate(WDateOnly date) => From(date, this.TimeOnly);

        public WDateTime WithTime(WTimeOnly time) => From(this.DateOnly, time);
    }
}
