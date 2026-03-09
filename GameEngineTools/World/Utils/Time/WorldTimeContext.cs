//// WorldTimeContext.cs
//// Copyright (c) 50PSoftware

//using System.Text;
//using GameEngineTools.World.Utils.Time;

//namespace GameEngineTools.World.Core.Time
//{
//    /// <summary>
//    /// Fasáda nad světovým časem zachovaná pro zpětnou kompatibilitu.
//    /// </summary>
//    /// <remarks>
//    /// <para>
//    /// <b>Status: legacy wrapper.</b> Od redesignu (Varianta A — Ambient Spec)
//    /// mají W-typy (<see cref="WDateTime"/>, <see cref="WDateOnly"/> atd.)
//    /// vlastní properties a metody — <c>ctx.GetParts(dt)</c> se píše jako
//    /// <c>dt.Year</c>, <c>ctx.AddMonths(date, 3)</c> jako <c>date.AddMonths(3)</c> atd.
//    /// </para>
//    /// <para>
//    /// <b>Kdy stále používat WorldTimeContext:</b>
//    /// <list type="bullet">
//    ///   <item>Kdykoli potřebuješ factory z <c>double</c> hodnot přes DI:
//    ///         <c>_wtctx.Hours(2.5)</c> — tyto metody jsou pohodlný zkrácený zápis.</item>
//    ///   <item>Formátování s explicitním předáváním kontextu (starý kód).</item>
//    ///   <item>JSON konvertery registrované přes DI (zpětná kompatibilita).</item>
//    /// </list>
//    /// Nový kód preferuj přes <see cref="WWorld"/> a metody přímo na W-typech.
//    /// </para>
//    /// <para>
//    /// Příklad registrace (DI):
//    /// <code>
//    /// services.AddSingleton&lt;WorldTimeContext&gt;();
//    /// </code>
//    /// </para>
//    /// </remarks>
//    public sealed class WorldTimeContext
//    {
//        #region Konstrukce

//        /// <summary>
//        /// Inicializuje kontext. Přijímá spec a clock pro zpětnou kompatibilitu,
//        /// ale interně deleguje na <see cref="WWorld"/>.
//        /// </summary>
//        /// <param name="spec">Specifikace světového času.</param>
//        /// <param name="clock">
//        /// Zdroj aktuálního světového času. Musí to být stejná instance jako
//        /// <see cref="WWorld.Clock"/> — jinak se <see cref="Now"/> a
//        /// <see cref="WDateTime.Now"/> rozcházejí.
//        /// </param>
//        public WorldTimeContext(WorldTimeSpec spec, IWorldClock clock)
//        {
//            Spec   = spec;
//            _clock = clock;
//        }

//        private readonly IWorldClock _clock;

//        #endregion

//        #region Vlastnosti

//        /// <summary>
//        /// Specifikace světového času platná pro tento kontext.
//        /// </summary>
//        public WorldTimeSpec Spec { get; }

//        #endregion

//        // =====================================================================
//        //  WTimeSpan
//        // =====================================================================

//        #region WTimeSpan — factory (z lidských jednotek na ticky)

//        /// <summary>Vytvoří interval odpovídající zadanému počtu světových sekund.</summary>
//        /// <param name="s">Počet sekund (může být desetinný).</param>
//        public WTimeSpan Seconds(double s) => WTimeSpan.FromSeconds(s);

//        /// <summary>Vytvoří interval odpovídající zadanému počtu světových minut.</summary>
//        /// <param name="m">Počet minut (může být desetinný).</param>
//        public WTimeSpan Minutes(double m) => WTimeSpan.FromMinutes(m);

//        /// <summary>Vytvoří interval odpovídající zadanému počtu světových hodin.</summary>
//        /// <param name="h">Počet hodin (může být desetinný).</param>
//        public WTimeSpan Hours(double h) => WTimeSpan.FromHours(h);

//        /// <summary>Vytvoří interval odpovídající zadanému počtu světových dní.</summary>
//        /// <param name="d">Počet dní (může být desetinný).</param>
//        public WTimeSpan Days(double d) => WTimeSpan.FromDays(d);

//        #endregion

//        #region WTimeSpan — konverze (z tiků na lidské jednotky)

