// BuildSpecFromConfigurationTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System.Collections.Generic;
    using GameEngineTools;
    using Microsoft.Extensions.Configuration;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// End-to-end tests for <see cref="GameEngineToolsRuntime.BuildSpecFromConfiguration"/> — proving
    /// the <c>World:Universe</c> (planet rotation → day length) and <c>World:Calendar</c> (month/time/leap
    /// overlay) config-key names bind correctly and produce the expected world time specification.
    /// </summary>
    [TestClass]
    public class BuildSpecFromConfigurationTests
    {
        private static IConfiguration BuildConfig(Dictionary<string, string?> values)
            => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        /// <summary>
        /// Universe rotation drives the day length; the Calendar section supplies months, ticks and the
        /// leap rule — here reproducing the Vigilia Insectianis calendar.
        /// </summary>
        [TestMethod]
        public void UniverseAndCalendar_ProduceViCalendar()
        {
            var cfg = BuildConfig(new Dictionary<string, string?>
            {
                ["World:Universe:PlanetSiderealRotationHrs"] = "26",
                ["World:Calendar:MonthCount"]                = "10",
                ["World:Calendar:TargetYearDays"]            = "360",
                ["World:Calendar:TicksPerSecond"]            = "10000000",
                ["World:Calendar:MinutesPerHour"]            = "60",
                ["World:Calendar:SecondsPerMinute"]          = "60",
                ["World:Calendar:LeapYearInterval"]          = "4",
                ["World:Calendar:LeapExtraDays"]             = "5",
            });

            var spec = GameEngineToolsRuntime.BuildSpecFromConfiguration(cfg);

            Assert.AreEqual(26, spec.HoursPerDay);
            Assert.AreEqual(10_000_000L, spec.TicksPerSecond);
            Assert.AreEqual(10, spec.Calendar.MonthsInYear(1));
            Assert.AreEqual(360L, spec.Calendar.DaysInYear(1));
            Assert.AreEqual(365L, spec.Calendar.DaysInYear(4));   // leap year
            Assert.AreEqual(41, spec.Calendar.DaysInMonth(4, 10)); // last month, leap: 36 + 5
        }

        /// <summary>
        /// Without a Calendar section, the year length is derived from the orbit and the default
        /// 12-month overlay applies. Earth's orbit with a rounded 24-hour day yields a 365-day year.
        /// </summary>
        [TestMethod]
        public void UniverseOnly_DerivesYearFromOrbit()
        {
            var cfg = BuildConfig(new Dictionary<string, string?>
            {
                ["World:Universe:PlanetSiderealRotationHrs"] = "23.9345",
                ["World:Universe:OrbitSemiMajorAxisAu"]      = "1.000001",
                // Star defaults to Sol via the UniverseConfig record.
            });

            var spec = GameEngineToolsRuntime.BuildSpecFromConfiguration(cfg);

            Assert.AreEqual(24, spec.HoursPerDay);          // round(23.9345)
            Assert.AreEqual(365L, spec.Calendar.DaysInYear(1));
        }

        /// <summary>
        /// Missing Universe and Calendar sections fall back to record defaults (Earth/Sol + 12-month
        /// year) rather than throwing — a safe baseline for a minimally-configured host.
        /// </summary>
        [TestMethod]
        public void EmptyConfig_UsesRecordDefaults()
        {
            var spec = GameEngineToolsRuntime.BuildSpecFromConfiguration(
                BuildConfig(new Dictionary<string, string?>()));

            Assert.AreEqual(24, spec.HoursPerDay);          // Earth default rotation 23.9345
            Assert.AreEqual(10_000_000L, spec.TicksPerSecond);
            Assert.AreEqual(365L, spec.Calendar.DaysInYear(1));
        }
    }
}
