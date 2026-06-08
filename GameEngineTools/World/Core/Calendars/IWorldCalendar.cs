// IWorldCalendar.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Calendars
{
    /// <summary>
    /// A world calendar: converts between absolute day counts and (year, month, day) triples
    /// and reports month/year lengths. All components are 1-based.
    /// </summary>
    public interface IWorldCalendar
    {
        /// <summary>Converts an absolute day count (0 = 1/1/1) to a calendar date.</summary>
        /// <param name="days">Days since the epoch.</param>
        /// <returns>The 1-based year, month and day.</returns>
        (int year, int month, int day) DateFromDays(long days);

        /// <summary>Converts a 1-based calendar date to an absolute day count.</summary>
        /// <param name="year">Year (≥ 1).</param>
        /// <param name="month">Month (≥ 1).</param>
        /// <param name="day">Day (≥ 1).</param>
        long DaysFromDate(int year, int month, int day);

        /// <summary>Number of days in the given month of the given year.</summary>
        int DaysInMonth(int year, int month);

        /// <summary>Number of months in the given year.</summary>
        int MonthsInYear(int year);

        /// <summary>Number of days in the given year.</summary>
        long DaysInYear(int year);

        /// <summary>Returns <c>true</c> if the given year has extra (leap) days.</summary>
        bool IsLeapYear(int year);
    }
}