//        /// <summary>Celkový počet světových sekund. Může být desetinný i záporný.</summary>
//        public double TotalSeconds(WTimeSpan span) => span.TotalSeconds;

//        /// <summary>Celkový počet světových minut. Může být desetinný i záporný.</summary>
//        public double TotalMinutes(WTimeSpan span) => span.TotalMinutes;

//        /// <summary>Celkový počet světových hodin. Může být desetinný i záporný.</summary>
//        public double TotalHours(WTimeSpan span) => span.TotalHours;

//        /// <summary>Celkový počet světových dní. Může být desetinný i záporný.</summary>
//        public double TotalDays(WTimeSpan span) => span.TotalDays;

//        /// <summary>Absolutní hodnota TotalSeconds. Vždy kladná nebo nulová.</summary>
//        public double AbsTotalSeconds(WTimeSpan span) => Math.Abs(span.TotalSeconds);

//        /// <summary>Absolutní hodnota TotalHours. Vždy kladná nebo nulová.</summary>
//        public double AbsTotalHours(WTimeSpan span) => Math.Abs(span.TotalHours);

//        /// <summary>Absolutní hodnota TotalDays. Vždy kladná nebo nulová.</summary>
//        public double AbsTotalDays(WTimeSpan span) => Math.Abs(span.TotalDays);

//        #endregion

//        #region WTimeSpan — dekompozice a formátování

//        /// <summary>
//        /// Rozloží interval na zobrazitelné složky (pracuje s absolutní hodnotou).
//        /// </summary>
//        /// <returns>
//        /// Tuple: (dny, hodiny, minuty, sekundy, subtiky).
//        /// </returns>
//        public (long days, int hours, int minutes, int seconds, long subTicks)
//            DeconstructSpan(WTimeSpan span)
//        {
//            long at = Math.Abs(span.Ticks);
//            long d  = at / Spec.TicksPerDay;   at %= Spec.TicksPerDay;
//            int  hh = (int)(at / Spec.TicksPerHour);   at %= Spec.TicksPerHour;
//            int  mm = (int)(at / Spec.TicksPerMinute);  at %= Spec.TicksPerMinute;
//            int  ss = (int)(at / Spec.TicksPerSecond);  at %= Spec.TicksPerSecond;
//            return (d, hh, mm, ss, at);
//        }

//        /// <summary>
//        /// Formátuje <see cref="WTimeSpan"/> jako <c>[-]d.hh:mm:ss[.sub]</c>.
//        /// </summary>
//        public string Format(WTimeSpan span) => span.ToString();

//        #endregion

//        // =====================================================================
//        //  WDateOnly
//        // =====================================================================

//        #region WDateOnly — factory

//        /// <summary>Vytvoří <see cref="WDateOnly"/> ze složek (rok, měsíc, den).</summary>
//        public WDateOnly CreateDate(int year, int month, int day)
//            => WDateOnly.New(year, month, day);

//        /// <summary>Extrahuje datovou složku z <see cref="WDateTime"/>.</summary>
//        public WDateOnly DateOf(WDateTime dt) => dt.Date;

//        #endregion

//        #region WDateOnly — dekompozice

//        /// <summary>Rozloží <see cref="WDateOnly"/> na složky (rok, měsíc, den).</summary>
//        public (int year, int month, int day) GetDateParts(WDateOnly date)
//            => (date.Year, date.Month, date.Day);

//        #endregion

//        #region WDateOnly — kalendářní aritmetika

//        /// <summary>Přičte zadaný počet měsíců k datu.</summary>
//        public WDateOnly AddMonths(WDateOnly date, int months) => date.AddMonths(months);

//        /// <summary>Přičte zadaný počet let k datu.</summary>
//        public WDateOnly AddYears(WDateOnly date, int years) => date.AddYears(years);

//        #endregion

//        #region WDateOnly — konverze na WDateTime

//        /// <summary>Kombinuje datum s časem dne a vrátí plný okamžik.</summary>
//        public WDateTime At(WDateOnly date, WTimeOnly time) => WDateTime.New(date, time);

//        /// <summary>Vrátí okamžik odpovídající začátku dne (00:00:00).</summary>
//        public WDateTime StartOfDay(WDateOnly date) => WDateTime.New(date);

//        #endregion

//        #region WDateOnly — parsování

