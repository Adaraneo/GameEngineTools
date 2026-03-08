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
    /// var tomorrow = _ctx.AddDays(_ctx.Today(), 1);
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
            Spec = spec;
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
        /// <param name="span">Zdrojový interval.</param>
        public double AbsTotalSeconds(WTimeSpan span) => Math.Abs(TotalSeconds(span));

        /// <summary>Absolutní hodnota <see cref="TotalHours"/>. Vždy kladná nebo nulová.</summary>
        /// <param name="span">Zdrojový interval.</param>
        public double AbsTotalHours(WTimeSpan span) => Math.Abs(TotalHours(span));

        /// <summary>Absolutní hodnota <see cref="TotalDays"/>. Vždy kladná nebo nulová.</summary>
        /// <param name="span">Zdrojový interval.</param>
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
            long at  = Math.Abs(span.Ticks);
            long d   = at / Spec.TicksPerDay;           at %= Spec.TicksPerDay;
            int  hh  = (int)(at / Spec.TicksPerHour);   at %= Spec.TicksPerHour;
            int  mm  = (int)(at / Spec.TicksPerMinute);  at %= Spec.TicksPerMinute;
            int  ss  = (int)(at / Spec.TicksPerSecond);  at %= Spec.TicksPerSecond;
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
            var sign          = span.Ticks < 0 ? "-" : "";
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
        /// je den oříznut na poslední platný den výsledného měsíce
        /// (např. den 36 → 28 pokud cílový měsíc má jen 28 dní).
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
        /// Nové datum. Den je oříznut pokud cílový rok má v daném měsíci méně dní
        /// (např. přestupný → nepřestupný rok).
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
    }
}
