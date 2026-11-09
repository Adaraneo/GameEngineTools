// WeatherGeneratorTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.World.Weather;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>The weather generator: reproducible, independent of the order of questions, and statistically what its settings say.</summary>
    [TestClass]
    public sealed class WeatherGeneratorTests
    {
        private const int DaysPerYear = 360;

        // No season, no regime: the plain chain, whose long-run statistics are known in closed form.
        private static WeatherConfig Plain() => new() { WetSeasonAmplitude = 0.0, RegimeTemperatureSdC = 0.0, RegimeWetLogitSd = 0.0 };

        private static List<WeatherDay> Series(WeatherGenerator g, string region, long from, int days)
            => Enumerable.Range(0, days).Select(i => g.Get(region, from + i)).ToList();

        [TestMethod]
        public void Weather_IsTheSameForTheSameSeedRegionAndDay_AndDiffersOtherwise()
        {
            var a = new WeatherGenerator(WeatherConfig.Default, 1, DaysPerYear);
            var b = new WeatherGenerator(WeatherConfig.Default, 1, DaysPerYear);
            var other = new WeatherGenerator(WeatherConfig.Default, 2, DaysPerYear);

            Assert.AreEqual(a.Get("village", 1234), b.Get("village", 1234));
            Assert.AreNotEqual(a.Get("village", 1234).TemperatureAnomalyC, other.Get("village", 1234).TemperatureAnomalyC);
            Assert.IsTrue(Series(a, "village", 0, 400).Zip(Series(a, "hamlet", 0, 400)).Any(p => p.First.Wet != p.Second.Wet), "Two places do not rain on the same days.");
        }

        [TestMethod]
        public void Weather_DoesNotDependOnWhichDaysWereAskedBefore()
        {
            var sequential = new WeatherGenerator(WeatherConfig.Default, 7, DaysPerYear);
            var cold = new WeatherGenerator(WeatherConfig.Default, 7, DaysPerYear);
            var mixed = new WeatherGenerator(WeatherConfig.Default, 7, DaysPerYear);
            Series(sequential, "v", 0, 800);
            mixed.Get("v", 5000);
            mixed.Get("w", 3);

            for (var day = 100; day < 800; day += 37)
            {
                Assert.AreEqual(sequential.Get("v", day), cold.Get("v", day), $"day {day}");
                Assert.AreEqual(sequential.Get("v", day), mixed.Get("v", day), $"day {day}");
            }
        }

        [TestMethod]
        public void Weather_BeforeTheEpoch_IsDefinedAndReproducible()
        {
            var a = new WeatherGenerator(WeatherConfig.Default, 3, DaysPerYear);
            var b = new WeatherGenerator(WeatherConfig.Default, 3, DaysPerYear);

            Assert.AreEqual(a.Get("v", -500), b.Get("v", -500));
            Assert.IsTrue(Series(a, "v", -400, 200).Any(d => d.Wet) && Series(a, "v", -400, 200).Any(d => !d.Wet));
        }

        [TestMethod]
        public void Weather_ForgetsItsStart_WithinTheWarmUp()
        {
            var shortWindow = new WeatherGenerator(new WeatherConfig { WarmUpDays = 60 }, 11, DaysPerYear);
            var longWindow = new WeatherGenerator(new WeatherConfig { WarmUpDays = 200 }, 11, DaysPerYear);

            for (var day = 300; day < 2300; day++)
            {
                var s = shortWindow.Get("v", day);
                var l = longWindow.Get("v", day);
                Assert.AreEqual(l.Wet, s.Wet, $"day {day}");
                Assert.AreEqual(l.TemperatureAnomalyC, s.TemperatureAnomalyC, 1e-5, $"day {day}");
            }
        }

        [TestMethod]
        public void PlainChain_HasTheWetDayShareAndPersistenceOfItsSettings()
        {
            var cfg = Plain();
            var days = Series(new WeatherGenerator(cfg, 21, DaysPerYear), "v", 0, DaysPerYear * 100);
            var p01 = cfg.ProbWetAfterDry;
            var p11 = cfg.ProbWetAfterWet;

            var wetShare = days.Average(d => d.Wet ? 1.0 : 0.0);
            Assert.AreEqual(p01 / (1.0 + p01 - p11), wetShare, 0.015, "Stationary wet-day share p01 / (1 + p01 - p11).");

            var occurrence = days.Select(d => d.Wet ? 1.0 : 0.0).ToList();
            Assert.AreEqual(p11 - p01, Lag1(occurrence), 0.03, "Lag-1 autocorrelation of occurrence p11 - p01.");
        }

        [TestMethod]
        public void PlainChain_HasTheAmountDistributionOfItsSettings()
        {
            var cfg = Plain();
            var wet = Series(new WeatherGenerator(cfg, 22, DaysPerYear), "v", 0, DaysPerYear * 100).Where(d => d.Wet).Select(d => d.PrecipitationMm).ToList();
            var mean = wet.Average();
            var cv = Math.Sqrt(wet.Sum(x => (x - mean) * (x - mean)) / wet.Count) / (mean - cfg.WetDayThresholdMm);

            Assert.AreEqual(cfg.MeanWetDayMm, mean, 0.3, "Mean wet-day amount.");
            Assert.AreEqual(1.0 / Math.Sqrt(cfg.WetDayShape), cv, 0.08, "Coefficient of variation of the gamma part is 1 / sqrt(shape).");
            Assert.IsTrue(wet.All(x => x >= cfg.WetDayThresholdMm), "A wet day has at least the threshold.");
            Assert.IsTrue(Series(new WeatherGenerator(cfg, 22, DaysPerYear), "v", 0, 300).Where(d => !d.Wet).All(d => d.PrecipitationMm == 0.0));
        }

        [TestMethod]
        public void PlainChain_HasTheAnomalySpreadAndPersistenceOfItsSettings()
        {
            var cfg = Plain();
            var anomaly = Series(new WeatherGenerator(cfg, 23, DaysPerYear), "v", 0, DaysPerYear * 100).Select(d => d.TemperatureAnomalyC).ToList();
            var mean = anomaly.Average();

            Assert.AreEqual(0.0, mean, 0.15);
            Assert.AreEqual(cfg.TemperatureAnomalySdC, Math.Sqrt(anomaly.Sum(x => (x - mean) * (x - mean)) / anomaly.Count), 0.12);
            Assert.AreEqual(cfg.TemperaturePersistence, Lag1(anomaly), 0.03);
        }

        [TestMethod]
        public void Regime_FillsTheSeasonalSpread_ThatTheDailyChainAloneLacks()
        {
            var cfg = new WeatherConfig { WetSeasonAmplitude = 0.0 };
            var g = new WeatherGenerator(cfg, 24, DaysPerYear);
            var withRegime = QuarterMeans(g, 400);
            var without = QuarterMeans(new WeatherGenerator(new WeatherConfig { RegimeTemperatureSdC = 0.0, RegimeWetLogitSd = 0.0 }, 24, DaysPerYear), 400);

            // Daily part alone: sigma^2 (1 + phi) / ((1 - phi) N) for a 90-day quarter.
            var daily = cfg.TemperatureAnomalySdC * Math.Sqrt((1.0 + cfg.TemperaturePersistence) / ((1.0 - cfg.TemperaturePersistence) * 90.0));
            Assert.AreEqual(daily, StandardDeviation(without), 0.08, "Without a regime only the daily chain spreads the seasonal mean.");
            Assert.AreEqual(Math.Sqrt(daily * daily + cfg.RegimeTemperatureSdC * cfg.RegimeTemperatureSdC), StandardDeviation(withRegime), 0.12, "The regime adds its own spread.");
        }

        [TestMethod]
        public void WetSeason_RaisesTheWetShareAtItsPeak()
        {
            var cfg = new WeatherConfig { WetSeasonAmplitude = 0.8, WetSeasonPeak = 0.25, RegimeTemperatureSdC = 0.0, RegimeWetLogitSd = 0.0 };
            var g = new WeatherGenerator(cfg, 25, DaysPerYear);
            double ShareAround(double seasonFraction)
            {
                var day = (int)(seasonFraction * DaysPerYear);
                return Enumerable.Range(0, 150).SelectMany(year => Enumerable.Range(day - 20, 40).Select(offset => g.Get("v", (long)year * DaysPerYear + offset)))
                    .Average(d => d.Wet ? 1.0 : 0.0);
            }

            Assert.IsTrue(ShareAround(0.25) > ShareAround(0.75) + 0.1, "More wet days at the peak than half a year later.");
        }

        [TestMethod]
        public void WetRegime_IsSharedByPlacesInProportionToTheConfiguredShare()
        {
            double Correlation(double regionalShare)
            {
                var cfg = new WeatherConfig { WetSeasonAmplitude = 0.0, RegimeTemperatureSdC = 0.0, RegimeWetLogitSd = 1.5, RegionalRegimeShare = regionalShare };
                var g = new WeatherGenerator(cfg, 26, DaysPerYear);
                var a = Enumerable.Range(0, 200).Select(y => (double)Series(g, "north", (long)y * DaysPerYear, DaysPerYear).Count(d => d.Wet)).ToList();
                var b = Enumerable.Range(0, 200).Select(y => (double)Series(g, "south", (long)y * DaysPerYear, DaysPerYear).Count(d => d.Wet)).ToList();
                return Pearson(a, b);
            }

            Assert.IsTrue(Correlation(0.0) > 0.6, "A fully shared regime makes wet years coincide.");
            Assert.IsTrue(Correlation(1.0) < 0.25, "A fully local regime does not.");
        }

        [TestMethod]
        public void Seasons_FollowTheCalendarYear_AlsoWhenYearsDifferInLength()
        {
            // Three years of 360 days and a leap year of 365, as in the default world calendar.
            WeatherGenerator.YearPosition Calendar(long day)
            {
                var cycle = 3L * 360 + 365;
                var cycles = (day - (((day % cycle) + cycle) % cycle)) / cycle;
                var rest = (int)(((day % cycle) + cycle) % cycle);
                var inCycle = rest / 360 > 2 ? 3 : rest / 360;
                var start = inCycle * 360;
                return new WeatherGenerator.YearPosition(cycles * 4 + inCycle, rest - start, inCycle == 3 ? 365 : 360);
            }

            var cfg = new WeatherConfig { WetSeasonAmplitude = 0.9, WetSeasonPeak = 0.25, RegimeTemperatureSdC = 0.0, RegimeWetLogitSd = 0.0 };
            var g = new WeatherGenerator(cfg, 29, Calendar);
            var peak = new List<double>();
            var trough = new List<double>();
            for (var year = 80; year < 240; year++)
            {
                var first = Enumerable.Range(0, 400000).First(i => Calendar(i).Year == year && Calendar(i).DayOfYear == 0);
                peak.Add(Enumerable.Range(60, 60).Average(i => g.Get("v", first + i).Wet ? 1.0 : 0.0));
                trough.Add(Enumerable.Range(240, 60).Average(i => g.Get("v", first + i).Wet ? 1.0 : 0.0));
            }

            Assert.IsTrue(peak.Average() > trough.Average() + 0.15, $"The wet season stays on its place in the calendar year ({peak.Average():F2} against {trough.Average():F2}).");
        }

        [TestMethod]
        public void Temperature_IsTheSameEverywhere_AndRainDiffersByPlace()
        {
            var g = new WeatherGenerator(WeatherConfig.Default, 27, DaysPerYear);

            foreach (var day in new long[] { 10, 500, 7777 })
                Assert.AreEqual(g.Get("north", day).TemperatureAnomalyC, g.Get("south", day).TemperatureAnomalyC);
        }

        [TestMethod]
        public void WetDays_HaveASmallerDiurnalRange()
        {
            var days = Series(new WeatherGenerator(WeatherConfig.Default, 28, DaysPerYear), "v", 0, 400);

            Assert.IsTrue(days.Where(d => d.Wet).All(d => d.DiurnalRangeC == WeatherConfig.Default.DiurnalRangeWetC));
            Assert.IsTrue(days.Where(d => !d.Wet).All(d => d.DiurnalRangeC == WeatherConfig.Default.DiurnalRangeDryC));
        }

        private static List<double> QuarterMeans(WeatherGenerator g, int years)
            => Enumerable.Range(0, years).Select(y => Enumerable.Range(0, 90).Average(i => g.TemperatureAnomaly((long)y * DaysPerYear + 90 + i))).ToList();

        private static double StandardDeviation(List<double> values)
        {
            var mean = values.Average();
            return Math.Sqrt(values.Sum(x => (x - mean) * (x - mean)) / values.Count);
        }

        private static double Lag1(List<double> values)
        {
            var mean = values.Average();
            var variance = values.Sum(x => (x - mean) * (x - mean));
            var covariance = Enumerable.Range(1, values.Count - 1).Sum(i => (values[i] - mean) * (values[i - 1] - mean));
            return covariance / variance;
        }

        private static double Pearson(List<double> a, List<double> b)
        {
            var ma = a.Average();
            var mb = b.Average();
            var cov = a.Zip(b).Sum(p => (p.First - ma) * (p.Second - mb));
            return cov / Math.Sqrt(a.Sum(x => (x - ma) * (x - ma)) * b.Sum(x => (x - mb) * (x - mb)));
        }
    }
}
