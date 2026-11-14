// AmbientTemperatureTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Weather;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Linq;

    /// <summary>The temporary day/night temperature curve and the weather generator on the configured world calendar.</summary>
    [TestClass]
    public sealed class AmbientTemperatureTests : TestBase
    {
        private static double[] Day(double mean, double range, double noon, double hoursPerDay, double lag)
            => Enumerable.Range(0, (int)(hoursPerDay * 4)).Select(i => AmbientTemperatureModel.Outdoor(mean, range, i / 4.0, noon, hoursPerDay, lag)).ToArray();

        [TestMethod]
        public void Outdoor_AveragesToTheMean_AndSwingsByTheRange()
        {
            var day = Day(12.0, 10.0, 13.0, 26.0, 2.0);

            Assert.AreEqual(12.0, day.Average(), 0.01);
            Assert.AreEqual(10.0, day.Max() - day.Min(), 0.01);
        }

        [TestMethod]
        public void Outdoor_IsWarmestAfterSolarNoon_AndColdestHalfADayLater()
        {
            const double hoursPerDay = 26.0, noon = 13.0, lag = 2.0;
            var day = Day(12.0, 10.0, noon, hoursPerDay, lag);
            var hourOfMax = Array.IndexOf(day, day.Max()) / 4.0;
            var hourOfMin = Array.IndexOf(day, day.Min()) / 4.0;

            Assert.AreEqual(noon + lag, hourOfMax, 0.3);
            Assert.AreEqual((noon + lag + hoursPerDay / 2.0) % hoursPerDay, hourOfMin, 0.3);
        }

        [TestMethod]
        public void Outdoor_WithoutASolarNoon_FallsBackToMidDay()
        {
            Assert.AreEqual(
                AmbientTemperatureModel.Outdoor(5.0, 8.0, 10.0, 13.0, 26.0, 2.0),
                AmbientTemperatureModel.Outdoor(5.0, 8.0, 10.0, double.NaN, 26.0, 2.0), 1e-9);
        }

        [TestMethod]
        public void ForWorld_ReadsTheConfiguredCalendar_AndIsDefinedBeforeTheEpoch()
        {
            UseDayLength(26);
            var a = WeatherGenerator.ForWorld(WeatherConfig.Default, 4, 0.0);
            var b = WeatherGenerator.ForWorld(WeatherConfig.Default, 4, 0.0);

            Assert.AreEqual(a.Get(string.Empty, 500), b.Get(string.Empty, 500));
            Assert.IsTrue(Enumerable.Range(-40, 80).All(d => double.IsFinite(a.Get(string.Empty, d).TemperatureAnomalyC)));
            Assert.AreNotEqual(a.Get(string.Empty, 500).TemperatureAnomalyC, WeatherGenerator.ForWorld(WeatherConfig.Default, 5, 0.0).Get(string.Empty, 500).TemperatureAnomalyC);
        }
    }
}
