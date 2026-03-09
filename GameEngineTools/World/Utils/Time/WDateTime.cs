// WDateTime.cs
// Copyright (c) 50PSoftware

using System.Text;
using System.Text.Json.Serialization;
using GameEngineTools.World.Core.Time;

namespace GameEngineTools.World.Utils.Time
{
    /// <summary>
    /// Reprezentuje konkrétní okamžik v herním světě, uložený jako počet worldTicks
    /// od světové epochy (1. den 1. měsíce 1. roku, 00:00:00).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ambient-spec design.</b> Chová se jako <see cref="DateTime"/> — properties
    /// <see cref="Year"/>, <see cref="Month"/>, <see cref="Day"/> atd. fungují přímo
    /// na hodnotě bez předávání kontextu. Vyžaduje nakonfigurovaný <see cref="WWorld"/>.
    /// Čistá matematika (operátory, porovnávání) <see cref="WWorld"/> nevyžaduje.
    /// </para>
    /// <para>
    /// Příklady:
    /// <code>
    /// // Aktuální čas
    /// var now = WDateTime.Now;
    ///
    /// // Vytvoření
    /// var dt = WDateTime.New(1322, 7, 4, hour: 6);
    ///
    /// // Ambient properties
    /// int year  = dt.Year;    // 1322
    /// int month = dt.Month;   // 7
    /// int day   = dt.Day;     // 4
    /// int hour  = dt.Hour;    // 6
    ///
    /// // Složky jako typy
    /// WDateOnly date = dt.Date;
    /// WTimeOnly time = dt.TimeOfDay;
    ///
    /// // Čistá matematika — bez WWorld
    /// var later = dt + WTimeSpan.FromHours(2);
    /// var diff  = later - dt;
    /// bool past = dt &lt; WDateTime.Now;
    ///
    /// // Kalendářní aritmetika
    /// var nextMonth = dt.AddMonths(1);
    /// var nextYear  = dt.AddYears(1);
    ///
    /// // Formátování
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
        /// Inicializuje nový okamžik z přesného počtu worldTicks od světové epochy.
        /// </summary>
        /// <param name="worldTicks">
        /// Počet worldTicks od světové epochy (0 = 1/1/1 00:00:00).
        /// Záporná hodnota by reprezentovala čas před epochou — není podporována.
        /// </param>
        public WDateTime(long worldTicks) => WorldTicks = worldTicks;

        #endregion Konstrukce

        #region Vlastnosti — raw data

        /// <summary>
        /// Počet worldTicks od světové epochy (1/1/1 00:00:00).
        /// Jediný zdroj pravdy.
        /// </summary>
        public long WorldTicks { get; }

        #endregion Vlastnosti — raw data

        #region Konstanty

        /// <summary>Minimální reprezentovatelná hodnota — světová epocha (1/1/1 00:00:00).</summary>
        public static WDateTime MinValue => new(0);

        #endregion Konstanty

        #region Ambient vlastnosti — složky data (vyžadují WWorld.Configure)

        /// <summary>Rok tohoto okamžiku.</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public int Year
        {
            get
            {
                var spec = WWorld.Spec;
                var (y, _, _) = spec.Calendar.DateFromDays(WorldTicks / spec.TicksPerDay);
                return y;
            }
        }

        /// <summary>Měsíc tohoto okamžiku (1-based).</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public int Month
        {
            get
            {
                var spec = WWorld.Spec;
                var (_, m, _) = spec.Calendar.DateFromDays(WorldTicks / spec.TicksPerDay);
                return m;
            }
        }

        /// <summary>Den v měsíci tohoto okamžiku (1-based).</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public int Day
        {
            get
            {
                var spec = WWorld.Spec;
                var (_, _, d) = spec.Calendar.DateFromDays(WorldTicks / spec.TicksPerDay);
                return d;
            }
        }

        /// <summary>Hodina tohoto okamžiku (0-based).</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public int Hour
        {
            get
            {
                var spec = WWorld.Spec;
                long rem = WorldTicks % spec.TicksPerDay;
                return (int)(rem / spec.TicksPerHour);
            }
        }

        /// <summary>Minuta tohoto okamžiku (0-based).</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public int Minute
        {
            get
            {
                var spec = WWorld.Spec;
                long rem = WorldTicks % spec.TicksPerHour;
                return (int)(rem / spec.TicksPerMinute);
            }
        }

        /// <summary>Sekunda tohoto okamžiku (0-based).</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
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
        /// Datová složka tohoto okamžiku jako <see cref="WDateOnly"/>.
        /// Časová složka je zahozena.
        /// </summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public WDateOnly Date => new(WorldTicks / WWorld.Spec.TicksPerDay);

