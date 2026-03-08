// WorldTimeSpec.cs
// Copyright (c) 50PSoftware

using GameEngineTools.World.Core.Calendars;

namespace GameEngineTools.World.Core.Time
{
    public sealed class WorldTimeSpec
    {
        // Kalendář
        public readonly IWorldCalendar Calendar;

        public readonly int HoursPerDay;

        public readonly int MinutesPerHour;

        public readonly int SecondsPerMinute;

        // Časová soustava
        public readonly long TicksPerSecond;     // kolik worldTicks je jedna “sekunda světa”

        // Zarovnání na real-time (viz WorldClock)
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

        public long TicksPerDay => TicksPerHour * HoursPerDay;
        public long TicksPerHour => TicksPerMinute * MinutesPerHour;
        public long TicksPerMinute => TicksPerSecond * SecondsPerMinute;
    }
}