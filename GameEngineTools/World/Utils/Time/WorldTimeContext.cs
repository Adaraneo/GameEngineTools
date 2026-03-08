// WorldTimeContext.cs
// Copyright (c) 50PSoftware

using System.Text;
using GameEngineTools.World.Utils.Time;

namespace GameEngineTools.World.Core.Time
{
    /// <summary>
    /// Hlavní vstupní bod pro všechny operace s herním časem, které vyžadují
    /// znalost <see cref="WorldTimeSpec"/> (kalendář, časová soustava).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Registruj jako singleton přes DI. Předávej přes konstruktor.</b>
    /// </para>
    /// <para>
    /// Datové typy (<see cref="WDateTime"/>, <see cref="WDateOnly"/>,
    /// <see cref="WTimeOnly"/>, <see cref="WTimeSpan"/>) jsou záměrně čistá data
    /// bez závislosti na <c>Spec</c>. Veškeré operace závislé na světovém
    /// kalendáři nebo časové soustavě patří do této třídy.
    /// </para>
    /// <para>
    /// Příklad registrace a použití:
    /// <code>
    /// // DI registrace
    /// services.AddSingleton&lt;WorldTimeContext&gt;();
    ///
    /// // Použití
    /// var now      = _ctx.Now();
    /// var dt       = _ctx.Create(1322, 7, 4, hour: 6);
    /// var twoHours = _ctx.Hours(2);
    /// var later    = dt + twoHours;
    /// string label = _ctx.Format(dt);    // "1322-07-04T06:00:00"
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class WorldTimeContext
    {
        #region Konstrukce

        /// <summary>
        /// Inicializuje nový kontext s danou časovou specifikací a hodinami.
        /// </summary>
        /// <param name="spec">
        /// Specifikace světového času — kalendář, délka dne, počet minut v hodině atd.
        /// </param>
        /// <param name="clock">
        /// Zdroj aktuálního světového času. Implementace může být reálná (<see cref="WorldClock"/>)
        /// nebo testovací (deterministická fake).
        /// </param>
        public WorldTimeContext(WorldTimeSpec spec, IWorldClock clock)
        {
            Spec   = spec;
            _clock = clock;
        }

        private readonly IWorldClock _clock;

        #endregion

        #region Vlastnosti

        /// <summary>
        /// Specifikace světového času platná pro tento kontext.
        /// Obsahuje kalendář, počet hodin v dni, ticků za sekundu atd.
        /// </summary>
        public WorldTimeSpec Spec { get; }

        #endregion

        // =====================================================================
        //  WTimeSpan
        // =====================================================================

        #region WTimeSpan — factory (z lidských jednotek na ticky)

        /// <summary>
        /// Vytvoří interval odpovídající zadanému počtu světových sekund.
        /// </summary>
        /// <param name="s">Počet sekund (může být desetinný).</param>
        public WTimeSpan Seconds(double s) => new((long)(s * Spec.TicksPerSecond));

        /// <summary>
        /// Vytvoří interval odpovídající zadanému počtu světových minut.
        /// </summary>
        /// <param name="m">Počet minut (může být desetinný).</param>
        public WTimeSpan Minutes(double m) => new((long)(m * Spec.TicksPerMinute));

        /// <summary>
        /// Vytvoří interval odpovídající zadanému počtu světových hodin.
        /// </summary>
        /// <param name="h">Počet hodin (může být desetinný).</param>
        public WTimeSpan Hours(double h) => new((long)(h * Spec.TicksPerHour));

        /// <summary>
        /// Vytvoří interval odpovídající zadanému počtu světových dní.
        /// </summary>
        /// <param name="d">Počet dní (může být desetinný).</param>
        public WTimeSpan Days(double d) => new((long)(d * Spec.TicksPerDay));

        #endregion

        #region WTimeSpan — konverze (z tiků na lidské jednotky)

        /// <summary>
        /// Vrátí celkový počet světových sekund reprezentovaných intervalem.
        /// Výsledek může být desetinný i záporný.
        /// </summary>
        public double TotalSeconds(WTimeSpan span) => (double)span.Ticks / Spec.TicksPerSecond;

        /// <summary>
        /// Vrátí celkový počet světových minut reprezentovaných intervalem.
        /// Výsledek může být desetinný i záporný.
        /// </summary>
        public double TotalMinutes(WTimeSpan span) => (double)span.Ticks / Spec.TicksPerMinute;

        /// <summary>
        /// Vrátí celkový počet světových hodin reprezentovaných intervalem.
        /// Výsledek může být desetinný i záporný.
        /// </summary>
        public double TotalHours(WTimeSpan span) => (double)span.Ticks / Spec.TicksPerHour;

        /// <summary>
        /// Vrátí celkový počet světových dní reprezentovaných intervalem.
        /// Výsledek může být desetinný i záporný.
        /// </summary>
        public double TotalDays(WTimeSpan span) => (double)span.Ticks / Spec.TicksPerDay;

        /// <summary>Absolutní hodnota <see cref="TotalSeconds"/>. Vždy kladná nebo nulová.</summary>
        public double AbsTotalSeconds(WTimeSpan span) => Math.Abs(TotalSeconds(span));

        /// <summary>Absolutní hodnota <see cref="TotalHours"/>. Vždy kladná nebo nulová.</summary>
        public double AbsTotalHours(WTimeSpan span)   => Math.Abs(TotalHours(span));

        /// <summary>Absolutní hodnota <see cref="TotalDays"/>. Vždy kladná nebo nulová.</summary>
        public double AbsTotalDays(WTimeSpan span)    => Math.Abs(TotalDays(span));

        #endregion

        #region WTimeSpan — dekompozice a formátování

        /// <summary>
        /// Rozloží interval na zobrazitelné složky (pracuje s absolutní hodnotou).
        /// </summary>
        /// <param name="span">Zdrojový interval.</param>
        /// <returns>
        /// Tuple složek: počet celých dní, hodin (0..HoursPerDay-1),
        /// minut (0..MinutesPerHour-1), sekund (0..SecondsPerMinute-1)
        /// a subticků pod sekundou.
        /// </returns>
        /// <remarks>
        /// Znaménko intervalu není součástí výsledku — pro záporné intervaly
        /// ošetři znaménko před voláním přes <see cref="WTimeSpan.Sign"/>.
        /// </remarks>
        public (long days, int hours, int minutes, int seconds, long subTicks)
            DeconstructSpan(WTimeSpan span)
        {
            long at = Math.Abs(span.Ticks);
            long d  = at / Spec.TicksPerDay;           at %= Spec.TicksPerDay;
            int  hh = (int)(at / Spec.TicksPerHour);   at %= Spec.TicksPerHour;
            int  mm = (int)(at / Spec.TicksPerMinute);  at %= Spec.TicksPerMinute;
            int  ss = (int)(at / Spec.TicksPerSecond);  at %= Spec.TicksPerSecond;
            return (d, hh, mm, ss, at);
        }

        /// <summary>
        /// Formátuje <see cref="WTimeSpan"/> jako čitelný řetězec ve formátu
        /// <c>[-]d.hh:mm:ss[.sub]</c>.
        /// </summary>
        /// <remarks>
        /// Složka <c>d.</c> se vypouští pokud je počet dní nulový.
        /// Složka <c>.sub</c> se vypouští pokud jsou subticky nulové.
        /// </remarks>
        /// <example>
        /// <code>
        /// ctx.Format(ctx.Hours(1.5))  // "01:30:00"
        /// ctx.Format(ctx.Days(-2.5))  // "-2.12:00:00"
        /// </code>
        /// </example>
        public string Format(WTimeSpan span)
        {
            var sign = span.Ticks < 0 ? "-" : "";
            var (d, hh, mm, ss, sub) = DeconstructSpan(span);

            if (d != 0)
                return sub != 0
                    ? $"{sign}{d}.{hh:00}:{mm:00}:{ss:00}.{sub}"
                    : $"{sign}{d}.{hh:00}:{mm:00}:{ss:00}";
            else
                return sub != 0
                    ? $"{sign}{hh:00}:{mm:00}:{ss:00}.{sub}"
                    : $"{sign}{hh:00}:{mm:00}:{ss:00}";
        }

        #endregion

        // =====================================================================
        //  WDateOnly
        // =====================================================================

        #region WDateOnly — factory

        /// <summary>
        /// Vytvoří <see cref="WDateOnly"/> ze složek (rok, měsíc, den).
        /// Validace probíhá přes <see cref="WorldTimeSpec.Calendar"/>.
        /// </summary>
        /// <param name="year">Rok (≥ 1).</param>
        /// <param name="month">Měsíc (1-based).</param>
        /// <param name="day">Den v měsíci (1-based).</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Pokud složky neodpovídají platnému datu v aktuálním kalendáři.
        /// </exception>
        public WDateOnly CreateDate(int year, int month, int day)
            => new(Spec.Calendar.DaysFromDate(year, month, day));

        /// <summary>
        /// Extrahuje datovou složku z <see cref="WDateTime"/> a vrátí ji jako <see cref="WDateOnly"/>.
        /// Časová složka (hodiny, minuty, sekundy) je zahozena.
        /// </summary>
        /// <param name="dt">Zdrojový okamžik.</param>
        public WDateOnly DateOf(WDateTime dt)
            => new(dt.WorldTicks / Spec.TicksPerDay);

        #endregion

        #region WDateOnly — dekompozice

        /// <summary>
        /// Rozloží <see cref="WDateOnly"/> na složky (rok, měsíc, den).
        /// </summary>
        /// <param name="date">Datum k rozložení.</param>
        /// <returns>Tuple (year, month, day) odpovídající světovému kalendáři.</returns>
        public (int year, int month, int day) GetDateParts(WDateOnly date)
            => Spec.Calendar.DateFromDays(date.DayIndex);

        #endregion

        #region WDateOnly — kalendářní aritmetika

        /// <summary>
        /// Přičte zadaný počet měsíců k datu.
        /// Správně pracuje s libovolným počtem měsíců v roce dle aktivního kalendáře —
        /// nepoužívá hardcoded konstantu 12.
        /// </summary>
        /// <param name="date">Výchozí datum.</param>
        /// <param name="months">Počet měsíců (může být záporný).</param>
        /// <returns>
        /// Nové datum. Pokud výsledný měsíc má méně dní než původní den,
        /// je den oříznut na poslední platný den výsledného měsíce (clamp).
        /// </returns>
        public WDateOnly AddMonths(WDateOnly date, int months)
        {
            var (y, m, d) = GetDateParts(date);
            var cal = Spec.Calendar;

            m += months;

            while (m < 1)
            {
                y -= 1;
                m += cal.MonthsInYear(y);
            }

            while (m > cal.MonthsInYear(y))
            {
                m -= cal.MonthsInYear(y);
                y += 1;
            }

            var dim = cal.DaysInMonth(y, m);
            if (d > dim) d = dim;

            return CreateDate(y, m, d);
        }

        /// <summary>
        /// Přičte zadaný počet let k datu.
        /// </summary>
        /// <param name="date">Výchozí datum.</param>
        /// <param name="years">Počet let (může být záporný).</param>
        /// <returns>
        /// Nové datum. Den je oříznut pokud cílový rok má v daném měsíci méně dní.
        /// </returns>
        public WDateOnly AddYears(WDateOnly date, int years)
        {
            var (y, m, d) = GetDateParts(date);
            y += years;

            var dim = Spec.Calendar.DaysInMonth(y, m);
            if (d > dim) d = dim;

            return CreateDate(y, m, d);
        }

        #endregion

        #region WDateOnly — konverze na WDateTime

        /// <summary>
        /// Kombinuje datum s časem dne a vrátí plný okamžik <see cref="WDateTime"/>.
        /// </summary>
        /// <exception cref="OverflowException">Pokud výsledek přeteče <c>long</c>.</exception>
        public WDateTime At(WDateOnly date, WTimeOnly time)
            => new(checked(date.DayIndex * Spec.TicksPerDay + time.TicksOfDay));

        /// <summary>
        /// Vrátí okamžik odpovídající začátku dne (00:00:00) pro zadané datum.
        /// </summary>
        /// <exception cref="OverflowException">Pokud výsledek přeteče <c>long</c>.</exception>
        public WDateTime StartOfDay(WDateOnly date)
            => new(checked(date.DayIndex * Spec.TicksPerDay));

        #endregion

        #region WDateOnly — parsování

        /// <summary>
        /// Parsuje datum ze řetězce ve formátu <c>YYYY-MM-DD</c>.
        /// </summary>
        /// <exception cref="FormatException">
        /// Pokud řetězec nemá platný formát nebo datum neexistuje v kalendáři.
        /// </exception>
        public WDateOnly ParseDate(string text)
            => TryParseDate(text, out var v)
                ? v
                : throw new FormatException($"Neplatný WDateOnly: '{text}'.");

        /// <summary>
        /// Pokusí se parsovat datum ze řetězce ve formátu <c>YYYY-MM-DD</c>.
        /// </summary>
        /// <param name="text">Řetězec k parsování.</param>
        /// <param name="value">Výstupní datum; <c>default</c> při neúspěchu.</param>
        /// <returns><c>true</c> pokud parsování uspělo.</returns>
        public bool TryParseDate(string? text, out WDateOnly value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var s = text.AsSpan().Trim();
            if (s.Length < 10 || s[4] != '-' || s[7] != '-') return false;

            if (!TryParseInt(s[..4],        min: 1, max: int.MaxValue, out int y))  return false;
            if (!TryParseInt(s.Slice(5, 2), min: 1, max: 99,           out int mo)) return false;
            if (!TryParseInt(s.Slice(8, 2), min: 1, max: 99,           out int da)) return false;

            try   { value = CreateDate(y, mo, da); return true; }
            catch { return false; }
        }

        #endregion

        #region WDateOnly — formátování

        /// <summary>
        /// Formátuje <see cref="WDateOnly"/> jako <c>YYYY-MM-DD</c>.
        /// </summary>
        /// <example><c>1322-07-04</c></example>
        public string Format(WDateOnly date)
        {
            var (y, m, d) = GetDateParts(date);
            return $"{y:0000}-{m:00}-{d:00}";
        }

        #endregion

        // =====================================================================
        //  WTimeOnly
        // =====================================================================

        #region WTimeOnly — factory

        /// <summary>
        /// Vytvoří <see cref="WTimeOnly"/> ze složek (hodina, minuta, sekunda).
        /// Validuje rozsah každé složky vůči aktuálnímu <see cref="Spec"/>.
        /// </summary>
        /// <param name="hour">Hodina (0..HoursPerDay-1).</param>
        /// <param name="minute">Minuta (0..MinutesPerHour-1).</param>
        /// <param name="second">Sekunda (0..SecondsPerMinute-1).</param>
        /// <param name="subTick">Subtiky pod sekundou (0..TicksPerSecond-1). Výchozí 0.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Pokud jakákoliv složka překračuje rozsah definovaný v <see cref="Spec"/>.
        /// </exception>
        public WTimeOnly CreateTime(int hour, int minute, int second, long subTick = 0)
        {
            if (hour    < 0 || hour    >= Spec.HoursPerDay)      throw new ArgumentOutOfRangeException(nameof(hour));
            if (minute  < 0 || minute  >= Spec.MinutesPerHour)   throw new ArgumentOutOfRangeException(nameof(minute));
            if (second  < 0 || second  >= Spec.SecondsPerMinute) throw new ArgumentOutOfRangeException(nameof(second));
            if (subTick < 0 || subTick >= Spec.TicksPerSecond)   throw new ArgumentOutOfRangeException(nameof(subTick));

            long ticks = hour   * Spec.TicksPerHour
                       + minute * Spec.TicksPerMinute
                       + second * Spec.TicksPerSecond
                       + subTick;

            return new WTimeOnly(ticks);
        }

        /// <summary>
        /// Extrahuje časovou složku dne z <see cref="WDateTime"/>.
        /// Datová složka (rok, měsíc, den) je zahozena.
        /// </summary>
        /// <param name="dt">Zdrojový okamžik.</param>
        public WTimeOnly TimeOf(WDateTime dt)
        {
            long rem = dt.WorldTicks % Spec.TicksPerDay;
            if (rem < 0) rem += Spec.TicksPerDay;
            return new WTimeOnly(rem);
        }

        #endregion

        #region WTimeOnly — dekompozice

        /// <summary>
        /// Rozloží <see cref="WTimeOnly"/> na složky (hodina, minuta, sekunda, subtiky).
        /// </summary>
        /// <param name="time">Čas dne k rozložení.</param>
        /// <returns>Tuple (hour, minute, second, subTick).</returns>
        public (int hour, int minute, int second, long subTick) GetTimeParts(WTimeOnly time)
        {
            long rem    = time.TicksOfDay;
            int  hour   = (int)(rem / Spec.TicksPerHour);   rem %= Spec.TicksPerHour;
            int  minute = (int)(rem / Spec.TicksPerMinute);  rem %= Spec.TicksPerMinute;
            int  second = (int)(rem / Spec.TicksPerSecond);  rem %= Spec.TicksPerSecond;
            return (hour, minute, second, rem);
        }

        /// <summary>
        /// Vrátí počet milisekund v rámci aktuální světové sekundy (0..999).
        /// </summary>
        public int GetMillisecond(WTimeOnly time)
        {
            long subTick = time.TicksOfDay % Spec.TicksPerSecond;
            return (int)((subTick * 1000L) / Spec.TicksPerSecond);
        }

        #endregion

        #region WTimeOnly — aritmetika

        /// <summary>
        /// Přičte interval k času dne s automatickým wraparoundem přes půlnoc.
        /// </summary>
        /// <param name="time">Výchozí čas dne.</param>
        /// <param name="span">Interval k přičtení (může být záporný).</param>
        /// <returns>Nový čas dne v rozsahu [0, TicksPerDay).</returns>
        public WTimeOnly AddTime(WTimeOnly time, WTimeSpan span)
        {
            long t = time.TicksOfDay + span.Ticks;
            t %= Spec.TicksPerDay;
            if (t < 0) t += Spec.TicksPerDay;
            return new WTimeOnly(t);
        }

        /// <summary>Přičte hodiny k času dne s wraparoundem.</summary>
        public WTimeOnly AddHours(WTimeOnly time, double hours)     => AddTime(time, Hours(hours));

        /// <summary>Přičte minuty k času dne s wraparoundem.</summary>
        public WTimeOnly AddMinutes(WTimeOnly time, double minutes)  => AddTime(time, Minutes(minutes));

        /// <summary>Přičte sekundy k času dne s wraparoundem.</summary>
        public WTimeOnly AddSeconds(WTimeOnly time, double seconds)  => AddTime(time, Seconds(seconds));

        /// <summary>
        /// Vrátí nejkratší vzdálenost mezi dvěma časy dne jako <see cref="WTimeSpan"/>,
        /// s wraparoundem přes půlnoc.
        /// </summary>
        /// <returns>
        /// Interval v rozsahu (-TicksPerDay/2, TicksPerDay/2].
        /// Kladný výsledek = <paramref name="b"/> je po <paramref name="a"/>.
        /// </returns>
        public WTimeSpan TimeDiff(WTimeOnly a, WTimeOnly b)
        {
            long diff = b.TicksOfDay - a.TicksOfDay;
            long half = Spec.TicksPerDay / 2;
            if (diff >   half) diff -= Spec.TicksPerDay;
            if (diff <= -half) diff += Spec.TicksPerDay;
            return new WTimeSpan(diff);
        }

        #endregion

        #region WTimeOnly — parsování

        /// <summary>
        /// Parsuje čas dne ze řetězce ve formátu <c>HH:MM:SS[.sub]</c>.
        /// </summary>
        /// <exception cref="FormatException">Neplatný formát nebo složky mimo rozsah.</exception>
        public WTimeOnly ParseTime(string text)
            => TryParseTime(text, out var v)
                ? v
                : throw new FormatException($"Neplatný WTimeOnly: '{text}'.");

        /// <summary>
        /// Pokusí se parsovat čas dne ze řetězce ve formátu <c>HH:MM:SS[.sub]</c>.
        /// </summary>
        /// <param name="text">Řetězec k parsování.</param>
        /// <param name="value">Výstupní čas; <c>default</c> při neúspěchu.</param>
        /// <returns><c>true</c> pokud parsování uspělo.</returns>
        public bool TryParseTime(string? text, out WTimeOnly value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var s   = text.AsSpan().Trim();
            var dot = s.IndexOf('.');

            ReadOnlySpan<char> main = dot >= 0 ? s[..dot] : s;
            ReadOnlySpan<char> frac = dot >= 0 ? s[(dot + 1)..] : ReadOnlySpan<char>.Empty;

            if (main.Length < 8 || main[2] != ':' || main[5] != ':') return false;

            if (!TryParseInt(main[..2],        min: 0, max: Spec.HoursPerDay      - 1, out int hh)) return false;
            if (!TryParseInt(main.Slice(3, 2), min: 0, max: Spec.MinutesPerHour   - 1, out int mm)) return false;
            if (!TryParseInt(main.Slice(6, 2), min: 0, max: Spec.SecondsPerMinute - 1, out int ss)) return false;

            long sub = 0;
            if (!frac.IsEmpty)
            {
                if (!TryParseInt64(frac, min: 0, max: Spec.TicksPerSecond - 1, out sub)) return false;
            }

            try   { value = CreateTime(hh, mm, ss, sub); return true; }
            catch { return false; }
        }

        #endregion

        #region WTimeOnly — formátování

        /// <summary>
        /// Formátuje <see cref="WTimeOnly"/> jako <c>HH:MM:SS[.sub]</c>.
        /// Složka <c>.sub</c> se vypouští pokud jsou subticky nulové.
        /// </summary>
        /// <example><c>06:30:00</c></example>
        public string Format(WTimeOnly time)
        {
            var (hh, mm, ss, sub) = GetTimeParts(time);
            return sub != 0
                ? $"{hh:00}:{mm:00}:{ss:00}.{sub}"
                : $"{hh:00}:{mm:00}:{ss:00}";
        }

        #endregion

        // =====================================================================
        //  WDateTime
        // =====================================================================

        #region WDateTime — factory

        /// <summary>
        /// Vrátí aktuální herní čas z připojeného <see cref="IWorldClock"/>.
        /// </summary>
        public WDateTime Now() => new WDateTime(_clock.NowWorldTicks());

        /// <summary>
        /// Maximální bezpečně reprezentovatelná hodnota — největší násobek
        /// <c>TicksPerDay</c> vejdoucí se do <c>long.MaxValue</c>.
        /// </summary>
        /// <remarks>
        /// Zarovnání na celý den zabraňuje přetečení při výpočtech s časovou složkou.
        /// </remarks>
        public WDateTime MaxValue
        {
            get
            {
                long fullDays = long.MaxValue / Spec.TicksPerDay;
                return new WDateTime(fullDays * Spec.TicksPerDay);
            }
        }

        /// <summary>
        /// Vytvoří okamžik ze všech časových složek.
        /// Validuje každou složku vůči aktuálnímu <see cref="Spec"/> a jeho kalendáři.
        /// </summary>
        /// <param name="year">Rok (≥ 1).</param>
        /// <param name="month">Měsíc (1-based).</param>
        /// <param name="day">Den v měsíci (1-based).</param>
        /// <param name="hour">Hodina (0..HoursPerDay-1). Výchozí 0.</param>
        /// <param name="minute">Minuta (0..MinutesPerHour-1). Výchozí 0.</param>
        /// <param name="second">Sekunda (0..SecondsPerMinute-1). Výchozí 0.</param>
        /// <param name="subTick">Subtiky (0..TicksPerSecond-1). Výchozí 0.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Pokud jakákoliv složka překračuje platný rozsah.
        /// </exception>
        public WDateTime Create(
            int year, int month, int day,
            int hour = 0, int minute = 0, int second = 0, long subTick = 0)
        {
            // Validace časových složek (kalendářní validace je v DaysFromDate)
            if (hour    < 0 || hour    >= Spec.HoursPerDay)      throw new ArgumentOutOfRangeException(nameof(hour));
            if (minute  < 0 || minute  >= Spec.MinutesPerHour)   throw new ArgumentOutOfRangeException(nameof(minute));
            if (second  < 0 || second  >= Spec.SecondsPerMinute) throw new ArgumentOutOfRangeException(nameof(second));
            if (subTick < 0 || subTick >= Spec.TicksPerSecond)   throw new ArgumentOutOfRangeException(nameof(subTick));

            long days  = Spec.Calendar.DaysFromDate(year, month, day);
            long ticks = days   * Spec.TicksPerDay
                       + hour   * Spec.TicksPerHour
                       + minute * Spec.TicksPerMinute
                       + second * Spec.TicksPerSecond
                       + subTick;

            return new WDateTime(ticks);
        }

        /// <summary>
        /// Sestaví <see cref="WDateTime"/> z hotového data a hotového času dne.
        /// </summary>
        /// <exception cref="OverflowException">Pokud výsledek přeteče <c>long</c>.</exception>
        public WDateTime Create(WDateOnly date, WTimeOnly time)
            => new(checked(date.DayIndex * Spec.TicksPerDay + time.TicksOfDay));

        /// <summary>
        /// Sestaví <see cref="WDateTime"/> z data zarovnaného na 00:00:00.
        /// </summary>
        /// <exception cref="OverflowException">Pokud výsledek přeteče <c>long</c>.</exception>
        public WDateTime Create(WDateOnly date)
            => new(checked(date.DayIndex * Spec.TicksPerDay));

        #endregion

        #region WDateTime — dekompozice

        /// <summary>
        /// Rozloží <see cref="WDateTime"/> na všechny časové složky.
        /// </summary>
        /// <param name="dt">Okamžik k rozložení.</param>
        /// <returns>
        /// Tuple (year, month, day, hour, minute, second, subTick).
        /// </returns>
        public (int year, int month, int day, int hour, int minute, int second, long subTick)
            GetParts(WDateTime dt)
        {
            long dayIndex = Math.DivRem(dt.WorldTicks, Spec.TicksPerDay, out long rest);
            var (year, month, day) = Spec.Calendar.DateFromDays(dayIndex);

            int hour   = (int)(rest / Spec.TicksPerHour);   rest %= Spec.TicksPerHour;
            int minute = (int)(rest / Spec.TicksPerMinute);  rest %= Spec.TicksPerMinute;
            int second = (int)(rest / Spec.TicksPerSecond);  rest %= Spec.TicksPerSecond;

            return (year, month, day, hour, minute, second, rest);
        }

        /// <summary>
        /// Vrátí datovou složku okamžiku jako <see cref="WDateOnly"/>.
        /// Časová složka je zahozena.
        /// </summary>
        public WDateOnly GetDate(WDateTime dt)
            => new(dt.WorldTicks / Spec.TicksPerDay);

        /// <summary>
        /// Vrátí časovou složku okamžiku jako <see cref="WTimeOnly"/>.
        /// Datová složka je zahozena.
        /// </summary>
        public WTimeOnly GetTime(WDateTime dt)
        {
            long rem = dt.WorldTicks % Spec.TicksPerDay;
            if (rem < 0) rem += Spec.TicksPerDay;
            return new WTimeOnly(rem);
        }

        /// <summary>
        /// Vrátí den v roce (1-based) pro daný okamžik.
        /// Výpočet jde přes kalendář: rozdíl mezi indexem dne a prvním dnem roku.
        /// </summary>
        public int GetDayOfYear(WDateTime dt)
        {
            long dayIndex              = dt.WorldTicks / Spec.TicksPerDay;
            var (year, _, _)           = Spec.Calendar.DateFromDays(dayIndex);
            long firstDayOfYear        = Spec.Calendar.DaysFromDate(year, 1, 1);
            return (int)(dayIndex - firstDayOfYear) + 1;
        }

        #endregion

        #region WDateTime — aritmetika

        /// <summary>
        /// Přičte dny k okamžiku.
        /// </summary>
        public WDateTime AddDays(WDateTime dt, long days)
            => new(dt.WorldTicks + days * Spec.TicksPerDay);

        /// <summary>
        /// Přičte hodiny k okamžiku.
        /// </summary>
        public WDateTime AddHours(WDateTime dt, long hours)
            => new(dt.WorldTicks + hours * Spec.TicksPerHour);

        /// <summary>
        /// Přičte minuty k okamžiku.
        /// </summary>
        public WDateTime AddMinutes(WDateTime dt, long minutes)
            => new(dt.WorldTicks + minutes * Spec.TicksPerMinute);

        /// <summary>
        /// Přičte sekundy k okamžiku.
        /// </summary>
        public WDateTime AddSeconds(WDateTime dt, long seconds)
            => new(dt.WorldTicks + seconds * Spec.TicksPerSecond);

        /// <summary>
        /// Vrátí okamžik se zachovaným časem dne, ale novým datem.
        /// </summary>
        public WDateTime WithDate(WDateTime dt, WDateOnly date)
            => Create(date, GetTime(dt));

        /// <summary>
        /// Vrátí okamžik se zachovaným datem, ale novým časem dne.
        /// </summary>
        public WDateTime WithTime(WDateTime dt, WTimeOnly time)
            => Create(GetDate(dt), time);

        #endregion

        #region WDateTime — parsování

        /// <summary>
        /// Parsuje okamžik ze řetězce ve formátu <c>YYYY-MM-DDTHH:MM:SS[.frac]</c>.
        /// Akceptuje rovněž mezeru místo <c>T</c> a volitelný suffix <c>Z</c>/<c>z</c>.
        /// </summary>
        /// <remarks>
        /// Subtiky lze zapsat dvěma způsoby:
        /// <list type="bullet">
        ///   <item><c>.123W</c> — raw worldTicks pod sekundou (round-trip formát)</item>
        ///   <item><c>.123</c>  — desetinné zlomky sekundy (převádí se na worldTicks)</item>
        /// </list>
        /// </remarks>
        /// <exception cref="FormatException">Neplatný formát nebo datum neexistuje v kalendáři.</exception>
        public WDateTime Parse(string text)
            => TryParse(text.AsSpan(), out var v)
                ? v
                : throw new FormatException($"Neplatný WDateTime: '{text}'.");

        /// <summary>
        /// Pokusí se parsovat okamžik ze řetězce.
        /// </summary>
        /// <param name="text">Řetězec k parsování.</param>
        /// <param name="value">Výstupní okamžik; <c>default</c> při neúspěchu.</param>
        /// <returns><c>true</c> pokud parsování uspělo.</returns>
        public bool TryParse(string? text, out WDateTime value)
            => string.IsNullOrWhiteSpace(text)
                ? (value = default) == default && false
                : TryParse(text.AsSpan(), out value);

        /// <summary>
        /// Pokusí se parsovat okamžik ze span znaků (bez alokace stringu).
        /// </summary>
        /// <param name="input">Span ke čtení.</param>
        /// <param name="value">Výstupní okamžik; <c>default</c> při neúspěchu.</param>
        /// <returns><c>true</c> pokud parsování uspělo.</returns>
        public bool TryParse(ReadOnlySpan<char> input, out WDateTime value)
        {
            value = default;

            // Trim bez alokací
            int start = 0, end = input.Length;
            while (start < end && char.IsWhiteSpace(input[start])) start++;
            while (end > start && char.IsWhiteSpace(input[end - 1])) end--;
            var s = input.Slice(start, end - start);
            if (s.Length == 0) return false;

            // Volitelný suffix Z/z (UTC marker pro Zemi — v herním čase ignorujeme)
            if (s.Length > 0 && (s[^1] == 'Z' || s[^1] == 'z')) s = s[..^1];

            // Rozdělení na datovou a časovou část podle 'T' nebo mezery
            int iT  = s.IndexOf('T');
            int iSP = s.IndexOf(' ');
            int sep = (iT >= 0 && iSP >= 0) ? Math.Min(iT, iSP) : Math.Max(iT, iSP);

            ReadOnlySpan<char> datePart = sep >= 0 ? s[..sep] : s;
            ReadOnlySpan<char> timePart = ReadOnlySpan<char>.Empty;
            bool hasTime = sep >= 0;

            if (hasTime)
            {
                timePart = s[(sep + 1)..].Trim();
                if (timePart.Length == 0) hasTime = false;
            }

            // --- Datum: "YYYY-MM-DD" -----------------------------------------
            if (datePart.Length < 10 || datePart[4] != '-' || datePart[7] != '-') return false;

            if (!TryParseInt(datePart[..4],        min: 1, max: int.MaxValue, out int year))  return false;
            if (!TryParseInt(datePart.Slice(5, 2), min: 1, max: 99,           out int month)) return false;
            if (!TryParseInt(datePart.Slice(8, 2), min: 1, max: 99,           out int day))   return false;

            // --- Čas: "HH:MM:SS[.frac[W]]" -----------------------------------
            int  hour = 0, minute = 0, second = 0;
            long subTick = 0;

            if (hasTime)
            {
                int dot = timePart.IndexOf('.');
                ReadOnlySpan<char> main = dot >= 0 ? timePart[..dot] : timePart;
                ReadOnlySpan<char> frac = dot >= 0 ? timePart[(dot + 1)..] : ReadOnlySpan<char>.Empty;

                if (main.Length < 8 || main[2] != ':' || main[5] != ':') return false;

                if (!TryParseInt(main[..2],        min: 0, max: Spec.HoursPerDay      - 1, out hour))   return false;
                if (!TryParseInt(main.Slice(3, 2), min: 0, max: Spec.MinutesPerHour   - 1, out minute)) return false;
                if (!TryParseInt(main.Slice(6, 2), min: 0, max: Spec.SecondsPerMinute - 1, out second)) return false;

                if (!frac.IsEmpty)
                {
                    // Suffix 'W' = raw worldTicks (round-trip); bez = desetinné zlomky sekundy
                    bool rawTicks = frac[^1] == 'W';
                    if (rawTicks) frac = frac[..^1];
                    if (frac.Length == 0) return false;

                    // Validace: jen číslice
                    for (int i = 0; i < frac.Length; i++)
                        if (frac[i] < '0' || frac[i] > '9') return false;

                    if (rawTicks)
                    {
                        if (!TryParseInt64(frac, min: 0, max: Spec.TicksPerSecond - 1, out subTick))
                            return false;
                    }
                    else
                    {
                        int n = Math.Min(frac.Length, 18);
                        if (!TryParseInt64(frac[..n], min: 0, max: long.MaxValue, out long fracVal))
                            return false;

                        long pow10 = Pow10(n);
                        subTick = (fracVal * Spec.TicksPerSecond) / pow10;
                        if (subTick >= Spec.TicksPerSecond) subTick = Spec.TicksPerSecond - 1;
                    }
                }
            }

            // --- Sestavení výsledku ------------------------------------------
            long days;
            try   { days = Spec.Calendar.DaysFromDate(year, month, day); }
            catch { return false; }

            long ticks = days   * Spec.TicksPerDay
                       + hour   * Spec.TicksPerHour
                       + minute * Spec.TicksPerMinute
                       + second * Spec.TicksPerSecond
                       + subTick;

            value = new WDateTime(ticks);
            return true;
        }

        #endregion

        #region WDateTime — formátování

        /// <summary>
        /// Formátuje <see cref="WDateTime"/> jako čitelný řetězec ve formátu
        /// <c>YYYY-MM-DDTHH:MM:SS[.subW]</c>.
        /// </summary>
        /// <remarks>
        /// Suffix <c>W</c> v subticích označuje raw worldTicks (round-trip formát).
        /// Složka <c>.subW</c> se vypouští pokud jsou subticky nulové.
        /// </remarks>
        /// <example><c>1322-07-04T06:30:00</c></example>
        public string Format(WDateTime dt)
        {
            var (y, mo, d, hh, mm, ss, sub) = GetParts(dt);
            var sb = new StringBuilder(32);

            Append4(sb, y);  sb.Append('-');
            Append2(sb, mo); sb.Append('-');
            Append2(sb, d);  sb.Append('T');
            Append2(sb, hh); sb.Append(':');
            Append2(sb, mm); sb.Append(':');
            Append2(sb, ss);

            if (sub != 0)
            {
                sb.Append('.').Append(sub).Append('W');
            }

            return sb.ToString();
        }

        #endregion

        // =====================================================================
        //  Interní pomocné metody
        // =====================================================================

        #region Interní pomocné metody

        /// <summary>
        /// Parsuje celé číslo <c>int</c> ze span znaků a ověří rozsah [min, max].
        /// Akceptuje pouze číslice — žádné znaménko ani mezery.
        /// </summary>
        private static bool TryParseInt(ReadOnlySpan<char> sp, int min, int max, out int v)
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

        /// <summary>
        /// Parsuje celé číslo <c>long</c> ze span znaků a ověří rozsah [min, max].
        /// Používá se pro subtiky kde rozsah přesahuje <c>int</c>.
        /// </summary>
        private static bool TryParseInt64(ReadOnlySpan<char> sp, long min, long max, out long v)
        {
            long acc = 0;
            for (int i = 0; i < sp.Length; i++)
            {
                int digit = sp[i] - '0';
                if ((uint)digit > 9U) { v = 0; return false; }
                if (acc > (long.MaxValue - digit) / 10) { v = 0; return false; } // overflow guard
                acc = acc * 10 + digit;
            }
            v = acc;
            return v >= min && v <= max;
        }

        /// <summary>Vrátí 10 na <paramref name="n"/>-tou (bez BCL Math.Pow pro celá čísla).</summary>
        private static long Pow10(int n)
        {
            long p = 1;
            for (int i = 0; i < n; i++) p *= 10;
            return p;
        }

        /// <summary>Zapíše číslo jako přesně 2 číslice (s nulou vlevo) do <see cref="StringBuilder"/>.</summary>
        private static void Append2(StringBuilder sb, int v)
        {
            sb.Append((char)('0' + v / 10));
            sb.Append((char)('0' + v % 10));
        }

        /// <summary>
        /// Zapíše rok jako alespoň 4 číslice (s nulami vlevo) do <see cref="StringBuilder"/>.
        /// Roky nad 9999 se vypíší celé — žádné tiché oříznutí (oprava bugu z původního kódu).
        /// </summary>
        private static void Append4(StringBuilder sb, int v)
        {
            if (v >= 10000) { sb.Append(v); return; } // rok > 9999 — vypíšeme celý
            sb.Append((char)('0' + (v / 1000) % 10));
            sb.Append((char)('0' + (v / 100)  % 10));
            sb.Append((char)('0' + (v / 10)   % 10));
            sb.Append((char)('0' +  v          % 10));
        }

        #endregion
    }
}
