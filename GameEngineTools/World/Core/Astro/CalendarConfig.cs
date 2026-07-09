// CalendarConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Astro;

using GameEngineTools.Universe;

/// <summary>
/// Flat binding record for the <c>World:Calendar</c> section — the cultural overlay that physics
/// does not determine. The day length comes from the planet's rotation (<see cref="UniverseConfig"/>)
/// and the year length from its orbit; this record supplies how the year is divided into months,
/// how time is subdivided, and the leap rule.
/// </summary>
public sealed record CalendarConfig(
    int    MonthCount       = 12,
    int    TargetYearDays   = 0,          // 0 = derive the year length from the orbit
    long   TicksPerSecond   = 10_000_000,
    int    MinutesPerHour   = 60,
    int    SecondsPerMinute = 60,
    int    LeapYearInterval = 0,          // 0 = no interval-based leap years
    int    LeapExtraDays    = 0,
    int[]? MonthLengths     = null,       // null = derive months by even division
    bool   UseGregorianLeap = false,      // true = Gregorian 4/100/400 rule
    int    LeapMonth        = 0)          // 0 = last month; 2 = February (Gregorian)
{
    /// <summary>Default constructor — a 12-month year derived from the orbit, no leap years.</summary>
    public CalendarConfig() : this(12) { }

    /// <summary>Builds the <see cref="CalendarOptions"/> overlay consumed by <see cref="PlanetaryCalendarFactory"/>.</summary>
    public CalendarOptions ToCalendarOptions() => new(
        MonthCount:       MonthCount,
        TargetYearDays:   TargetYearDays > 0 ? TargetYearDays : null,
        MinutesPerHour:   MinutesPerHour,
        SecondsPerMinute: SecondsPerMinute,
        TicksPerSecond:   TicksPerSecond,
        LeapYearInterval: LeapYearInterval,
        LeapExtraDays:    LeapExtraDays,
        MonthLengths:     MonthLengths,
        UseGregorianLeap: UseGregorianLeap,
        LeapMonth:        LeapMonth > 0 ? LeapMonth : null);
}
