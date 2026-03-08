// WorldTimeContext.cs
// Copyright (c) 50PSoftware

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
    /// Příklad použití:
    /// <code>
    /// // Registrace v DI
    /// services.AddSingleton&lt;WorldTimeContext&gt;();
    ///
    /// // Použití
    /// var now      = _ctx.Now();
    /// var date     = _ctx.CreateDate(1322, 7, 4);
    /// var time     = _ctx.CreateTime(6, 30, 0);
    /// var twoHours = _ctx.Hours(2);
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
        /// <param name="span">Zdrojový interval.</param>
        public double TotalSeconds(WTimeSpan span) => (double)span.Ticks / Spec.TicksPerSecond;

        /// <summary>
        /// Vrátí celkový počet světových minut reprezentovaných intervalem.
        /// Výsledek může být desetinný i záporný.
        /// </summary>
        /// <param name="span">Zdrojový interval.</param>
        public double TotalMinutes(WTimeSpan span) => (double)span.Ticks / Spec.TicksPerMinute;

        /// <summary>
        /// Vrátí celkový počet světových hodin reprezentovaných intervalem.
        /// Výsledek může být desetinný i záporný.
        /// </summary>
        /// <param name="span">Zdrojový interval.</param>
        public double TotalHours(WTimeSpan span) => (double)span.Ticks / Spec.TicksPerHour;

        /// <summary>
        /// Vrátí celkový počet světových dní reprezentovaných intervalem.
        /// Výsledek může být desetinný i záporný.
        /// </summary>
        /// <param name="span">Zdrojový interval.</param>
        public double TotalDays(WTimeSpan span) => (double)span.Ticks / Spec.TicksPerDay;

        /// <summary>Absolutní hodnota <see cref="TotalSeconds"/>. Vždy kladná nebo nulová.</summary>
        public double AbsTotalSeconds(WTimeSpan span) => Math.Abs(TotalSeconds(span));

        /// <summary>Absolutní hodnota <see cref="TotalHours"/>. Vždy kladná nebo nulová.</summary>
        public double AbsTotalHours(WTimeSpan span) => Math.Abs(TotalHours(span));

        /// <summary>Absolutní hodnota <see cref="TotalDays"/>. Vždy kladná nebo nulová.</summary>
        public double AbsTotalDays(WTimeSpan span) => Math.Abs(TotalDays(span));

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
        /// Formátuje <see cref="WTimeSpan"/> jako čitelný řetězec.
        /// </summary>
        /// <param name="span">Interval k formátování.</param>
        /// <returns>
        /// Řetězec ve formátu <c>[-]d.hh:mm:ss[.sub]</c>.
        /// Složka <c>d.</c> se vypouští pokud je počet dní nulový.
        /// Složka <c>.sub</c> se vypouští pokud jsou subticky nulové.
        /// </returns>
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
        {
            long dayIndex = Spec.Calendar.DaysFromDate(year, month, day);
            return new WDateOnly(dayIndex);
        }

        /// <summary>
        /// Extrahuje datovou složku z <see cref="WDateTime"/> a vrátí ji jako <see cref="WDateOnly"/>.
        /// Časová složka (hodiny, minuty, sekundy) je zahozena.
        /// </summary>
        /// <param name="dt">Zdrojový okamžik.</param>
        public WDateOnly DateOf(WDateTime dt)
        {
            long dayIndex = dt.WorldTicks / Spec.TicksPerDay;
            return new WDateOnly(dayIndex);
        }

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
        /// je den oříznut na poslední platný den výsledného měsíce.
        /// </returns>
        public WDateOnly AddMonths(WDateOnly date, int months)
        {
            var (y, m, d) = GetDateParts(date);
            var cal = Spec.Calendar;

            m += months;

            // Podtečení — přejdeme do předchozích let
            while (m < 1)
            {
                y -= 1;
                m += cal.MonthsInYear(y);
            }

            // Přetečení — přejdeme do následujících let
            while (m > cal.MonthsInYear(y))
            {
                m -= cal.MonthsInYear(y);
                y += 1;
            }

            // Clamp dne — pokud cílový měsíc je kratší než původní den
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

            // Clamp dne — délka měsíce se může lišit mezi lety (přestupné roky)
            var dim = Spec.Calendar.DaysInMonth(y, m);
            if (d > dim) d = dim;

            return CreateDate(y, m, d);
        }

        #endregion

        #region WDateOnly — konverze na WDateTime

        /// <summary>
        /// Kombinuje datum s časem dne a vrátí plný okamžik <see cref="WDateTime"/>.
        /// </summary>
        /// <param name="date">Datum.</param>
        /// <param name="time">Čas dne.</param>
        /// <exception cref="OverflowException">Pokud výsledek přeteče <c>long</c>.</exception>
        public WDateTime At(WDateOnly date, WTimeOnly time)
            => new(checked(date.DayIndex * Spec.TicksPerDay + time.TicksOfDay));

        /// <summary>
        /// Vrátí okamžik odpovídající začátku dne (00:00:00) pro zadané datum.
        /// </summary>
        /// <param name="date">Datum.</param>
        /// <exception cref="OverflowException">Pokud výsledek přeteče <c>long</c>.</exception>
        public WDateTime StartOfDay(WDateOnly date)
            => new(checked(date.DayIndex * Spec.TicksPerDay));

        #endregion

        #region WDateOnly — parsování

        /// <summary>
        /// Parsuje datum ze řetězce ve formátu <c>YYYY-MM-DD</c>.
        /// </summary>
        /// <exception cref="FormatException">Pokud řetězec nemá platný formát nebo datum neexistuje v kalendáři.</exception>
        public WDateOnly ParseDate(string text)
            => TryParseDate(text, out var v)
                ? v
                : throw new FormatException($"Neplatný WDateOnly: '{text}'.");

        /// <summary>
        /// Pokusí se parsovat datum ze řetězce ve formátu <c>YYYY-MM-DD</c>.
        /// </summary>
        /// <param name="text">Řetězec k parsování.</param>
        /// <param name="value">Výstupní datum, pokud parsování uspělo; jinak <c>default</c>.</param>
        /// <returns><c>true</c> pokud parsování uspělo, jinak <c>false</c>.</returns>
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
        /// <param name="date">Datum k formátování.</param>
        /// <returns>Řetězec ve formátu <c>YYYY-MM-DD</c>, např. <c>1322-07-04</c>.</returns>
        public string Format(WDateOnly date)
        {
            var (y, m, d) = GetDateParts(date);
            return $"{y:0000}-{m:00}-{d:00}";
        }

        #endregion

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
            if (rem < 0) rem += Spec.TicksPerDay; // ochrana pro záporné WorldTicks
            return new WTimeOnly(rem);
        }

        #endregion

        #region WTimeOnly — dekompozice

        /// <summary>
        /// Rozloží <see cref="WTimeOnly"/> na složky (hodina, minuta, sekunda, subtiky).
        /// </summary>
        /// <param name="time">Čas dne k rozložení.</param>
        /// <returns>
        /// Tuple (hour, minute, second, subTick) odpovídající aktuálnímu <see cref="Spec"/>.
        /// </returns>
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
        /// Vypočítáno z subtické složky relativně vůči <see cref="WorldTimeSpec.TicksPerSecond"/>.
        /// </summary>
        /// <param name="time">Čas dne.</param>
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
        /// <returns>
        /// Nový čas dne v rozsahu [0, TicksPerDay).
        /// Přetečení se zalamuje — např. 23:00 + 3h = 02:00.
        /// </returns>
        public WTimeOnly AddTime(WTimeOnly time, WTimeSpan span)
        {
            long t = time.TicksOfDay + span.Ticks;
            t %= Spec.TicksPerDay;
            if (t < 0) t += Spec.TicksPerDay;
            return new WTimeOnly(t);
        }

        /// <summary>
        /// Přičte zadaný počet hodin k času dne s wraparoundem přes půlnoc.
        /// </summary>
        /// <param name="time">Výchozí čas dne.</param>
        /// <param name="hours">Počet hodin (může být desetinný nebo záporný).</param>
        public WTimeOnly AddHours(WTimeOnly time, double hours)
            => AddTime(time, Hours(hours));

        /// <summary>
        /// Přičte zadaný počet minut k času dne s wraparoundem přes půlnoc.
        /// </summary>
        /// <param name="time">Výchozí čas dne.</param>
        /// <param name="minutes">Počet minut (může být desetinný nebo záporný).</param>
        public WTimeOnly AddMinutes(WTimeOnly time, double minutes)
            => AddTime(time, Minutes(minutes));

        /// <summary>
        /// Přičte zadaný počet sekund k času dne s wraparoundem přes půlnoc.
        /// </summary>
        /// <param name="time">Výchozí čas dne.</param>
        /// <param name="seconds">Počet sekund (může být desetinný nebo záporný).</param>
        public WTimeOnly AddSeconds(WTimeOnly time, double seconds)
            => AddTime(time, Seconds(seconds));

        /// <summary>
        /// Vrátí nejkratší vzdálenost mezi dvěma časy dne jako <see cref="WTimeSpan"/>.
        /// Na rozdíl od <see cref="WTimeOnly.Diff"/> respektuje wraparound přes půlnoc.
        /// </summary>
        /// <param name="a">První čas dne.</param>
        /// <param name="b">Druhý čas dne.</param>
        /// <returns>
        /// Interval v rozsahu (-TicksPerDay/2, TicksPerDay/2].
        /// Kladný výsledek znamená, že <paramref name="b"/> je po <paramref name="a"/>.
        /// </returns>
        public WTimeSpan TimeDiff(WTimeOnly a, WTimeOnly b)
        {
            long diff = b.TicksOfDay - a.TicksOfDay;
            long half = Spec.TicksPerDay / 2;

            // Zalamujeme do rozsahu (-half, half] aby výsledek byl vždy nejkratší cesta
            if (diff >  half) diff -= Spec.TicksPerDay;
            if (diff <= -half) diff += Spec.TicksPerDay;

            return new WTimeSpan(diff);
        }

        #endregion

        #region WTimeOnly — parsování

        /// <summary>
        /// Parsuje čas dne ze řetězce ve formátu <c>HH:MM:SS[.sub]</c>.
        /// </summary>
        /// <exception cref="FormatException">Pokud řetězec nemá platný formát nebo složky jsou mimo rozsah.</exception>
        public WTimeOnly ParseTime(string text)
            => TryParseTime(text, out var v)
                ? v
                : throw new FormatException($"Neplatný WTimeOnly: '{text}'.");

        /// <summary>
        /// Pokusí se parsovat čas dne ze řetězce ve formátu <c>HH:MM:SS[.sub]</c>.
        /// </summary>
        /// <param name="text">Řetězec k parsování.</param>
        /// <param name="value">Výstupní čas, pokud parsování uspělo; jinak <c>default</c>.</param>
        /// <returns><c>true</c> pokud parsování uspělo, jinak <c>false</c>.</returns>
        public bool TryParseTime(string? text, out WTimeOnly value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var s   = text.AsSpan().Trim();
            var dot = s.IndexOf('.');

            ReadOnlySpan<char> main = dot >= 0 ? s[..dot] : s;
            ReadOnlySpan<char> frac = dot >= 0 ? s[(dot + 1)..] : ReadOnlySpan<char>.Empty;

            // Formát hlavní části: "HH:MM:SS" = minimálně 8 znaků
            if (main.Length < 8 || main[2] != ':' || main[5] != ':') return false;

            if (!TryParseInt(main[..2],        min: 0, max: Spec.HoursPerDay      - 1, out int hh)) return false;
            if (!TryParseInt(main.Slice(3, 2), min: 0, max: Spec.MinutesPerHour   - 1, out int mm)) return false;
            if (!TryParseInt(main.Slice(6, 2), min: 0, max: Spec.SecondsPerMinute - 1, out int ss)) return false;

            long sub = 0;
            if (!frac.IsEmpty)
            {
                // Subtiky jsou uloženy jako raw číslo (round-trip s Format)
                if (!TryParseInt64(frac, min: 0, max: Spec.TicksPerSecond - 1, out sub)) return false;
            }

            try   { value = CreateTime(hh, mm, ss, sub); return true; }
            catch { return false; }
        }

        #endregion

        #region WTimeOnly — formátování

        /// <summary>
        /// Formátuje <see cref="WTimeOnly"/> jako čitelný řetězec.
        /// </summary>
        /// <param name="time">Čas dne k formátování.</param>
        /// <returns>
        /// Řetězec ve formátu <c>HH:MM:SS[.sub]</c>.
        /// Složka <c>.sub</c> se vypouští pokud jsou subticky nulové.
        /// </returns>
        /// <example>
        /// <code>
        /// ctx.Format(_ctx.CreateTime(6, 30, 0))  // "06:30:00"
        /// </code>
        /// </example>
        public string Format(WTimeOnly time)
        {
            var (hh, mm, ss, sub) = GetTimeParts(time);
            return sub != 0
                ? $"{hh:00}:{mm:00}:{ss:00}.{sub}"
                : $"{hh:00}:{mm:00}:{ss:00}";
        }

        #endregion

        // =====================================================================

        #region Interní pomocné metody

        /// <summary>
        /// Parsuje celé číslo <c>int</c> ze span znaků a ověří rozsah [<paramref name="min"/>, <paramref name="max"/>].
        /// Akceptuje pouze číslice, žádné znaménko ani mezery.
        /// </summary>
        private static bool TryParseInt(ReadOnlySpan<char> sp, int min, int max, out int v)
        {
            long acc = 0;
            for (int i = 0; i < sp.Length; i++)
            {
                char c = sp[i];
                if (c < '0' || c > '9') { v = 0; return false; }
                acc = acc * 10 + (c - '0');
                if (acc > int.MaxValue)  { v = 0; return false; }
            }
            v = (int)acc;
            return v >= min && v <= max;
        }

        /// <summary>
        /// Parsuje celé číslo <c>long</c> ze span znaků a ověří rozsah [<paramref name="min"/>, <paramref name="max"/>].
        /// Používá se pro subtiky kde rozsah přesahuje <c>int</c>.
        /// </summary>
        private static bool TryParseInt64(ReadOnlySpan<char> sp, long min, long max, out long v)
        {
            long acc = 0;
            for (int i = 0; i < sp.Length; i++)
            {
                char c = sp[i];
                if (c < '0' || c > '9') { v = 0; return false; }
                long digit = c - '0';
                if (acc > (long.MaxValue - digit) / 10) { v = 0; return false; } // overflow guard
                acc = acc * 10 + digit;
            }
            v = acc;
            return v >= min && v <= max;
        }

        #endregion
    }
}