        /// <summary>
        /// Časová složka tohoto okamžiku jako <see cref="WTimeOnly"/>.
        /// Datová složka je zahozena.
        /// </summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
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
        /// Den v roce (1-based). Výpočet jde přes kalendář.
        /// </summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
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
        /// Maximální bezpečně reprezentovatelná hodnota — největší násobek TicksPerDay v long.
        /// </summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
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
        /// Aktuální herní čas z <see cref="WWorld.Clock"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public static WDateTime Now => WWorld.Clock.Now;

        /// <summary>
        /// Vytvoří okamžik ze všech časových složek.
        /// Validuje každou složku vůči <see cref="WWorld.Spec"/> a jeho kalendáři.
        /// </summary>
        /// <param name="year">Rok (≥ 1).</param>
        /// <param name="month">Měsíc (1-based).</param>
        /// <param name="day">Den v měsíci (1-based).</param>
        /// <param name="hour">Hodina (0..HoursPerDay-1). Výchozí 0.</param>
        /// <param name="minute">Minuta (0..MinutesPerHour-1). Výchozí 0.</param>
        /// <param name="second">Sekunda (0..SecondsPerMinute-1). Výchozí 0.</param>
        /// <param name="subTick">Subtiky (0..TicksPerSecond-1). Výchozí 0.</param>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Pokud složky překračují platný rozsah.</exception>
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
        /// Sestaví okamžik z hotového data (zarovnáno na 00:00:00).
        /// </summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public static WDateTime New(WDateOnly date)
            => new(checked(date.DayIndex * WWorld.Spec.TicksPerDay));

        /// <summary>
        /// Sestaví okamžik z hotového data a hotového času dne.
        /// </summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public static WDateTime New(WDateOnly date, WTimeOnly time)
            => new(checked(date.DayIndex * WWorld.Spec.TicksPerDay + time.TicksOfDay));

        #endregion Static factory a aktuální čas

        #region Aritmetika — čistá matematika (nevyžaduje WWorld)

        /// <summary>
        /// Vrátí rozdíl dvou okamžiků jako <see cref="WTimeSpan"/>.
        /// Ekvivalentní operátoru <c>a - b</c>.
        /// </summary>
        public static WTimeSpan Difference(WDateTime a, WDateTime b) => new(a.WorldTicks - b.WorldTicks);

        /// <summary>Posune okamžik o zadaný interval dopředu.</summary>
        public static WDateTime operator +(WDateTime t, WTimeSpan d) => new(t.WorldTicks + d.Ticks);

        /// <inheritdoc cref="operator +(WDateTime, WTimeSpan)"/>
        public static WDateTime operator +(WTimeSpan d, WDateTime t) => new(t.WorldTicks + d.Ticks);

        /// <summary>Posune okamžik o zadaný počet worldTicks dopředu.</summary>
        public static WDateTime operator +(WDateTime t, long ticks) => new(t.WorldTicks + ticks);

        /// <inheritdoc cref="operator +(WDateTime, long)"/>
        public static WDateTime operator +(long ticks, WDateTime t) => new(t.WorldTicks + ticks);

        /// <summary>Posune okamžik o zadaný interval dozadu.</summary>
        public static WDateTime operator -(WDateTime t, WTimeSpan d) => new(t.WorldTicks - d.Ticks);

        /// <summary>Posune okamžik o zadaný počet worldTicks dozadu.</summary>
        public static WDateTime operator -(WDateTime t, long ticks) => new(t.WorldTicks - ticks);

        /// <summary>Vrátí rozdíl dvou okamžiků jako <see cref="WTimeSpan"/>.</summary>
        public static WTimeSpan operator -(WDateTime a, WDateTime b) => new(a.WorldTicks - b.WorldTicks);

        /// <summary>Posune okamžik o jeden worldTick dopředu.</summary>
        public static WDateTime operator ++(WDateTime t) => new(t.WorldTicks + 1);

        /// <summary>Posune okamžik o jeden worldTick dozadu.</summary>
        public static WDateTime operator --(WDateTime t) => new(t.WorldTicks - 1);

        #endregion Aritmetika — čistá matematika (nevyžaduje WWorld)

        #region Ambient aritmetika — Add* a With* (vyžadují WWorld.Configure)

        /// <summary>Přičte dny k okamžiku.</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public WDateTime AddDays(long days) => new(WorldTicks + days * WWorld.Spec.TicksPerDay);

        /// <summary>Přičte hodiny k okamžiku.</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public WDateTime AddHours(long hours) => new(WorldTicks + hours * WWorld.Spec.TicksPerHour);

        /// <summary>Přičte minuty k okamžiku.</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public WDateTime AddMinutes(long minutes) => new(WorldTicks + minutes * WWorld.Spec.TicksPerMinute);

        /// <summary>Přičte sekundy k okamžiku.</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public WDateTime AddSeconds(long seconds) => new(WorldTicks + seconds * WWorld.Spec.TicksPerSecond);

