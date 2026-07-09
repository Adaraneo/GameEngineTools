// PlanetaryCalendarFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Universe;

using GameEngineTools.World.Core.Calendars;
using GameEngineTools.World.Core.Time;

/// <summary>
/// Builds a <see cref="WorldTimeSpec"/> from the physical parameters of a planet and its star.
/// The day length comes from the planet's rotation, the year length from its orbit (or an explicit
/// override), and the division into months from <see cref="CalendarOptions"/>.
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

        // ── Months ─────────────────────────────────────────────────────────────
        // An explicit month layout (e.g. Gregorian 31/28/31/…) wins; otherwise the year length
        // (explicit or derived from the orbit) is divided evenly across MonthCount months.
        int[] monthLengths;
        if (options.MonthLengths is { Length: > 0 } explicitMonths)
        {
            monthLengths = explicitMonths;
        }
        else
        {
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
            daysInYear   = Math.Max(options.MonthCount, daysInYear);
            monthLengths = BuildMonthLengths(daysInYear, options.MonthCount);
        }

        // ── Leap rule ────────────────────────────────────────────────────────────
        var leapExtra = BuildLeapRule(options);
        var calendar  = new FixedMonthsCalendar(monthLengths, leapExtra, options.LeapMonth);

        return new WorldTimeSpec(
            options.TicksPerSecond,
            options.SecondsPerMinute,
            options.MinutesPerHour,
            hoursPerDay,
            calendar);
    }

    /// <summary>
    /// Builds the per-year leap-day function: the Gregorian rule (every 4th year, except centuries
    /// not divisible by 400) when requested, otherwise a fixed interval, otherwise none.
    /// </summary>
    private static Func<int, int> BuildLeapRule(CalendarOptions options)
    {
        if (options.UseGregorianLeap)
            return y => (y % 4 == 0 && (y % 100 != 0 || y % 400 == 0)) ? options.LeapExtraDays : 0;

        if (options.LeapYearInterval > 0)
            return y => y % options.LeapYearInterval == 0 ? options.LeapExtraDays : 0;

        return _ => 0;
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
