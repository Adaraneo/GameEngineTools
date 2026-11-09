// WeatherGenerator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Weather
{
    using System;
    using System.Collections.Generic;

    /// <summary>Daily weather as a pure function of (seed, region, day): a Richardson-type chain plus a slow seasonal regime, every draw hashed from the absolute day.</summary>
    public sealed class WeatherGenerator
    {
        // Streams keep the draws of one day independent of each other.
        private const ulong StreamWetChain = 1, StreamAnomalyChain = 2, StreamAmount = 3, StreamAnomalyStart = 4, StreamWetStart = 5, StreamRegime = 6;

        // Variance of the mean of one quarter of linearly interpolated regime values is 0.59375 (weights 1/8, 3/4, 1/8); the regime is scaled to cancel it.
        private const double QuarterMeanVarianceFactor = 0.59375;

        private readonly WeatherConfig _cfg;
        private readonly ulong _seed;
        private readonly Func<long, YearPosition> _calendar;
        private readonly double _vernalPhase;
        private readonly double _regimeScale;
        private readonly Dictionary<string, WeatherDay> _lastByRegion = new();
        private readonly object _lock = new();

        /// <summary>Creates the generator of one world; the year length and vernal phase must match the astronomical context.</summary>
        public WeatherGenerator(WeatherConfig cfg, int seed, Func<long, YearPosition> calendar, double vernalPhase = 0.0)
        {
            _cfg = cfg;
            _seed = (ulong)(uint)seed;
            _calendar = calendar;
            _vernalPhase = vernalPhase;
            _regimeScale = 1.0 / Math.Sqrt(QuarterMeanVarianceFactor);
        }

        /// <summary>Creates the generator for a world whose every year has the same number of days.</summary>
        public WeatherGenerator(WeatherConfig cfg, int seed, int daysPerYear, double vernalPhase = 0.0)
            : this(cfg, seed, ConstantYears(Math.Max(4, daysPerYear)), vernalPhase)
        {
        }

        /// <summary>Where a world day falls in its year: the year number, the day of that year and the days that year has.</summary>
        public readonly record struct YearPosition(long Year, int DayOfYear, int DaysInYear);

        /// <summary>The weather of <paramref name="region"/> on <paramref name="day"/>; the empty region is the whole world.</summary>
        public WeatherDay Get(string region, long day)
        {
            lock (_lock)
            {
                if (_lastByRegion.TryGetValue(region, out var last) && last.Day == day)
                    return last;

                var computed = Compute(region, day);
                _lastByRegion[region] = computed;
                return computed;
            }
        }

        /// <summary>The temperature anomaly of <paramref name="day"/> alone; it does not depend on the place.</summary>
        public double TemperatureAnomaly(long day) => Compute(string.Empty, day).TemperatureAnomalyC;

        private WeatherDay Compute(string region, long day)
        {
            var start = day - _cfg.WarmUpDays;
            var regionKey = Hash(region);
            var phi = _cfg.TemperaturePersistence;
            var innovation = Math.Sqrt(1.0 - phi * phi) * _cfg.TemperatureAnomalySdC;

            // A stationary start; the shared uniforms below make any two windows agree after a few days.
            var x = _cfg.TemperatureAnomalySdC * Normal(new Stream(Key(0, start, StreamAnomalyStart)));
            var (p01, p11) = Probabilities(regionKey, start);
            var wet = Unit(Key(regionKey, start, StreamWetStart)) < p01 / (1.0 + p01 - p11);

            for (var d = start + 1; d <= day; d++)
            {
                x = phi * x + innovation * Normal(new Stream(Key(0, d, StreamAnomalyChain)));
                (p01, p11) = Probabilities(regionKey, d);
                wet = Unit(Key(regionKey, d, StreamWetChain)) < (wet ? p11 : p01);
            }

            var anomaly = x + RegimeTemperatureShift(day);
            var amount = 0.0;
            if (wet)
            {
                var shape = _cfg.WetDayShape;
                var scale = Math.Max(1e-6, _cfg.MeanWetDayMm - _cfg.WetDayThresholdMm) / shape;
                amount = _cfg.WetDayThresholdMm + scale * Gamma(shape, new Stream(Key(regionKey, day, StreamAmount)));
            }

            return new WeatherDay
            {
                Day = day,
                TemperatureAnomalyC = anomaly,
                Wet = wet,
                PrecipitationMm = amount,
                DiurnalRangeC = wet ? _cfg.DiurnalRangeWetC : _cfg.DiurnalRangeDryC,
            };
        }

        // Chances of a wet day after a dry and after a wet day: logit of the base value, the wet-season swing and the regime.
        private (double P01, double P11) Probabilities(ulong regionKey, long day)
        {
            var seasonFraction = SeasonFraction(day);
            var shift = _cfg.WetSeasonAmplitude * Math.Cos(2.0 * Math.PI * (seasonFraction - _cfg.WetSeasonPeak)) + RegimeWetShift(regionKey, day);
            return (Logistic(Logit(_cfg.ProbWetAfterDry) + shift), Logistic(Logit(_cfg.ProbWetAfterWet) + shift));
        }

        private double SeasonFraction(long day)
        {
            var position = _calendar(day);
            return (position.DayOfYear / (double)position.DaysInYear + _vernalPhase) % 1.0;
        }

        private double RegimeTemperatureShift(long day)
            => _cfg.RegimeTemperatureSdC <= 0.0 ? 0.0 : _cfg.RegimeTemperatureSdC * _regimeScale * RegimeValue(0, 0, day);

        private double RegimeWetShift(ulong regionKey, long day)
        {
            if (_cfg.RegimeWetLogitSd <= 0.0)
                return 0.0;

            var share = Math.Clamp(_cfg.RegionalRegimeShare, 0.0, 1.0);
            var shared = RegimeValue(0, 1, day);
            var local = regionKey == 0 ? shared : RegimeValue(regionKey, 1, day);
            return _cfg.RegimeWetLogitSd * _regimeScale * (Math.Sqrt(1.0 - share) * shared + Math.Sqrt(share) * local);
        }

        // A standard-normal value per quarter of the year, linearly interpolated between the centres of neighbouring quarters.
        private double RegimeValue(ulong regionKey, ulong variable, long day)
        {
            var position = _calendar(day);
            var t = position.Year * 4.0 + position.DayOfYear * 4.0 / position.DaysInYear - 0.5;
            var lower = (long)Math.Floor(t);
            var f = t - lower;
            return (1.0 - f) * QuarterValue(regionKey, variable, lower) + f * QuarterValue(regionKey, variable, lower + 1);
        }

        private double QuarterValue(ulong regionKey, ulong variable, long quarter)
            => Normal(new Stream(Key(regionKey ^ (variable * 0x9E3779B97F4A7C15UL), quarter, StreamRegime)));

        private ulong Key(ulong regionKey, long day, ulong stream)
            => Mix(Mix(Mix(Mix(_seed + 0x632BE59BD9B4E019UL) ^ regionKey) ^ (ulong)day) ^ (stream * 0xD6E8FEB86659FD93UL));

        private static double Unit(ulong key) => ((Mix(key) >> 11) + 0.5) / 9007199254740992.0;

        private static double Normal(Stream s)
        {
            var u1 = s.Next();
            var u2 = s.Next();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        // Marsaglia and Tsang (2000); shapes below 1 are boosted from shape + 1.
        private static double Gamma(double shape, Stream s)
        {
            if (shape < 1.0)
                return Gamma(shape + 1.0, s) * Math.Pow(s.Next(), 1.0 / shape);

            var d = shape - 1.0 / 3.0;
            var c = 1.0 / Math.Sqrt(9.0 * d);
            for (var attempt = 0; attempt < 1000; attempt++)
            {
                double x, v;
                do
                {
                    x = Normal(s);
                    v = 1.0 + c * x;
                }
                while (v <= 0.0);

                v = v * v * v;
                var u = s.Next();
                if (u < 1.0 - 0.0331 * x * x * x * x || Math.Log(u) < 0.5 * x * x + d * (1.0 - v + Math.Log(v)))
                    return d * v;
            }

            return d;
        }

        private static ulong Hash(string text)
        {
            if (text.Length == 0)
                return 0;

            var hash = 14695981039346656037UL;
            foreach (var c in text)
                hash = (hash ^ c) * 1099511628211UL;
            return hash == 0 ? 1 : hash;
        }

        private static ulong Mix(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        private static long FloorMod(long a, long b) => ((a % b) + b) % b;

        private static Func<long, YearPosition> ConstantYears(int daysPerYear)
            => day => new YearPosition((day - FloorMod(day, daysPerYear)) / daysPerYear, (int)FloorMod(day, daysPerYear), daysPerYear);

        private static double Logistic(double x) => 1.0 / (1.0 + Math.Exp(-x));

        private static double Logit(double p) => Math.Log(p / (1.0 - p));

        // A short run of uniform numbers from one key.
        private sealed class Stream
        {
            private ulong _state;

            public Stream(ulong key) => _state = key;

            public double Next()
            {
                _state += 0x9E3779B97F4A7C15UL;
                return ((Mix(_state) >> 11) + 0.5) / 9007199254740992.0;
            }
        }
    }
}