//        /// <summary>Parsuje datum ze řetězce <c>YYYY-MM-DD</c>.</summary>
//        /// <exception cref="FormatException">Neplatný formát nebo datum neexistuje.</exception>
//        public WDateOnly ParseDate(string text)
//            => TryParseDate(text, out var v)
//                ? v
//                : throw new FormatException($"Neplatný WDateOnly: '{text}'.");

//        /// <summary>Pokusí se parsovat datum ze řetězce <c>YYYY-MM-DD</c>.</summary>
//        public bool TryParseDate(string? text, out WDateOnly value)
//        {
//            value = default;
//            if (string.IsNullOrWhiteSpace(text)) return false;

//            var s = text.AsSpan().Trim();
//            if (s.Length < 10 || s[4] != '-' || s[7] != '-') return false;

//            if (!TryParseInt(s[..4],        1, int.MaxValue, out int y))  return false;
//            if (!TryParseInt(s.Slice(5, 2), 1, 99,           out int mo)) return false;
//            if (!TryParseInt(s.Slice(8, 2), 1, 99,           out int da)) return false;

//            try { value = WDateOnly.New(y, mo, da); return true; }
//            catch { return false; }
//        }

//        #endregion

//        #region WDateOnly — formátování

//        /// <summary>Formátuje <see cref="WDateOnly"/> jako <c>YYYY-MM-DD</c>.</summary>
//        public string Format(WDateOnly date) => date.ToString();

//        #endregion

//        // =====================================================================
//        //  WTimeOnly
//        // =====================================================================

//        #region WTimeOnly — factory

//        /// <summary>Vytvoří <see cref="WTimeOnly"/> ze složek.</summary>
//        public WTimeOnly CreateTime(int hour, int minute, int second, long subTick = 0)
//            => WTimeOnly.New(hour, minute, second, subTick);

//        /// <summary>Extrahuje časovou složku dne z <see cref="WDateTime"/>.</summary>
//        public WTimeOnly TimeOf(WDateTime dt) => dt.TimeOfDay;

//        #endregion

//        #region WTimeOnly — dekompozice

//        /// <summary>Rozloží <see cref="WTimeOnly"/> na složky.</summary>
//        public (int hour, int minute, int second, long subTick) GetTimeParts(WTimeOnly time)
//        {
//            long rem    = time.TicksOfDay;
//            int  hour   = (int)(rem / Spec.TicksPerHour);   rem %= Spec.TicksPerHour;
//            int  minute = (int)(rem / Spec.TicksPerMinute); rem %= Spec.TicksPerMinute;
//            int  second = (int)(rem / Spec.TicksPerSecond); rem %= Spec.TicksPerSecond;
//            return (hour, minute, second, rem);
//        }

//        /// <summary>Vrátí počet milisekund v rámci aktuální světové sekundy (0..999).</summary>
//        public int GetMillisecond(WTimeOnly time)
//        {
//            long subTick = time.TicksOfDay % Spec.TicksPerSecond;
//            return (int)((subTick * 1000L) / Spec.TicksPerSecond);
//        }

//        #endregion

//        #region WTimeOnly — aritmetika

//        /// <summary>Přičte interval k času dne s wraparoundem přes půlnoc.</summary>
//        public WTimeOnly AddTime(WTimeOnly time, WTimeSpan span) => time.Add(span);

//        /// <summary>Přičte hodiny k času dne s wraparoundem.</summary>
//        public WTimeOnly AddHours(WTimeOnly time, double hours) => time.AddHours(hours);

//        /// <summary>Přičte minuty k času dne s wraparoundem.</summary>
//        public WTimeOnly AddMinutes(WTimeOnly time, double minutes) => time.AddMinutes(minutes);

//        /// <summary>Přičte sekundy k času dne s wraparoundem.</summary>
//        public WTimeOnly AddSeconds(WTimeOnly time, double seconds) => time.AddSeconds(seconds);

//        /// <summary>
//        /// Vrátí nejkratší vzdálenost mezi dvěma časy dne s wraparoundem přes půlnoc.
//        /// Výsledek je v rozsahu (-TicksPerDay/2, TicksPerDay/2].
//        /// </summary>
//        public WTimeSpan TimeDiff(WTimeOnly a, WTimeOnly b)
//        {
//            long diff = b.TicksOfDay - a.TicksOfDay;
//            long half = Spec.TicksPerDay / 2;
//            if (diff >  half) diff -= Spec.TicksPerDay;
//            if (diff <= -half) diff += Spec.TicksPerDay;
//            return new WTimeSpan(diff);
//        }

