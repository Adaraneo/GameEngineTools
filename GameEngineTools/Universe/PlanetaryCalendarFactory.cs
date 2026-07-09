// PlanetaryCalendarFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Universe;

using GameEngineTools.World.Core.Calendars;
using GameEngineTools.World.Core.Time;

/// <summary>
/// Builds a <see cref="WorldTimeSpec"/> from the physical parameters of a planet and its star,
/// replacing hand-authored <c>InitWorldClock</c> configuration. The day length comes from the
/// planet's rotation, the year length from its orbit (or an explicit override), and the
/// division into months from <see cref="CalendarOptions"/>.
/// </summary>
public static class PlanetaryCalendarFactory
{
    /// <summary>
    /// Derives a world time specification from the given planetary system.
    /// </summary>
    /// <param name="planet">The planet — supplies the day length via <see cref="PlanetConfig.SiderealRotationHrs"/>.</param>
    /// <param name="orbit">The planet's orbit around the star — supplies the year length via Kepler's third law.</param>
    /// <param name="star">The host star — supplies the gravitational parameter for the orbital period.</param>
    /// <param name="options">Cultural overlay (month count, time subdivisions, optional year override).</param>
    /// <exception cref="ArgumentOutOfRangeException">If <see cref="CalendarOptions.MonthCount"/> is below 1.</exception>
    public static WorldTimeSpec Build(
        PlanetConfig planet,
        OrbitalElements orbit,
        StarPhysics star,
        CalendarOptions? options = null)
    {
        options ??= new CalendarOptions();

        if (options.MonthCount < 1)
            throw new ArgumentOutOfRangeException(
                nameof(options), options.MonthCount, "MonthCount must be at least 1.");

        // ── Day length ─────────────────────────────────────────────────────────
        // Retrograde rotation (negative period) still yields a positive day length.
        var hoursPerDay = Math.Max(1, (int)Math.Round(Math.Abs(planet.SiderealRotationHrs)));

        // ── Year length ────────────────────────────────────────────────────────
        int daysInYear;
        if (options.TargetYearDays is { } target)
        {
            daysInYear = target;
        }
        else
        {
            var secondsPerWorldDay = (double)hoursPerDay * options.MinutesPerHour * options.SecondsPerMinute;
            var orbitalPeriodSec   = orbit.OrbitalPeriodSeconds(star.GravitationalParameter);
            daysInYear = (int)Math.Round(orbitalPeriodSec / secondsPerWorldDay);
        }

        // A calendar needs at least one day per month; clamp degenerate inputs.
        daysInYear = Math.Max(options.MonthCount, daysInYear);

        // ── Months ─────────────────────────────────────────────────────────────
        var monthLengths = BuildMonthLengths(daysInYear, options.MonthCount);
        var leapExtra = options.LeapYearInterval > 0
            ? new Func<int, int>(y => y % options.LeapYearInterval == 0 ? options.LeapExtraDays : 0)
            : _ => 0;
        var calendar = new FixedMonthsCalendar(monthLengths, leapExtra);

        return new WorldTimeSpec(
            options.TicksPerSecond,
            options.SecondsPerMinute,
            options.MinutesPerHour,
            hoursPerDay,
            calendar);
    }

    /// <summary>
    /// Splits <paramref name="daysInYear"/> evenly across <paramref name="monthCount"/> months,
    /// adding any remainder (epagomenal days) to the last month.
    /// </summary>
    private static int[] BuildMonthLengths(int daysInYear, int monthCount)
    {
        var baseLen   = daysInYear / monthCount;
        var remainder = daysInYear % monthCount;
        var months    = new int[monthCount];
        Array.Fill(months, baseLen);
        months[monthCount - 1] += remainder;
        return months;
    }
}
