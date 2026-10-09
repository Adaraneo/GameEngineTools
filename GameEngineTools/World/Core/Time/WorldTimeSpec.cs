// WorldTimeSpec.cs
// Copyright (c) 50PSoftware

using GameEngineTools.World.Core.Calendars;

namespace GameEngineTools.World.Core.Time
{
    /// <summary>
    /// Defines a world's time system: the calendar plus the day/hour/minute/second subdivisions
    /// and the world-ticks-per-second base unit. Drives all <c>WDateTime</c> arithmetic.
    /// </summary>
    public sealed class WorldTimeSpec
    {
        /// <summary>The calendar (months, days per month, leap rules).</summary>
        public readonly IWorldCalendar Calendar;

        /// <summary>Number of hours in a day.</summary>
        public readonly int HoursPerDay;

        /// <summary>Number of minutes in an hour.</summary>
        public readonly int MinutesPerHour;

        /// <summary>Number of seconds in a minute.</summary>
        public readonly int SecondsPerMinute;

        /// <summary>How many world ticks make up one world second.</summary>
        public readonly long TicksPerSecond;     // kolik worldTicks je jedna “sekunda světa”

        /// <summary>
        /// Whether the inhabitants' body clock is adapted to this world's day, as it would be for a species that evolved on the planet.
        /// When <c>true</c> the sleep-regulation time constants and sleep durations scale with the day length (see <see cref="BodyClockScale"/>),
        /// so the rhythm looks the same as on Earth on any planet. When <c>false</c> they keep their Earth values in real hours (settlers
        /// whose bodies still run on a 24 h clock; a rhythm that cannot entrain on days far from 24 h).
        /// </summary>
        public readonly bool BodyClockFollowsDay;

        /// <summary>Creates a world time specification.</summary>
        /// <param name="ticksPerSecond">World ticks per world second.</param>
        /// <param name="secondsPerMinute">Seconds per minute.</param>
        /// <param name="minutesPerHour">Minutes per hour.</param>
        /// <param name="hoursPerDay">Hours per day.</param>
        /// <param name="calendar">The world calendar.</param>
        /// <param name="bodyClockFollowsDay">See <see cref="BodyClockFollowsDay"/>. Defaults to <c>true</c>.</param>
        public WorldTimeSpec(
            long ticksPerSecond,
            int secondsPerMinute,
            int minutesPerHour,
            int hoursPerDay,
            IWorldCalendar calendar,
            bool bodyClockFollowsDay = true)
        {
            BodyClockFollowsDay = bodyClockFollowsDay;
            TicksPerSecond = ticksPerSecond;
            SecondsPerMinute = secondsPerMinute;
            MinutesPerHour = minutesPerHour;
            HoursPerDay = hoursPerDay;
            Calendar = calendar;
        }

        /// <summary>Hours in the given calendar year (days in the year × <see cref="HoursPerDay"/>); varies with the calendar and leap rules.</summary>
        /// <param name="year">The calendar year.</param>
        public double HoursInYear(int year) => Calendar.DaysInYear(year) * (double)HoursPerDay;

        /// <summary>
        /// Converts an hour written on a 24-hour clock (how hour-of-day constants such as "cortisol peaks at 8" are
        /// authored) to the same fraction of this world's day; a 40 h day maps 8 to 13.33.
        /// </summary>
        /// <param name="hour24">Hour on a 24-hour clock.</param>
        public double FromClock24(double hour24) => hour24 * HoursPerDay / 24.0;

        /// <summary>Inverse of <see cref="FromClock24"/>: a world hour (or hour difference) expressed on a 24-hour clock.</summary>
        /// <param name="worldHours">Hours of this world's day.</param>
        public double ToClock24(double worldHours) => worldHours * 24.0 / HoursPerDay;

        /// <summary>
        /// How many world hours one hour of the body clock lasts: <c>HoursPerDay / 24</c> when <see cref="BodyClockFollowsDay"/>, otherwise 1.
        /// Multiplies the time constants of the sleep-regulation model (a 40 h day gives 1.67).
        /// </summary>
        public double BodyClockScale => BodyClockFollowsDay ? HoursPerDay / 24.0 : 1.0;

        /// <summary>World ticks in one day.</summary>
        public long TicksPerDay => TicksPerHour * HoursPerDay;

        /// <summary>World ticks in one hour.</summary>
        public long TicksPerHour => TicksPerMinute * MinutesPerHour;

        /// <summary>World ticks in one minute.</summary>
        public long TicksPerMinute => TicksPerSecond * SecondsPerMinute;
    }
}