        /// <summary>
        /// Přičte zadaný počet měsíců. Zachová časovou složku dne.
        /// Den je oříznut pokud výsledný měsíc má méně dní (clamp).
        /// </summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
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
        /// Přičte zadaný počet let. Zachová časovou složku dne.
        /// Den je oříznut pokud cílový rok má v daném měsíci méně dní.
        /// </summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
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

        /// <summary>Vrátí okamžik se zachovaným časem dne, ale novým datem.</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public WDateTime WithDate(WDateOnly date)
            => WDateTime.New(date, TimeOfDay);

        /// <summary>Vrátí okamžik se zachovaným datem, ale novým časem dne.</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public WDateTime WithTime(WTimeOnly time)
            => WDateTime.New(Date, time);

        #endregion Ambient aritmetika — Add* a With* (vyžadují WWorld.Configure)

        #region Porovnávací operátory

        /// <summary>Vrátí <c>true</c> pokud oba okamžiky nastávají ve stejný worldTick.</summary>
        public static bool operator ==(WDateTime a, WDateTime b) => a.WorldTicks == b.WorldTicks;

        /// <summary>Vrátí <c>true</c> pokud okamžiky nastávají v různý worldTick.</summary>
        public static bool operator !=(WDateTime a, WDateTime b) => a.WorldTicks != b.WorldTicks;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> dříve než <paramref name="b"/>.</summary>
        public static bool operator <(WDateTime a, WDateTime b) => a.WorldTicks < b.WorldTicks;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> dříve nebo ve stejný okamžik.</summary>
        public static bool operator <=(WDateTime a, WDateTime b) => a.WorldTicks <= b.WorldTicks;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> po <paramref name="b"/>.</summary>
        public static bool operator >(WDateTime a, WDateTime b) => a.WorldTicks > b.WorldTicks;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> po nebo ve stejný okamžik jako <paramref name="b"/>.</summary>
        public static bool operator >=(WDateTime a, WDateTime b) => a.WorldTicks >= b.WorldTicks;

        #endregion Porovnávací operátory

        #region Parsování (static — vyžadují WWorld.Configure)

        /// <summary>
        /// Parsuje okamžik ze řetězce ve formátu <c>YYYY-MM-DDTHH:MM:SS[.frac]</c>.
        /// Akceptuje mezeru místo <c>T</c> a volitelný suffix <c>Z</c>.
        /// </summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        /// <exception cref="FormatException">Neplatný formát nebo datum neexistuje v kalendáři.</exception>
        public static WDateTime Parse(string text)
            => TryParse(text.AsSpan(), out var v)
                ? v
                : throw new FormatException($"Neplatný WDateTime: '{text}'.");

        /// <summary>
        /// Pokusí se parsovat okamžik ze řetězce.
        /// </summary>
        /// <param name="text">Řetězec k parsování.</param>
        /// <param name="value">Výstupní okamžik; <c>default</c> při neúspěchu.</param>
        /// <returns><c>true</c> pokud parsování uspělo.</returns>
        public static bool TryParse(string? text, out WDateTime value)
            => string.IsNullOrWhiteSpace(text)
                ? (value = default) == default && false
                : TryParse(text.AsSpan(), out value);

        /// <summary>
        /// Pokusí se parsovat okamžik ze span znaků (bez alokace stringu).
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

            // Volitelný suffix Z/z (UTC marker pro Zemi — ignorujeme)
            if (s[^1] == 'Z' || s[^1] == 'z') s = s[..^1];

            // Rozdělit na datum a čas podle T nebo mezery
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

            // Sestavení
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
        /// Vrátí okamžik jako čitelný řetězec ve formátu <c>YYYY-MM-DDTHH:MM:SS</c>.
        /// Vyžaduje nakonfigurovaný <see cref="WWorld"/>. Fallback na WorldTicks pokud není.
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

        /// <summary>Parsuje int ze span — pouze číslice, žádné znaménko.</summary>
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

        /// <summary>Parsuje long ze span — pouze číslice, žádné znaménko.</summary>
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

        /// <summary>Vrátí 10^n (bez BCL Math.Pow — pracujeme s int).</summary>
        private static long Pow10(int n)
        { long p = 1; for (int i = 0; i < n; i++) p *= 10; return p; }

        /// <summary>Zapíše číslo jako přesně 2 číslice (s nulou vlevo).</summary>
        private static void Append2(StringBuilder sb, int v)
        {
            sb.Append((char)('0' + v / 10));
            sb.Append((char)('0' + v % 10));
        }

        /// <summary>
        /// Zapíše rok jako alespoň 4 číslice. Roky nad 9999 se vypíší celé
        /// — žádné tiché oříznutí.
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
