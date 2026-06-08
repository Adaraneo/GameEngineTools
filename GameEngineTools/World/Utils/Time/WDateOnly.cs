// WDateOnly.cs
// Copyright (c) 50PSoftware

using System.Text.Json.Serialization;
using GameEngineTools.World.Core.Time;

namespace GameEngineTools.World.Utils.Time
{
    /// <summary>
    /// Represents a date without a time component, stored as the number of days since the world epoch
    /// (0 = day 1 of month 1 of year 1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pure data type + ambient properties.</b>
    /// The single source of truth is <see cref="DayIndex"/>.
    /// Properties jako <see cref="Year"/>, <see cref="Month"/>, <see cref="Day"/>
    /// and methods such as <see cref="AddMonths"/> require <see cref="WWorld"/> to be configured.
    /// </para>
    /// <para>
    /// Examples:
    /// <code>
    /// // Factory (requires WWorld.Configure)
    /// var date = WDateOnly.New(1322, 7, 4);
    /// var today = WDateOnly.Today;
    ///
    /// // Ambient properties (require WWorld.Configure)
    /// int year  = date.Year;    // 1322
    /// int month = date.Month;   // 7
    /// int day   = date.Day;     // 4
    ///
    /// // Pure math — does not require WWorld
    /// var tomorrow = date.AddDays(1);
    /// long left    = date.DaysUntil(deadline);
    /// bool past    = date &lt; today;
    ///
    /// // Calendar operations (require WWorld.Configure)
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
        /// Initializes a new date from a 0-based day index since the world epoch.
        /// </summary>
        /// <param name="dayIndex">Number of days since the world epoch (0 = 1/1/1). Must not be negative.</param>
        /// <exception cref="ArgumentOutOfRangeException">If <paramref name="dayIndex"/> is negative.</exception>
        [JsonConstructor]
        public WDateOnly(long dayIndex)
        {
            if (dayIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(dayIndex));
            DayIndex = dayIndex;
        }

        #endregion Konstrukce

        #region Vlastnosti — raw data

        /// <summary>
        /// Number of days since the world epoch (0-based). The single source of truth.
        /// </summary>
        public long DayIndex { get; }

        #endregion Vlastnosti — raw data

        #region Ambient vlastnosti — složky data (vyžadují WWorld.Configure)

        /// <summary>Rok tohoto data.</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public int Year
        {
            get
            {
                var (y, _, _) = WWorld.Spec.Calendar.DateFromDays(DayIndex);
                return y;
            }
        }

        /// <summary>Month of this date (1-based).</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public int Month
        {
            get
            {
                var (_, m, _) = WWorld.Spec.Calendar.DateFromDays(DayIndex);
                return m;
            }
        }

        /// <summary>Day of month of this date (1-based).</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public int Day
        {
            get
            {
                var (_, _, d) = WWorld.Spec.Calendar.DateFromDays(DayIndex);
                return d;
            }
        }

        #endregion Ambient vlastnosti — složky data (vyžadují WWorld.Configure)

        #region Static factory

        /// <summary>
        /// Creates a date from its components (year, month, day).
        /// Validation goes through the <see cref="WWorld.Spec"/> calendar.
        /// </summary>
        /// <param name="year">Rok (≥ 1).</param>
        /// <param name="month">Month (1-based).</param>
        /// <param name="day">Day of month (1-based).</param>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        /// <exception cref="ArgumentOutOfRangeException">If the components do not form a valid date in the calendar.</exception>
        public static WDateOnly New(int year, int month, int day)
            => new(WWorld.Spec.Calendar.DaysFromDate(year, month, day));

        /// <summary>
        /// Today's date in the game world (extracted from <see cref="WWorld.Clock"/>).
        /// </summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public static WDateOnly Today => new(WWorld.Clock.Now.WorldTicks / WWorld.Spec.TicksPerDay);

        #endregion Static factory

        #region Aritmetika — čistá matematika (nevyžaduje WWorld)

        /// <summary>
        /// Adds the given number of days to the date.
        /// Pure math — does not require <see cref="WWorld"/>.
        /// </summary>
        /// <param name="days">Number of days (may be negative to move backward).</param>
        /// <exception cref="OverflowException">If the result overflows <c>long</c>.</exception>
        public WDateOnly AddDays(long days) => new(checked(DayIndex + days));

        /// <summary>
        /// Returns the number of days between this date and <paramref name="other"/>.
        /// A positive result means <paramref name="other"/> is in the future.
        /// </summary>
        public long DaysUntil(WDateOnly other) => other.DayIndex - DayIndex;