//        #endregion

//        #region WTimeOnly — parsování

//        /// <summary>Parsuje čas dne ze řetězce <c>HH:MM:SS[.sub]</c>.</summary>
//        /// <exception cref="FormatException">Neplatný formát nebo složky mimo rozsah.</exception>
//        public WTimeOnly ParseTime(string text)
//            => TryParseTime(text, out var v)
//                ? v
//                : throw new FormatException($"Neplatný WTimeOnly: '{text}'.");

//        /// <summary>Pokusí se parsovat čas dne ze řetězce <c>HH:MM:SS[.sub]</c>.</summary>
//        public bool TryParseTime(string? text, out WTimeOnly value)
//        {
//            value = default;
//            if (string.IsNullOrWhiteSpace(text)) return false;

//            var s   = text.AsSpan().Trim();
//            var dot = s.IndexOf('.');

//            ReadOnlySpan<char> main = dot >= 0 ? s[..dot] : s;
//            ReadOnlySpan<char> frac = dot >= 0 ? s[(dot + 1)..] : ReadOnlySpan<char>.Empty;

//            if (main.Length < 8 || main[2] != ':' || main[5] != ':') return false;

//            if (!TryParseInt(main[..2],        0, Spec.HoursPerDay - 1,      out int hh)) return false;
//            if (!TryParseInt(main.Slice(3, 2), 0, Spec.MinutesPerHour - 1,   out int mm)) return false;
//            if (!TryParseInt(main.Slice(6, 2), 0, Spec.SecondsPerMinute - 1, out int ss)) return false;

//            long sub = 0;
//            if (!frac.IsEmpty)
//            {
//                if (!TryParseInt64(frac, 0, Spec.TicksPerSecond - 1, out sub)) return false;
//            }

//            try { value = WTimeOnly.New(hh, mm, ss, sub); return true; }
//            catch { return false; }
//        }

//        #endregion

//        #region WTimeOnly — formátování

//        /// <summary>Formátuje <see cref="WTimeOnly"/> jako <c>HH:MM:SS</c>.</summary>
//        public string Format(WTimeOnly time) => time.ToString();

//        #endregion

//        // =====================================================================
//        //  WDateTime
//        // =====================================================================

//        #region WDateTime — factory a aktuální čas

//        /// <summary>Vrátí aktuální herní čas.</summary>
//        public WDateTime Now() => WDateTime.Now;

//        /// <summary>
//        /// Maximální bezpečně reprezentovatelná hodnota.
//        /// </summary>
//        public WDateTime MaxValue => WDateTime.MaxValue;

//        /// <summary>Vytvoří okamžik ze všech časových složek.</summary>
//        public WDateTime Create(
//            int year, int month, int day,
//            int hour = 0, int minute = 0, int second = 0, long subTick = 0)
//            => WDateTime.New(year, month, day, hour, minute, second, subTick);

//        /// <summary>Sestaví <see cref="WDateTime"/> z data a času dne.</summary>
//        public WDateTime Create(WDateOnly date, WTimeOnly time) => WDateTime.New(date, time);

//        /// <summary>Sestaví <see cref="WDateTime"/> zarovnaný na 00:00:00.</summary>
//        public WDateTime Create(WDateOnly date) => WDateTime.New(date);

//        #endregion

//        #region WDateTime — dekompozice

//        /// <summary>Rozloží <see cref="WDateTime"/> na všechny časové složky.</summary>
//        public (int year, int month, int day, int hour, int minute, int second, long subTick)
//            GetParts(WDateTime dt)
//        {
//            var spec     = Spec;
//            long dayIndex = Math.DivRem(dt.WorldTicks, spec.TicksPerDay, out long rest);
//            var (year, month, day) = spec.Calendar.DateFromDays(dayIndex);
//            int  hour    = (int)(rest / spec.TicksPerHour);   rest %= spec.TicksPerHour;
//            int  minute  = (int)(rest / spec.TicksPerMinute); rest %= spec.TicksPerMinute;
//            int  second  = (int)(rest / spec.TicksPerSecond); rest %= spec.TicksPerSecond;
//            return (year, month, day, hour, minute, second, rest);
//        }

