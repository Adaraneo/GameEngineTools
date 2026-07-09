// PlanetaryCalendarFactoryTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Universe;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Tests for <see cref="PlanetaryCalendarFactory"/> — deriving a world time specification
    /// from planetary rotation, orbit, and a cultural month overlay.
    /// </summary>
    [TestClass]
    public class PlanetaryCalendarFactoryTests
    {
        /// <summary>
        /// An explicit <see cref="CalendarOptions.TargetYearDays"/> bypasses the physical orbit
        /// calculation and produces exactly the requested calendar — here Vigilia Insectianis:
        /// a 26-hour day and a 360-day year split into ten 36-day months.
        /// </summary>
        [TestMethod]
        public void Build_WithTargetYearDays_ProducesExactCalendar()
        {
            var planet = PlanetConfig.Earth with { SiderealRotationHrs = 26.0 };
            var options = new CalendarOptions(MonthCount: 10, TargetYearDays: 360);

            var spec = PlanetaryCalendarFactory.Build(planet, OrbitalElements.Earth, StarPhysics.Sol, options);

            Assert.AreEqual(26, spec.HoursPerDay);
            Assert.AreEqual(10, spec.Calendar.MonthsInYear(1));
            Assert.AreEqual(360L, spec.Calendar.DaysInYear(1));
            Assert.AreEqual(36, spec.Calendar.DaysInMonth(1, 1));
            Assert.AreEqual(36, spec.Calendar.DaysInMonth(1, 10));
        }

        /// <summary>
        /// Without an override the year length is computed from the orbit via Kepler's third law.
        /// Earth around the Sun with a rounded 24-hour day yields a 365-day year.
        /// </summary>
        [TestMethod]
        public void Build_FromEarthOrbit_YieldsEarthLikeYear()
        {
            var spec = PlanetaryCalendarFactory.Build(
                PlanetConfig.Earth,
                OrbitalElements.Earth,
                StarPhysics.Sol,
                new CalendarOptions(MonthCount: 12));

            Assert.AreEqual(24, spec.HoursPerDay);          // round(23.9345)
            Assert.AreEqual(365L, spec.Calendar.DaysInYear(1));
        }

        /// <summary>
        /// When the year does not divide evenly, the remainder (epagomenal days) lands in the last month.
        /// 365 days over 12 months = eleven 30-day months and a final 35-day month.
        /// </summary>
        [TestMethod]
        public void Build_WithUnevenYear_AddsRemainderToLastMonth()
        {
            var options = new CalendarOptions(MonthCount: 12, TargetYearDays: 365);

            var spec = PlanetaryCalendarFactory.Build(
                PlanetConfig.Earth, OrbitalElements.Earth, StarPhysics.Sol, options);

            Assert.AreEqual(30, spec.Calendar.DaysInMonth(1, 1));
            Assert.AreEqual(35, spec.Calendar.DaysInMonth(1, 12));
            Assert.AreEqual(365L, spec.Calendar.DaysInYear(1));
        }

        /// <summary>Retrograde rotation (negative period) still yields a positive day length.</summary>
        [TestMethod]
        public void Build_WithRetrogradeRotation_UsesAbsoluteDayLength()
        {
            var planet = PlanetConfig.Earth with { SiderealRotationHrs = -18.0 };

            var spec = PlanetaryCalendarFactory.Build(
                planet, OrbitalElements.Earth, StarPhysics.Sol, new CalendarOptions());

            Assert.AreEqual(18, spec.HoursPerDay);
        }

        /// <summary>
        /// Reproduces the hand-authored <c>InitWorldClock:VIWorld</c> calendar exactly (Vigilia
        /// Insectianis: 26-hour day, ten 36-day months, 10M ticks/second, and a leap year every
        /// fourth year adding five days to the last month). This is the guarantee that switching a
        /// world onto the physics-derived spec keeps its calendar — and its persisted time — intact.
        /// </summary>
        [TestMethod]
        public void Build_ReproducesVigiliaInsectianisExactly()
        {
            var planet  = PlanetConfig.Earth with { SiderealRotationHrs = 26.0 };
            var options = new CalendarOptions(
                MonthCount: 10, TargetYearDays: 360,
                MinutesPerHour: 60, SecondsPerMinute: 60, TicksPerSecond: 10_000_000,
                LeapYearInterval: 4, LeapExtraDays: 5);

            var spec = PlanetaryCalendarFactory.Build(planet, OrbitalElements.Earth, StarPhysics.Sol, options);

            Assert.AreEqual(26, spec.HoursPerDay);
            Assert.AreEqual(10_000_000L, spec.TicksPerSecond);
            Assert.AreEqual(10, spec.Calendar.MonthsInYear(1));
            Assert.AreEqual(360L, spec.Calendar.DaysInYear(1));    // non-leap year
            Assert.AreEqual(365L, spec.Calendar.DaysInYear(4));    // leap year (4 % 4 == 0): +5 days
            Assert.AreEqual(36, spec.Calendar.DaysInMonth(1, 10)); // last month, non-leap
            Assert.AreEqual(41, spec.Calendar.DaysInMonth(4, 10)); // last month, leap: 36 + 5
        }

        /// <summary>Time subdivisions flow through from <see cref="CalendarOptions"/> to the spec.</summary>
        [TestMethod]
        public void Build_CarriesTimeSubdivisions()
        {
            var options = new CalendarOptions(
                MonthCount: 8, TargetYearDays: 320,
                MinutesPerHour: 50, SecondsPerMinute: 50, TicksPerSecond: 10);

            var spec = PlanetaryCalendarFactory.Build(
                PlanetConfig.Earth, OrbitalElements.Earth, StarPhysics.Sol, options);

            Assert.AreEqual(50, spec.MinutesPerHour);
            Assert.AreEqual(50, spec.SecondsPerMinute);
            Assert.AreEqual(10L, spec.TicksPerSecond);
        }
    }
}