        #endregion Aritmetika — čistá matematika (nevyžaduje WWorld)

        #region Kalendářní aritmetika (vyžadují WWorld.Configure)

        /// <summary>
        /// Adds the given number of months to the date.
        /// Works correctly with any number of months per year per the active calendar.
        /// </summary>
        /// <param name="months">Number of months (may be negative).</param>
        /// <returns>
        /// The new date. If the resulting month has fewer days, the day is clamped to the last valid day.
        /// </returns>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public WDateOnly AddMonths(int months)
        {
            var spec = WWorld.Spec;
            var cal = spec.Calendar;
            var (y, m, d) = cal.DateFromDays(DayIndex);

            m += months;
            while (m < 1) { y -= 1; m += cal.MonthsInYear(y); }
            while (m > cal.MonthsInYear(y)) { m -= cal.MonthsInYear(y); y += 1; }

            var dim = cal.DaysInMonth(y, m);
            if (d > dim) d = dim;

            return new WDateOnly(cal.DaysFromDate(y, m, d));
        }

        /// <summary>
        /// Adds the given number of years to the date.
        /// The day is clamped if the target year has fewer days in the given month.
        /// </summary>
        /// <param name="years">Number of years (may be negative).</param>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public WDateOnly AddYears(int years)
        {
            var cal = WWorld.Spec.Calendar;
            var (y, m, d) = cal.DateFromDays(DayIndex);

            y += years;
            var dim = cal.DaysInMonth(y, m);
            if (d > dim) d = dim;

            return new WDateOnly(cal.DaysFromDate(y, m, d));
        }

        /// <summary>
        /// Converts the date to a <see cref="WDateTime"/> aligned to 00:00:00.
        /// </summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        /// <exception cref="OverflowException">If the result overflows <c>long</c>.</exception>
        public WDateTime ToDateTime()
            => new(checked(DayIndex * WWorld.Spec.TicksPerDay));

        #endregion Kalendářní aritmetika (vyžadují WWorld.Configure)

        #region Porovnávací operátory

        /// <summary>Returns <c>true</c> if both dates represent the same day.</summary>
        public static bool operator ==(WDateOnly a, WDateOnly b) => a.DayIndex == b.DayIndex;

        /// <summary>Returns <c>true</c> if the dates represent different days.</summary>
        public static bool operator !=(WDateOnly a, WDateOnly b) => a.DayIndex != b.DayIndex;

        /// <summary>Returns <c>true</c> if <paramref name="a"/> is earlier than <paramref name="b"/>.</summary>
        public static bool operator <(WDateOnly a, WDateOnly b) => a.DayIndex < b.DayIndex;

        /// <summary>Returns <c>true</c> if <paramref name="a"/> is earlier than or on the same day as <paramref name="b"/>.</summary>
        public static bool operator <=(WDateOnly a, WDateOnly b) => a.DayIndex <= b.DayIndex;

        /// <summary>Returns <c>true</c> if <paramref name="a"/> is later than <paramref name="b"/>.</summary>
        public static bool operator >(WDateOnly a, WDateOnly b) => a.DayIndex > b.DayIndex;

        /// <summary>Returns <c>true</c> if <paramref name="a"/> is later than or on the same day as <paramref name="b"/>.</summary>
        public static bool operator >=(WDateOnly a, WDateOnly b) => a.DayIndex >= b.DayIndex;

        #endregion Porovnávací operátory

        #region Rovnost a hashování

        /// <inheritdoc/>
        public int CompareTo(WDateOnly other) => DayIndex.CompareTo(other.DayIndex);

        /// <inheritdoc/>
        public bool Equals(WDateOnly other) => DayIndex == other.DayIndex;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is WDateOnly d && Equals(d);

        /// <inheritdoc/>
        public override int GetHashCode() => DayIndex.GetHashCode();

        #endregion Rovnost a hashování

        #region Formátování

        /// <summary>
        /// Returns the date as a readable string in the format <c>YYYY-MM-DD</c>.
        /// Requires <see cref="WWorld"/> to be configured. Falls back to DayIndex otherwise.
        /// </summary>
        public override string ToString()
        {
            if (!WWorld.IsConfigured) return DayIndex.ToString();
            var (y, m, d) = WWorld.Spec.Calendar.DateFromDays(DayIndex);
            return $"{y:0000}-{m:00}-{d:00}";
        }

        #endregion Formátování
    }
}
