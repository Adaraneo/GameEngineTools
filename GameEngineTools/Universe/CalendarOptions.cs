// CalendarOptions.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Universe;

/// <summary>
/// Cultural overlay for <see cref="PlanetaryCalendarFactory"/> — the parameters that physics
/// does not determine. The length of the day and year come from the planet and its orbit; how the
/// year is divided into months and how time is subdivided are conventions defined here.
/// </summary>
/// <param name="MonthCount">
/// Number of months the year is split into. The year is divided evenly; any remainder is added to
/// the last month. Must be at least 1.
/// </param>
/// <param name="TargetYearDays">
/// Optional explicit year length in world days. When set, the physical orbital-period calculation
/// is bypassed — useful for design-first worlds that want an exact year length (e.g. 360 days).
/// </param>
/// <param name="MinutesPerHour">Minutes per hour (timekeeping convention, not astronomy). Default 60.</param>
/// <param name="SecondsPerMinute">Seconds per minute. Default 60.</param>
/// <param name="TicksPerSecond">Engine ticks per world second. Default 20.</param>
/// <param name="LeapYearInterval">
/// Every Nth year is a leap year, gaining <paramref name="LeapExtraDays"/> extra days in the last
/// month. <c>0</c> disables leap years.
/// </param>
/// <param name="LeapExtraDays">Extra (epagomenal) days added to the last month in a leap year.</param>
public sealed record CalendarOptions(
    int   MonthCount       = 12,
    int?  TargetYearDays   = null,
    int   MinutesPerHour   = 60,
    int   SecondsPerMinute = 60,
    long  TicksPerSecond   = 20,
    int   LeapYearInterval = 0,
    int   LeapExtraDays    = 0);
