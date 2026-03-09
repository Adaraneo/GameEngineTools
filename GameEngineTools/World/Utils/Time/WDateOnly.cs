// WDateOnly.cs
// Copyright (c) 50PSoftware

using System.Text.Json.Serialization;
using GameEngineTools.World.Core.Time;

namespace GameEngineTools.World.Utils.Time
{
    /// <summary>
    /// Reprezentuje datum bez časové složky, uložené jako počet dní od světové epochy
    /// (0 = 1. den 1. měsíce 1. roku).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Čistý datový typ + ambient properties.</b>
    /// Jediným zdrojem pravdy je <see cref="DayIndex"/>.
    /// Properties jako <see cref="Year"/>, <see cref="Month"/>, <see cref="Day"/>
    /// a metody jako <see cref="AddMonths"/> vyžadují nakonfigurovaný <see cref="WWorld"/>.
    /// </para>
    /// <para>
    /// Příklady:
    /// <code>
    /// // Factory (vyžaduje WWorld.Configure)
    /// var date = WDateOnly.New(1322, 7, 4);
    /// var today = WDateOnly.Today;
    ///
    /// // Ambient properties (vyžadují WWorld.Configure)
    /// int year  = date.Year;    // 1322
    /// int month = date.Month;   // 7
    /// int day   = date.Day;     // 4
    ///
    /// // Čistá matematika — nevyžaduje WWorld
    /// var tomorrow = date.AddDays(1);
    /// long left    = date.DaysUntil(deadline);
    /// bool past    = date &lt; today;
    ///
    /// // Kalendářní operace (vyžadují WWorld.Configure)
    /// var nextMonth = date.AddMonths(1);
    /// var nextYear  = date.AddYears(1);
    /// var asDateTime = date.ToDateTime();   // 1322-07-04T00:00:00
    /// </code>
    /// </para>
    /// </remarks>
    public readonly struct WDateOnly :
        IEquatable<WDateOnly>, IComparable<WDateOnly>
    {
        #region Konstrukce

        /// <summary>
        /// Inicializuje nové datum z 0-based indexu dne od světové epochy.
        /// </summary>
        /// <param name="dayIndex">Počet dní od světové epochy (0 = 1/1/1). Nesmí být záporný.</param>
        /// <exception cref="ArgumentOutOfRangeException">Pokud je <paramref name="dayIndex"/> záporný.</exception>
        [JsonConstructor]
        public WDateOnly(long dayIndex)
        {
            if (dayIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(dayIndex));
            DayIndex = dayIndex;
        }

        #endregion

        #region Vlastnosti — raw data

        /// <summary>
        /// Počet dní od světové epochy (0-based). Jediný zdroj pravdy.
        /// </summary>
        public long DayIndex { get; }

        #endregion

        #region Ambient vlastnosti — složky data (vyžadují WWorld.Configure)

        /// <summary>Rok tohoto data.</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public int Year
        {
            get
            {
                var (y, _, _) = WWorld.Spec.Calendar.DateFromDays(DayIndex);
                return y;
            }
        }

        /// <summary>Měsíc tohoto data (1-based).</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public int Month
        {
            get
            {
                var (_, m, _) = WWorld.Spec.Calendar.DateFromDays(DayIndex);
                return m;
            }
        }

        /// <summary>Den v měsíci tohoto data (1-based).</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public int Day
        {
            get
            {
                var (_, _, d) = WWorld.Spec.Calendar.DateFromDays(DayIndex);
                return d;
            }
        }

        #endregion

        #region Static factory

        /// <summary>
        /// Vytvoří datum ze složek (rok, měsíc, den).
        /// Validace probíhá přes <see cref="WWorld.Spec"/> kalendář.
        /// </summary>
        /// <param name="year">Rok (≥ 1).</param>
        /// <param name="month">Měsíc (1-based).</param>
        /// <param name="day">Den v měsíci (1-based).</param>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Pokud složky netvoří platné datum v kalendáři.</exception>
        public static WDateOnly New(int year, int month, int day)
            => new(WWorld.Spec.Calendar.DaysFromDate(year, month, day));

        /// <summary>
        /// Dnešní datum v herním světě (extrahováno z <see cref="WWorld.Clock"/>).
        /// </summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public static WDateOnly Today => new(WWorld.Clock.Now.WorldTicks / WWorld.Spec.TicksPerDay);

        #endregion

        #region Aritmetika — čistá matematika (nevyžaduje WWorld)

        /// <summary>
        /// Přičte zadaný počet dní k datu.
        /// Čistá matematika — nevyžaduje <see cref="WWorld"/>.
        /// </summary>
        /// <param name="days">Počet dní (může být záporný pro posun zpět).</param>
        /// <exception cref="OverflowException">Pokud výsledek přeteče <c>long</c>.</exception>
        public WDateOnly AddDays(long days) => new(checked(DayIndex + days));

        /// <summary>
        /// Vrátí počet dní mezi tímto datem a <paramref name="other"/>.
        /// Kladný výsledek = <paramref name="other"/> je v budoucnosti.
        /// </summary>
        public long DaysUntil(WDateOnly other) => other.DayIndex - DayIndex;

        #endregion

        #region Kalendářní aritmetika (vyžadují WWorld.Configure)

        /// <summary>
        /// Přičte zadaný počet měsíců k datu.
        /// Správně pracuje s libovolným počtem měsíců v roce dle aktivního kalendáře.
        /// </summary>
        /// <param name="months">Počet měsíců (může být záporný).</param>
        /// <returns>
        /// Nové datum. Pokud výsledný měsíc má méně dní, je den oříznut na poslední platný den (clamp).
        /// </returns>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public WDateOnly AddMonths(int months)
        {
            var spec  = WWorld.Spec;
            var cal   = spec.Calendar;
            var (y, m, d) = cal.DateFromDays(DayIndex);

            m += months;
            while (m < 1) { y -= 1; m += cal.MonthsInYear(y); }
            while (m > cal.MonthsInYear(y)) { m -= cal.MonthsInYear(y); y += 1; }

            var dim = cal.DaysInMonth(y, m);
            if (d > dim) d = dim;

            return new WDateOnly(cal.DaysFromDate(y, m, d));
        }

        /// <summary>
        /// Přičte zadaný počet let k datu.
        /// Den je oříznut pokud cílový rok má v daném měsíci méně dní.
        /// </summary>
        /// <param name="years">Počet let (může být záporný).</param>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public WDateOnly AddYears(int years)
        {
            var cal   = WWorld.Spec.Calendar;
            var (y, m, d) = cal.DateFromDays(DayIndex);

            y += years;
            var dim = cal.DaysInMonth(y, m);
            if (d > dim) d = dim;

            return new WDateOnly(cal.DaysFromDate(y, m, d));
        }

        /// <summary>
        /// Převede datum na <see cref="WDateTime"/> zarovnaný na 00:00:00.
        /// </summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        /// <exception cref="OverflowException">Pokud výsledek přeteče <c>long</c>.</exception>
        public WDateTime ToDateTime()
            => new(checked(DayIndex * WWorld.Spec.TicksPerDay));

        #endregion

        #region Porovnávací operátory

        /// <summary>Vrátí <c>true</c> pokud obě data reprezentují stejný den.</summary>
        public static bool operator ==(WDateOnly a, WDateOnly b) => a.DayIndex == b.DayIndex;

        /// <summary>Vrátí <c>true</c> pokud data reprezentují různé dny.</summary>
        public static bool operator !=(WDateOnly a, WDateOnly b) => a.DayIndex != b.DayIndex;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> dříve než <paramref name="b"/>.</summary>
        public static bool operator <(WDateOnly a, WDateOnly b) => a.DayIndex < b.DayIndex;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> dříve nebo ve stejný den jako <paramref name="b"/>.</summary>
        public static bool operator <=(WDateOnly a, WDateOnly b) => a.DayIndex <= b.DayIndex;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> po <paramref name="b"/>.</summary>
        public static bool operator >(WDateOnly a, WDateOnly b) => a.DayIndex > b.DayIndex;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> po nebo ve stejný den jako <paramref name="b"/>.</summary>
        public static bool operator >=(WDateOnly a, WDateOnly b) => a.DayIndex >= b.DayIndex;

        #endregion

        #region Rovnost a hashování

        /// <inheritdoc/>
        public int CompareTo(WDateOnly other) => DayIndex.CompareTo(other.DayIndex);

        /// <inheritdoc/>
        public bool Equals(WDateOnly other) => DayIndex == other.DayIndex;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is WDateOnly d && Equals(d);

        /// <inheritdoc/>
        public override int GetHashCode() => DayIndex.GetHashCode();

        #endregion

        #region Formátování

        /// <summary>
        /// Vrátí datum jako čitelný řetězec ve formátu <c>YYYY-MM-DD</c>.
        /// Vyžaduje nakonfigurovaný <see cref="WWorld"/>. Fallback na DayIndex pokud není.
        /// </summary>
        public override string ToString()
        {
            if (!WWorld.IsConfigured) return DayIndex.ToString();
            var (y, m, d) = WWorld.Spec.Calendar.DateFromDays(DayIndex);
            return $"{y:0000}-{m:00}-{d:00}";
        }

        #endregion
    }
}
