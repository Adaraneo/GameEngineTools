// InitWorldClockConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Config
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public sealed class InitWorldClockConfig
    {
        /// <summary>
        /// Each entry represents number of days in the corresponding month.
        /// </summary>
        public int[] DaysInMonths { get ; set; } = new[] { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        /// <summary>
        /// Gets or sets the number of ticks that represent one second.
        /// </summary>
        public long TicksPerSecond { get; set; } = 1_000_000;

        /// <summary>
        /// Gets or sets the number of seconds in a minute. The default value is 60.
        /// </summary>
        public int SecondsPerMinute { get; set; } = 60;

        /// <summary>
        /// Gets or sets the number of minutes in an hour.
        /// </summary>
        public int MinutesPerHour { get; set; } = 60;

        /// <summary>
        /// Gets or sets the number of hours in a day.
        /// </summary>
        public int HoursPerDay { get; set; } = 24;

        /// <summary>
        /// Gets or sets the interval, in years, used to determine leap years.
        /// </summary>
        public int LeapYearInterval { get; set; } = 4;

        /// <summary>
        /// Gets or sets the number of leap days used in date calculations.
        /// </summary>
        public int LeapExtraDays { get; set; } = 1;
    }
}
