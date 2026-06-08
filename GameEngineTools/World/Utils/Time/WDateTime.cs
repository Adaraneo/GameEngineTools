// WDateTime.cs
// Copyright (c) 50PSoftware

using System.Text;
using System.Text.Json.Serialization;
using GameEngineTools.World.Core.Time;

namespace GameEngineTools.World.Utils.Time
{
    /// <summary>
    /// Represents a specific instant in the game world, stored as the number of world ticks
    /// since the world epoch (day 1 of month 1 of year 1, 00:00:00).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ambient-spec design.</b> Behaves like <see cref="DateTime"/> — the properties
    /// <see cref="Year"/>, <see cref="Month"/>, <see cref="Day"/> etc. work directly
    /// on the value without passing a context. Requires <see cref="WWorld"/> to be configured.
    /// Pure math (operators, comparisons) does not require <see cref="WWorld"/>.
    /// </para>
    /// <para>
    /// Examples:
    /// <code>
    /// // Current time
    /// var now = WDateTime.Now;
    ///
    /// // Creation
    /// var dt = WDateTime.New(1322, 7, 4, hour: 6);
    ///
    /// // Ambient properties
    /// int year  = dt.Year;    // 1322
    /// int month = dt.Month;   // 7
    /// int day   = dt.Day;     // 4
    /// int hour  = dt.Hour;    // 6
    ///
    /// // Components as types
    /// WDateOnly date = dt.Date;
    /// WTimeOnly time = dt.TimeOfDay;
    ///
    /// // Pure math — without WWorld
    /// var later = dt + WTimeSpan.FromHours(2);
    /// var diff  = later - dt;
    /// bool past = dt &lt; WDateTime.Now;
    ///
    /// // Calendar arithmetic
    /// var nextMonth = dt.AddMonths(1);
    /// var nextYear  = dt.AddYears(1);
    ///
    /// // Formatting
    /// string s = dt.ToString();  // "1322-07-04T06:00:00"
    /// </code>
    /// </para>
    /// </remarks>
    [JsonConverter(typeof(WDateTimeJsonConverter))]
    public readonly struct WDateTime :
        IEquatable<WDateTime>, IComparable<WDateTime>
    {
        #region Konstrukce

        /// <summary>
        /// Initializes a new instant from an exact number of world ticks since the world epoch.
        /// </summary>
        /// <param name="worldTicks">
        /// Number of world ticks since the world epoch (0 = 1/1/1 00:00:00).
        /// A negative value would represent a time before the epoch — not supported.
        /// </param>
        public WDateTime(long worldTicks) => WorldTicks = worldTicks;

        #endregion Konstrukce

        #region Vlastnosti — raw data

        /// <summary>
        /// Number of world ticks since the world epoch (1/1/1 00:00:00).
        /// The single source of truth.
        /// </summary>
        public long WorldTicks { get; }

        #endregion Vlastnosti — raw data

        #region Konstanty

        /// <summary>Minimum representable value — the world epoch (1/1/1 00:00:00).</summary>
        public static WDateTime MinValue => new(0);

        #endregion Konstanty

        #region Ambient vlastnosti — složky data (vyžadují WWorld.Configure)

        /// <summary>Year of this instant.</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public int Year
        {
            get
            {
                var spec = WWorld.Spec;
                var (y, _, _) = spec.Calendar.DateFromDays(WorldTicks / spec.TicksPerDay);
                return y;
            }
        }

        /// <summary>Month of this instant (1-based).</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public int Month
        {
            get
            {
                var spec = WWorld.Spec;
                var (_, m, _) = spec.Calendar.DateFromDays(WorldTicks / spec.TicksPerDay);
                return m;
            }
        }

        /// <summary>Day of month of this instant (1-based).</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public int Day
        {
            get
            {
                var spec = WWorld.Spec;
                var (_, _, d) = spec.Calendar.DateFromDays(WorldTicks / spec.TicksPerDay);
                return d;
            }
        }

        /// <summary>Hour of this instant (0-based).</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public int Hour
        {
            get
            {
                var spec = WWorld.Spec;
                long rem = WorldTicks % spec.TicksPerDay;
                return (int)(rem / spec.TicksPerHour);
            }
        }

        /// <summary>Minute of this instant (0-based).</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public int Minute
        {
            get
            {
                var spec = WWorld.Spec;
                long rem = WorldTicks % spec.TicksPerHour;
                return (int)(rem / spec.TicksPerMinute);
            }
        }

        /// <summary>Second of this instant (0-based).</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public int Second
        {
            get
            {
                var spec = WWorld.Spec;
                long rem = WorldTicks % spec.TicksPerMinute;
                return (int)(rem / spec.TicksPerSecond);
            }
        }

        /// <summary>
        /// The date component of this instant as a <see cref="WDateOnly"/>.
        /// The time component is discarded.
        /// </summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public WDateOnly Date => new(WorldTicks / WWorld.Spec.TicksPerDay);

        /// <summary>
        /// The time component of this instant as a <see cref="WTimeOnly"/>.
        /// The date component is discarded.
        /// </summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public WTimeOnly TimeOfDay
        {
            get
            {
                var spec = WWorld.Spec;
                long rem = WorldTicks % spec.TicksPerDay;
                if (rem < 0) rem += spec.TicksPerDay;
                return new WTimeOnly(rem);
            }
        }

        /// <summary>
        /// Day of the year (1-based). Computed through the calendar.
        /// </summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public int DayOfYear
        {
            get
            {
                var spec = WWorld.Spec;
                long dayIndex = WorldTicks / spec.TicksPerDay;
                var (y, _, _) = spec.Calendar.DateFromDays(dayIndex);
                long firstDay = spec.Calendar.DaysFromDate(y, 1, 1);
                return (int)(dayIndex - firstDay) + 1;
            }
        }

        /// <summary>
        /// Maximum safely representable value — the largest multiple of TicksPerDay within a long.
        /// </summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public static WDateTime MaxValue
        {
            get
            {
                long fullDays = long.MaxValue / WWorld.Spec.TicksPerDay;
                return new WDateTime(fullDays * WWorld.Spec.TicksPerDay);
            }
        }

        #endregion Ambient vlastnosti — složky data (vyžadují WWorld.Configure)

        #region Static factory a aktuální čas

        /// <summary>
        /// Current game time from <see cref="WWorld.Clock"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public static WDateTime Now => WWorld.Clock.Now;

        /// <summary>
        /// Creates an instant from all time components.
        /// Validates each component against <see cref="WWorld.Spec"/> and its calendar.
        /// </summary>
        /// <param name="year">Rok (≥ 1).</param>
        /// <param name="month">Month (1-based).</param>
        /// <param name="day">Day of month (1-based).</param>
        /// <param name="hour">Hour (0..HoursPerDay-1). Default 0.</param>
        /// <param name="minute">Minute (0..MinutesPerHour-1). Default 0.</param>
        /// <param name="second">Second (0..SecondsPerMinute-1). Default 0.</param>
        /// <param name="subTick">Sub-ticks (0..TicksPerSecond-1). Default 0.</param>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        /// <exception cref="ArgumentOutOfRangeException">If the components exceed the valid range.</exception>
        public static WDateTime New(
            int year, int month, int day,
            int hour = 0, int minute = 0, int second = 0, long subTick = 0)
        {
            var spec = WWorld.Spec;

            if (hour < 0 || hour >= spec.HoursPerDay) throw new ArgumentOutOfRangeException(nameof(hour));
            if (minute < 0 || minute >= spec.MinutesPerHour) throw new ArgumentOutOfRangeException(nameof(minute));
            if (second < 0 || second >= spec.SecondsPerMinute) throw new ArgumentOutOfRangeException(nameof(second));
            if (subTick < 0 || subTick >= spec.TicksPerSecond) throw new ArgumentOutOfRangeException(nameof(subTick));

            long days = spec.Calendar.DaysFromDate(year, month, day);
            long ticks = days * spec.TicksPerDay
                       + hour * spec.TicksPerHour
                       + minute * spec.TicksPerMinute
                       + second * spec.TicksPerSecond
                       + subTick;

            return new WDateTime(ticks);
        }

        /// <summary>
        /// Builds an instant from an existing date (aligned to 00:00:00).
        /// </summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public static WDateTime New(WDateOnly date)
            => new(checked(date.DayIndex * WWorld.Spec.TicksPerDay));

        /// <summary>
        /// Builds an instant from an existing date and an existing time of day.
        /// </summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public static WDateTime New(WDateOnly date, WTimeOnly time)
            => new(checked(date.DayIndex * WWorld.Spec.TicksPerDay + time.TicksOfDay));

        #endregion Static factory a aktuální čas

        #region Aritmetika — čistá matematika (nevyžaduje WWorld)

        /// <summary>
        /// Returns the difference of two instants as a <see cref="WTimeSpan"/>.
        /// Equivalent to the <c>a - b</c> operator.
        /// </summary>
        public static WTimeSpan Difference(WDateTime a, WDateTime b) => new(a.WorldTicks - b.WorldTicks);

        /// <summary>Moves the instant forward by the given interval.</summary>
        public static WDateTime operator +(WDateTime t, WTimeSpan d) => new(t.WorldTicks + d.Ticks);

        /// <inheritdoc cref="operator +(WDateTime, WTimeSpan)"/>
        public static WDateTime operator +(WTimeSpan d, WDateTime t) => new(t.WorldTicks + d.Ticks);

        /// <summary>Moves the instant forward by the given number of world ticks.</summary>
        public static WDateTime operator +(WDateTime t, long ticks) => new(t.WorldTicks + ticks);

        /// <inheritdoc cref="operator +(WDateTime, long)"/>
        public static WDateTime operator +(long ticks, WDateTime t) => new(t.WorldTicks + ticks);

        /// <summary>Moves the instant backward by the given interval.</summary>
        public static WDateTime operator -(WDateTime t, WTimeSpan d) => new(t.WorldTicks - d.Ticks);

        /// <summary>Moves the instant backward by the given number of world ticks.</summary>
        public static WDateTime operator -(WDateTime t, long ticks) => new(t.WorldTicks - ticks);

        /// <summary>Returns the difference of two instants as a <see cref="WTimeSpan"/>.</summary>
        public static WTimeSpan operator -(WDateTime a, WDateTime b) => new(a.WorldTicks - b.WorldTicks);

        /// <summary>Moves the instant forward by one world tick.</summary>
        public static WDateTime operator ++(WDateTime t) => new(t.WorldTicks + 1);

        /// <summary>Moves the instant backward by one world tick.</summary>
        public static WDateTime operator --(WDateTime t) => new(t.WorldTicks - 1);

        #endregion Aritmetika — čistá matematika (nevyžaduje WWorld)

        #region Ambient aritmetika — Add* a With* (vyžadují WWorld.Configure)

        /// <summary>Adds days to the instant.</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public WDateTime AddDays(long days) => new(WorldTicks + days * WWorld.Spec.TicksPerDay);

        /// <summary>Adds hours to the instant.</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public WDateTime AddHours(long hours) => new(WorldTicks + hours * WWorld.Spec.TicksPerHour);

        /// <summary>Adds minutes to the instant.</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public WDateTime AddMinutes(long minutes) => new(WorldTicks + minutes * WWorld.Spec.TicksPerMinute);

        /// <summary>Adds seconds to the instant.</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public WDateTime AddSeconds(long seconds) => new(WorldTicks + seconds * WWorld.Spec.TicksPerSecond);

        /// <summary>
        /// Adds the given number of months. Preserves the time-of-day component.
        /// The day is clamped if the resulting month has fewer days.
        /// </summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public WDateTime AddMonths(int months)
        {
            var spec = WWorld.Spec;
            var cal = spec.Calendar;
            long tpd = spec.TicksPerDay;
            long tod = WorldTicks % tpd;           // čas dne zachováme
            long dayIdx = WorldTicks / tpd;

            var (y, m, d) = cal.DateFromDays(dayIdx);
            m += months;
            while (m < 1) { y -= 1; m += cal.MonthsInYear(y); }
            while (m > cal.MonthsInYear(y)) { m -= cal.MonthsInYear(y); y += 1; }

            int dim = cal.DaysInMonth(y, m);
            if (d > dim) d = dim;

            return new WDateTime(cal.DaysFromDate(y, m, d) * tpd + tod);
        }

        /// <summary>
        /// Adds the given number of years. Preserves the time-of-day component.
        /// The day is clamped if the target year has fewer days in the given month.
        /// </summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public WDateTime AddYears(int years)
        {
            var spec = WWorld.Spec;
            var cal = spec.Calendar;
            long tpd = spec.TicksPerDay;
            long tod = WorldTicks % tpd;
            long dayIdx = WorldTicks / tpd;

            var (y, m, d) = cal.DateFromDays(dayIdx);
            y += years;
            int dim = cal.DaysInMonth(y, m);
            if (d > dim) d = dim;

            return new WDateTime(cal.DaysFromDate(y, m, d) * tpd + tod);
        }

        /// <summary>Returns the instant with the time of day kept but a new date.</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public WDateTime WithDate(WDateOnly date)
            => WDateTime.New(date, TimeOfDay);

        /// <summary>Returns the instant with the date kept but a new time of day.</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public WDateTime WithTime(WTimeOnly time)
            => WDateTime.New(Date, time);

        #endregion Ambient aritmetika — Add* a With* (vyžadují WWorld.Configure)

        #region Porovnávací operátory

        /// <summary>Returns <c>true</c> if both instants occur on the same world tick.</summary>
        public static bool operator ==(WDateTime a, WDateTime b) => a.WorldTicks == b.WorldTicks;

        /// <summary>Returns <c>true</c> if the instants occur on different world ticks.</summary>
        public static bool operator !=(WDateTime a, WDateTime b) => a.WorldTicks != b.WorldTicks;

        /// <summary>Returns <c>true</c> if <paramref name="a"/> is earlier than <paramref name="b"/>.</summary>
        public static bool operator <(WDateTime a, WDateTime b) => a.WorldTicks < b.WorldTicks;

        /// <summary>Returns <c>true</c> if <paramref name="a"/> is earlier than or at the same instant.</summary>
        public static bool operator <=(WDateTime a, WDateTime b) => a.WorldTicks <= b.WorldTicks;

        /// <summary>Returns <c>true</c> if <paramref name="a"/> is later than <paramref name="b"/>.</summary>
        public static bool operator >(WDateTime a, WDateTime b) => a.WorldTicks > b.WorldTicks;

        /// <summary>Returns <c>true</c> if <paramref name="a"/> is later than or at the same instant as <paramref name="b"/>.</summary>
        public static bool operator >=(WDateTime a, WDateTime b) => a.WorldTicks >= b.WorldTicks;

        #endregion Porovnávací operátory

        #region Parsování (static — vyžadují WWorld.Configure)

        /// <summary>
        /// Parses an instant from a string in the format <c>YYYY-MM-DDTHH:MM:SS[.frac]</c>.
        /// Accepts a space instead of <c>T</c> and an optional <c>Z</c> suffix.
        /// </summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        /// <exception cref="FormatException">Invalid format, or the date does not exist in the calendar.</exception>
        public static WDateTime Parse(string text)
            => TryParse(text.AsSpan(), out var v)
                ? v
                : throw new FormatException($"Neplatný WDateTime: '{text}'.");

        /// <summary>
        /// Attempts to parse an instant from a string.
        /// </summary>
        /// <param name="text">The string to parse.</param>
        /// <param name="value">The output instant; <c>default</c> on failure.</param>
        /// <returns><c>true</c> if parsing succeeded.</returns>
        public static bool TryParse(string? text, out WDateTime value)
            => string.IsNullOrWhiteSpace(text)
                ? (value = default) == default && false
                : TryParse(text.AsSpan(), out value);

        /// <summary>
        /// Attempts to parse an instant from a character span (without allocating a string).
        /// </summary>
        public static bool TryParse(ReadOnlySpan<char> input, out WDateTime value)
        {
            value = default;
            var spec = WWorld.Spec;

            // Trim
            int start = 0, end = input.Length;
            while (start < end && char.IsWhiteSpace(input[start])) start++;
            while (end > start && char.IsWhiteSpace(input[end - 1])) end--;
            var s = input.Slice(start, end - start);
            if (s.Length == 0) return false;

            // Optional Z/z suffix (UTC marker for Earth — ignored)
            if (s[^1] == 'Z' || s[^1] == 'z') s = s[..^1];

            // Split into date and time on T or a space
            int iT = s.IndexOf('T'), iSP = s.IndexOf(' ');
            int sep = (iT >= 0 && iSP >= 0) ? Math.Min(iT, iSP) : Math.Max(iT, iSP);

            ReadOnlySpan<char> datePart = sep >= 0 ? s[..sep] : s;
            ReadOnlySpan<char> timePart = ReadOnlySpan<char>.Empty;
            bool hasTime = sep >= 0;
            if (hasTime) { timePart = s[(sep + 1)..].Trim(); if (timePart.Length == 0) hasTime = false; }

            // Datum: YYYY-MM-DD
            if (datePart.Length < 10 || datePart[4] != '-' || datePart[7] != '-') return false;
            if (!SpanParseInt(datePart[..4], 1, int.MaxValue, out int year)) return false;
            if (!SpanParseInt(datePart.Slice(5, 2), 1, 99, out int month)) return false;
            if (!SpanParseInt(datePart.Slice(8, 2), 1, 99, out int day)) return false;

            // Čas: HH:MM:SS[.frac]
            int hour = 0, minute = 0, second = 0;
            long subTick = 0;

            if (hasTime)
            {
                int dot = timePart.IndexOf('.');
                var main = dot >= 0 ? timePart[..dot] : timePart;
                var frac = dot >= 0 ? timePart[(dot + 1)..] : ReadOnlySpan<char>.Empty;

                if (main.Length < 8 || main[2] != ':' || main[5] != ':') return false;
                if (!SpanParseInt(main[..2], 0, spec.HoursPerDay - 1, out hour)) return false;
                if (!SpanParseInt(main.Slice(3, 2), 0, spec.MinutesPerHour - 1, out minute)) return false;
                if (!SpanParseInt(main.Slice(6, 2), 0, spec.SecondsPerMinute - 1, out second)) return false;

                if (!frac.IsEmpty)
                {
                    bool rawTicks = frac[^1] == 'W';
                    if (rawTicks) frac = frac[..^1];
                    if (frac.Length == 0) return false;
                    for (int i = 0; i < frac.Length; i++) if (frac[i] < '0' || frac[i] > '9') return false;

                    if (rawTicks)
                    {
                        if (!SpanParseInt64(frac, 0, spec.TicksPerSecond - 1, out subTick)) return false;
                    }
                    else
                    {
                        int n = Math.Min(frac.Length, 18);
                        if (!SpanParseInt64(frac[..n], 0, long.MaxValue, out long fracVal)) return false;
                        long pow10 = Pow10(n);
                        subTick = (fracVal * spec.TicksPerSecond) / pow10;
                        if (subTick >= spec.TicksPerSecond) subTick = spec.TicksPerSecond - 1;
                    }
                }
            }

            // Assembly
            long days;
            try { days = spec.Calendar.DaysFromDate(year, month, day); }
            catch { return false; }

            long ticks = days * spec.TicksPerDay
                       + hour * spec.TicksPerHour
                       + minute * spec.TicksPerMinute
                       + second * spec.TicksPerSecond
                       + subTick;

            value = new WDateTime(ticks);
            return true;
        }

        #endregion Parsování (static — vyžadují WWorld.Configure)

        #region Rovnost a hashování

        /// <inheritdoc/>
        public int CompareTo(WDateTime other) => WorldTicks.CompareTo(other.WorldTicks);

        /// <inheritdoc/>
        public bool Equals(WDateTime other) => WorldTicks == other.WorldTicks;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is WDateTime d && Equals(d);

        /// <inheritdoc/>
        public override int GetHashCode() => WorldTicks.GetHashCode();

        #endregion Rovnost a hashování

        #region Formátování

        /// <summary>
        /// Returns the instant as a readable string in the format <c>YYYY-MM-DDTHH:MM:SS</c>.
        /// Requires <see cref="WWorld"/> to be configured. Falls back to WorldTicks otherwise.
        /// </summary>
        public override string ToString()
        {
            if (!WWorld.IsConfigured) return WorldTicks.ToString();

            var spec = WWorld.Spec;
            long dayIndex = Math.DivRem(WorldTicks, spec.TicksPerDay, out long rest);
            var (y, mo, d) = spec.Calendar.DateFromDays(dayIndex);

            int hh = (int)(rest / spec.TicksPerHour); rest %= spec.TicksPerHour;
            int mm = (int)(rest / spec.TicksPerMinute); rest %= spec.TicksPerMinute;
            int ss = (int)(rest / spec.TicksPerSecond); rest %= spec.TicksPerSecond;

            var sb = new StringBuilder(32);
            Append4(sb, y); sb.Append('-');
            Append2(sb, mo); sb.Append('-');
            Append2(sb, d); sb.Append('T');
            Append2(sb, hh); sb.Append(':');
            Append2(sb, mm); sb.Append(':');
            Append2(sb, ss);
            if (rest != 0) sb.Append('.').Append(rest).Append('W');

            return sb.ToString();
        }

        #endregion Formátování

        #region Privátní pomocné metody

        /// <summary>Parses an int from a span — digits only, no sign.</summary>
        private static bool SpanParseInt(ReadOnlySpan<char> sp, int min, int max, out int v)
        {
            long acc = 0;
            for (int i = 0; i < sp.Length; i++)
            {
                int digit = sp[i] - '0';
                if ((uint)digit > 9U) { v = 0; return false; }
                acc = acc * 10 + digit;
                if (acc > int.MaxValue) { v = 0; return false; }
            }
            v = (int)acc;
            return v >= min && v <= max;
        }

        /// <summary>Parses a long from a span — digits only, no sign.</summary>
        private static bool SpanParseInt64(ReadOnlySpan<char> sp, long min, long max, out long v)
        {
            long acc = 0;
            for (int i = 0; i < sp.Length; i++)
            {
                int digit = sp[i] - '0';
                if ((uint)digit > 9U) { v = 0; return false; }
                if (acc > (long.MaxValue - digit) / 10) { v = 0; return false; }
                acc = acc * 10 + digit;
            }
            v = acc;
            return v >= min && v <= max;
        }

        /// <summary>Returns 10^n (without BCL Math.Pow — integer arithmetic).</summary>
        private static long Pow10(int n)
        { long p = 1; for (int i = 0; i < n; i++) p *= 10; return p; }

        /// <summary>Writes a number as exactly 2 digits (zero-padded).</summary>
        private static void Append2(StringBuilder sb, int v)
        {
            sb.Append((char)('0' + v / 10));
            sb.Append((char)('0' + v % 10));
        }

        /// <summary>
        /// Writes the year as at least 4 digits. Years above 9999 are written in full
        /// — no silent truncation.
        /// </summary>
        private static void Append4(StringBuilder sb, int v)
        {
            if (v >= 10000) { sb.Append(v); return; }
            sb.Append((char)('0' + (v / 1000) % 10));
            sb.Append((char)('0' + (v / 100) % 10));
            sb.Append((char)('0' + (v / 10) % 10));
            sb.Append((char)('0' + v % 10));
        }

        #endregion Privátní pomocné metody
    }
}
