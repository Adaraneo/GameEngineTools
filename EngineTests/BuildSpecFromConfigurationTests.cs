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
    /// the config-key names bind correctly and the <c>UseAsCalendarSource</c> flag routes between the
    /// physics-derived calendar and the hand-authored <c>InitWorldClock</c> fallback.
    /// </summary>
    [TestClass]
    public class BuildSpecFromConfigurationTests
    {
        private static IConfiguration BuildConfig(Dictionary<string, string?> values)
            => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        /// <summary>
        /// With <c>World:Universe:UseAsCalendarSource = true</c>, the spec is derived from the
        /// planetary system — here reproducing the Vigilia Insectianis calendar.
        /// </summary>
        [TestMethod]
        public void UseAsCalendarSource_True_DerivesFromUniverseSection()
        {
            var cfg = BuildConfig(new Dictionary<string, string?>
            {
                ["World:Universe:UseAsCalendarSource"]      = "true",
                ["World:Universe:PlanetSiderealRotationHrs"] = "26",
                ["World:Universe:CalendarMonthCount"]       = "10",
                ["World:Universe:CalendarTargetYearDays"]   = "360",
                ["World:Universe:CalendarTicksPerSecond"]   = "10000000",
                ["World:Universe:CalendarMinutesPerHour"]   = "60",
                ["World:Universe:CalendarSecondsPerMinute"] = "60",
                ["World:Universe:CalendarLeapYearInterval"] = "4",
                ["World:Universe:CalendarLeapExtraDays"]    = "5",
            });

            var spec = GameEngineToolsRuntime.BuildSpecFromConfiguration(cfg);

            Assert.AreEqual(26, spec.HoursPerDay);
            Assert.AreEqual(10_000_000L, spec.TicksPerSecond);
            Assert.AreEqual(360L, spec.Calendar.DaysInYear(1));
            Assert.AreEqual(365L, spec.Calendar.DaysInYear(4));   // leap year
        }

        /// <summary>
        /// Without the flag, the spec falls back to the hand-authored <c>InitWorldClock</c> section —
        /// unchanged legacy behaviour.
        /// </summary>
        [TestMethod]
        public void UseAsCalendarSource_False_FallsBackToInitWorldClock()
        {
            var cfg = BuildConfig(new Dictionary<string, string?>
            {
                ["World:Universe:UseAsCalendarSource"]        = "false",
                ["InitWorldClock:UseWorldType"]              = "VIWorld",
                ["InitWorldClock:VIWorld:HoursPerDay"]       = "26",
                ["InitWorldClock:VIWorld:MinutesPerHour"]    = "60",
                ["InitWorldClock:VIWorld:SecondsPerMinute"]  = "60",
                ["InitWorldClock:VIWorld:TicksPerSecond"]    = "10000000",
                ["InitWorldClock:VIWorld:LeapYearInterval"]  = "4",
                ["InitWorldClock:VIWorld:LeapExtraDays"]     = "5",
                ["InitWorldClock:VIWorld:DaysInMonths:0"]    = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:1"]    = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:2"]    = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:3"]    = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:4"]    = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:5"]    = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:6"]    = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:7"]    = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:8"]    = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:9"]    = "36",
            });

            var spec = GameEngineToolsRuntime.BuildSpecFromConfiguration(cfg);

            Assert.AreEqual(26, spec.HoursPerDay);
            Assert.AreEqual(10_000_000L, spec.TicksPerSecond);
            Assert.AreEqual(360L, spec.Calendar.DaysInYear(1));
            Assert.AreEqual(365L, spec.Calendar.DaysInYear(4));   // leap year
        }

        /// <summary>
        /// The physics path and the fallback path yield the same calendar for Vigilia Insectianis —
        /// this is what makes switching the world onto the physics-derived spec safe for persisted time.
        /// </summary>
        [TestMethod]
        public void BothPaths_ProduceIdenticalViCalendar()
        {
            var universe = BuildConfig(new Dictionary<string, string?>
            {
                ["World:Universe:UseAsCalendarSource"]      = "true",
                ["World:Universe:PlanetSiderealRotationHrs"] = "26",
                ["World:Universe:CalendarMonthCount"]       = "10",
                ["World:Universe:CalendarTargetYearDays"]   = "360",
                ["World:Universe:CalendarTicksPerSecond"]   = "10000000",
                ["World:Universe:CalendarLeapYearInterval"] = "4",
                ["World:Universe:CalendarLeapExtraDays"]    = "5",
            });
            var initWorldClock = BuildConfig(new Dictionary<string, string?>
            {
                ["InitWorldClock:UseWorldType"]             = "VIWorld",
                ["InitWorldClock:VIWorld:HoursPerDay"]      = "26",
                ["InitWorldClock:VIWorld:TicksPerSecond"]   = "10000000",
                ["InitWorldClock:VIWorld:LeapYearInterval"] = "4",
                ["InitWorldClock:VIWorld:LeapExtraDays"]    = "5",
                ["InitWorldClock:VIWorld:DaysInMonths:0"]   = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:1"]   = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:2"]   = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:3"]   = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:4"]   = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:5"]   = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:6"]   = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:7"]   = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:8"]   = "36",
                ["InitWorldClock:VIWorld:DaysInMonths:9"]   = "36",
            });

            var physics  = GameEngineToolsRuntime.BuildSpecFromConfiguration(universe);
            var authored = GameEngineToolsRuntime.BuildSpecFromConfiguration(initWorldClock);

            Assert.AreEqual(authored.HoursPerDay, physics.HoursPerDay);
            Assert.AreEqual(authored.TicksPerSecond, physics.TicksPerSecond);
            Assert.AreEqual(authored.MinutesPerHour, physics.MinutesPerHour);
            Assert.AreEqual(authored.SecondsPerMinute, physics.SecondsPerMinute);
            for (int year = 1; year <= 8; year++)
            {
                Assert.AreEqual(authored.Calendar.DaysInYear(year), physics.Calendar.DaysInYear(year),
                    $"Year {year} length differs between the physics and authored calendars.");
            }
        }
    }
}
