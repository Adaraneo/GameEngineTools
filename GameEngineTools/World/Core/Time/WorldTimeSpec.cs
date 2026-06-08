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

        /// <summary>Creates a world time specification.</summary>
        /// <param name="ticksPerSecond">World ticks per world second.</param>
        /// <param name="secondsPerMinute">Seconds per minute.</param>
        /// <param name="minutesPerHour">Minutes per hour.</param>
        /// <param name="hoursPerDay">Hours per day.</param>
        /// <param name="calendar">The world calendar.</param>
        public WorldTimeSpec(
            long ticksPerSecond,
            int secondsPerMinute,
            int minutesPerHour,
            int hoursPerDay,
            IWorldCalendar calendar)
        {
            TicksPerSecond = ticksPerSecond;
            SecondsPerMinute = secondsPerMinute;
            MinutesPerHour = minutesPerHour;
            HoursPerDay = hoursPerDay;
            Calendar = calendar;
        }

        /// <summary>World ticks in one day.</summary>
        public long TicksPerDay => TicksPerHour * HoursPerDay;

        /// <summary>World ticks in one hour.</summary>
        public long TicksPerHour => TicksPerMinute * MinutesPerHour;

        /// <summary>World ticks in one minute.</summary>
        public long TicksPerMinute => TicksPerSecond * SecondsPerMinute;
    }
}