//        /// <summary>Vrátí datovou složku okamžiku jako <see cref="WDateOnly"/>.</summary>
//        public WDateOnly GetDate(WDateTime dt) => dt.Date;

//        /// <summary>Vrátí časovou složku okamžiku jako <see cref="WTimeOnly"/>.</summary>
//        public WTimeOnly GetTime(WDateTime dt) => dt.TimeOfDay;

//        /// <summary>Vrátí den v roce (1-based) pro daný okamžik.</summary>
//        public int GetDayOfYear(WDateTime dt) => dt.DayOfYear;

//        #endregion

//        #region WDateTime — aritmetika

//        /// <summary>Přičte dny k okamžiku.</summary>
//        public WDateTime AddDays(WDateTime dt, long days)    => dt.AddDays(days);

//        /// <summary>Přičte hodiny k okamžiku.</summary>
//        public WDateTime AddHours(WDateTime dt, long hours)  => dt.AddHours(hours);

//        /// <summary>Přičte minuty k okamžiku.</summary>
//        public WDateTime AddMinutes(WDateTime dt, long mins) => dt.AddMinutes(mins);

//        /// <summary>Přičte sekundy k okamžiku.</summary>
//        public WDateTime AddSeconds(WDateTime dt, long secs) => dt.AddSeconds(secs);

//        /// <summary>Vrátí okamžik se zachovaným časem dne, ale novým datem.</summary>
//        public WDateTime WithDate(WDateTime dt, WDateOnly date) => dt.WithDate(date);

//        /// <summary>Vrátí okamžik se zachovaným datem, ale novým časem dne.</summary>
//        public WDateTime WithTime(WDateTime dt, WTimeOnly time) => dt.WithTime(time);

//        #endregion

//        #region WDateTime — parsování

//        /// <summary>
//        /// Parsuje okamžik ze řetězce <c>YYYY-MM-DDTHH:MM:SS[.frac]</c>.
//        /// </summary>
//        /// <exception cref="FormatException">Neplatný formát.</exception>
//        public WDateTime Parse(string text) => WDateTime.Parse(text);

//        /// <summary>Pokusí se parsovat okamžik ze řetězce.</summary>
//        public bool TryParse(string? text, out WDateTime value) => WDateTime.TryParse(text, out value);

//        /// <summary>Pokusí se parsovat okamžik ze span znaků.</summary>
//        public bool TryParse(ReadOnlySpan<char> input, out WDateTime value) => WDateTime.TryParse(input, out value);

//        #endregion

//        #region WDateTime — formátování

//        /// <summary>Formátuje <see cref="WDateTime"/> jako <c>YYYY-MM-DDTHH:MM:SS</c>.</summary>
//        public string Format(WDateTime dt) => dt.ToString();

//        #endregion

//        // =====================================================================
//        //  Interní pomocné metody (zachovány pro parsování TryParseDate / TryParseTime)
//        // =====================================================================

//        #region Interní pomocné metody

//        /// <summary>Parsuje int ze span — pouze číslice, žádné znaménko.</summary>
//        private static bool TryParseInt(ReadOnlySpan<char> sp, int min, int max, out int v)
//        {
//            long acc = 0;
//            for (int i = 0; i < sp.Length; i++)
//            {
//                int digit = sp[i] - '0';
//                if ((uint)digit > 9U) { v = 0; return false; }
//                acc = acc * 10 + digit;
//                if (acc > int.MaxValue) { v = 0; return false; }
//            }
//            v = (int)acc;
//            return v >= min && v <= max;
//        }

//        /// <summary>Parsuje long ze span — pouze číslice, žádné znaménko.</summary>
//        private static bool TryParseInt64(ReadOnlySpan<char> sp, long min, long max, out long v)
//        {
//            long acc = 0;
//            for (int i = 0; i < sp.Length; i++)
//            {
//                int digit = sp[i] - '0';
//                if ((uint)digit > 9U) { v = 0; return false; }
//                if (acc > (long.MaxValue - digit) / 10) { v = 0; return false; }
//                acc = acc * 10 + digit;
//            }
//            v = acc;
//            return v >= min && v <= max;
//        }

//        #endregion
//    }
//}
