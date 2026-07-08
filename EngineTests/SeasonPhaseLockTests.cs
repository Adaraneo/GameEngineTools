// SeasonPhaseLockTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using GameEngineTools.Universe;
    using GameEngineTools.World.Core.Astro;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Verifies that the Phase-2 <see cref="CelestialContextComputer"/> temperature season is
    /// phase-locked to the light season (both driven by the axial tilt / solar declination),
    /// respects the configured thermal lag, and is hemisphere-correct.
    /// </summary>
    [TestClass]
    public class SeasonPhaseLockTests : TestBase
    {
        #region Helpers

        /// <summary>
        /// Scans a full world year day-by-day and returns the day index of the longest day
        /// (light-season peak) and of the warmest day (temperature-season peak).
        /// </summary>
        private static (int PeakDaylightDay, int PeakTempDay) ScanYear(double thermalLag, double latitudeDeg)
        {
            var computer = new CelestialContextComputer(new SunModel());
            var star     = StarPhysics.Sol;
            // Circular orbit (e = 0) isolates the axial-tilt season from the minor eccentric
            // distance effect, so the light and temperature seasons must line up exactly.
            var orbit    = new OrbitalElements(
                SemiMajorAxisAu: 1.0, Eccentricity: 0.0, InclinationDeg: 0.0,
                LongAscNodeDeg: 0.0, ArgPeriapsisDeg: 0.0, MeanLongitudeDeg: 0.0);
            var planet   = PlanetConfig.Earth;
            var cfg = new AstroConfig(
                Sun:                        new SunParamsConfig(),
                LatitudeDeg:                latitudeDeg,
                LongitudeDeg:               0.0,
                BaseTemperatureCelsius:     11.0,
                SeasonalAmplitudeCelsius:   9.0,
                VernalPhase:                0.0,
                SeasonalThermalLagFraction: thermalLag);

            var start   = WDateTime.New(WDateOnly.New(100, 1, 1));
            var yearLen = (int)WWorld.Spec.Calendar.DaysInYear(100);

            double maxDaylight = double.NegativeInfinity;
            double maxTemp     = double.NegativeInfinity;
            int peakDaylightDay = 0;
            int peakTempDay     = 0;

            for (int d = 0; d < yearLen; d++)
            {
                var ctx = computer.Compute(start.AddDays(d), cfg, star, orbit, planet);
                if (ctx.DaylightHours > maxDaylight) { maxDaylight = ctx.DaylightHours; peakDaylightDay = d; }
                if (ctx.BaseAmbientTempCelsius > maxTemp) { maxTemp = ctx.BaseAmbientTempCelsius; peakTempDay = d; }
            }

            return (peakDaylightDay, peakTempDay);
        }

        /// <summary>Shortest distance between two day-of-year indices on a circular calendar.</summary>
        private static int CircularDistance(int a, int b, int yearLen)
        {
            int diff = Math.Abs(a - b);
            return Math.Min(diff, yearLen - diff);
        }

        #endregion Helpers

        /// <summary>
        /// With no thermal lag, the warmest day must coincide with the longest day — proving the
        /// temperature season and the light season share the same (axial-tilt) clock.
        /// </summary>
        [TestMethod]
        public void SeasonalTemperature_WithZeroLag_PeaksWithLongestDay()
        {
            var (peakDaylight, peakTemp) = ScanYear(thermalLag: 0.0, latitudeDeg: 50.0);
            var yearLen = (int)WWorld.Spec.Calendar.DaysInYear(100);

            int diff = CircularDistance(peakTemp, peakDaylight, yearLen);
            Assert.IsTrue(diff <= 3,
                $"Temperature peak (day {peakTemp}) should coincide with the longest day " +
                $"(day {peakDaylight}); circular diff was {diff}.");
        }

        /// <summary>
        /// With a positive thermal lag, the warmest day must trail the longest day by roughly the
        /// configured fraction of the year (ocean/soil thermal inertia).
        /// </summary>
        [TestMethod]
        public void SeasonalTemperature_WithThermalLag_TrailsLongestDay()
        {
            const double lag = 0.08;
            var (peakDaylight, peakTemp) = ScanYear(thermalLag: lag, latitudeDeg: 50.0);
            var yearLen = (int)WWorld.Spec.Calendar.DaysInYear(100);

            int forwardOffset = ((peakTemp - peakDaylight) % yearLen + yearLen) % yearLen;
            int expected      = (int)Math.Round(lag * yearLen);
            Assert.IsTrue(Math.Abs(forwardOffset - expected) <= 3,
                $"Temperature peak should trail the longest day by ~{expected} days (thermal lag); " +
                $"actual forward offset was {forwardOffset}.");
        }

        /// <summary>
        /// The southern-hemisphere temperature peak must fall roughly half a year from the
        /// northern one — confirming the latitude-sign hemisphere correction.
        /// </summary>
        [TestMethod]
        public void SeasonalTemperature_SouthernHemisphere_IsHalfYearOutOfPhase()
        {
            var (_, peakTempNorth) = ScanYear(thermalLag: 0.0, latitudeDeg: 50.0);
            var (_, peakTempSouth) = ScanYear(thermalLag: 0.0, latitudeDeg: -50.0);
            var yearLen = (int)WWorld.Spec.Calendar.DaysInYear(100);

            int diff = CircularDistance(peakTempNorth, peakTempSouth, yearLen);
            int half = yearLen / 2;
            Assert.IsTrue(Math.Abs(diff - half) <= 5,
                $"Northern (day {peakTempNorth}) and southern (day {peakTempSouth}) temperature peaks " +
                $"should be ~half a year apart; circular diff was {diff}, expected ~{half}.");
        }
    }
}
