// CropMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Crops
{
    using System;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;

    /// <summary>The arithmetic and the stage transitions of the grain cycle, free of storage and time sources so they can be tested alone.</summary>
    public static class CropMath
    {
        /// <summary>Seed needed for a field, in storage units, at least 1.</summary>
        public static int SeedUnits(double areaHectares, CropConfig cfg)
            => Math.Max(1, (int)Math.Round(areaHectares * cfg.SowingRateKgPerHectare * 1000.0 / cfg.GramsPerUnit));

        /// <summary>Thermal time collected over <paramref name="days"/> at a constant <paramref name="temperatureC"/>: heat between the base and the ceiling.</summary>
        public static double DegreeDays(double temperatureC, double days, CropConfig cfg)
        {
            var effective = Math.Min(temperatureC, cfg.CeilingTemperatureCelsius) - cfg.BaseTemperatureCelsius;
            return effective <= 0.0 || days <= 0.0 ? 0.0 : effective * days;
        }

        /// <summary>The weather of a crop year as a yield multiplier, the same for one place and year on every run; lognormal with mean 1 and the configured spread, kept between 0.3 and 1.8.</summary>
        public static double YearFactor(string placeKey, int year, CropConfig cfg)
        {
            var sigmaSquared = Math.Log(1.0 + cfg.YieldVariation * cfg.YieldVariation);
            if (sigmaSquared <= 0.0)
                return 1.0;

            // FNV-1a over the place and year gives two uniform numbers, which Box-Muller turns into one normal deviate.
            ulong hash = 14695981039346656037UL;
            foreach (var c in placeKey + "|" + year)
                hash = (hash ^ c) * 1099511628211UL;
            var u1 = ((hash >> 11) + 1) / 9007199254740994.0;
            hash = (hash ^ (hash >> 29)) * 0xBF58476D1CE4E5B9UL;
            var u2 = (hash >> 11) / 9007199254740992.0;
            var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

            var factor = Math.Exp(Math.Sqrt(sigmaSquared) * z - sigmaSquared / 2.0);
            return Math.Clamp(factor, 0.3, 1.8);
        }

        /// <summary>Weather for sowing winter wheat: the year is cooling and the air is between the configured limits.</summary>
        public static bool IsSowingWeather(double temperatureC, double trendPerDay, CropConfig cfg)
            => trendPerDay < 0.0 && temperatureC >= cfg.SowingTemperatureMinCelsius && temperatureC <= cfg.SowingTemperatureMaxCelsius;

        /// <summary>Heat collected between two dates from a daily temperature source; steps of one world day, counted in 24-hour days because heat acts in absolute time.</summary>
        public static double DegreeDaysBetween(WDateTime from, WDateTime to, Func<WDateTime, double> temperatureAt, CropConfig cfg)
        {
            var step = WTimeSpan.FromDays(1);
            var earthDaysPerStep = WWorld.Spec.HoursPerDay / 24.0;
            var total = 0.0;
            for (var t = from; t < to; t += step)
                total += DegreeDays(temperatureAt(t), earthDaysPerStep, cfg);
            return total;
        }

        /// <summary>
        /// The state of a field that was sown before the world began, worked out from the climate: sown in the middle of the most recent spell of
        /// sowing weather, with the heat since then, ripe if that was enough. Fallow if there was no sowing weather in the last year.
        /// </summary>
        public static FieldState Found(FieldState field, WDateTime now, Func<WDateTime, double> temperatureAt, CropConfig cfg)
        {
            var day = WTimeSpan.FromDays(1);
            bool Weather(WDateTime t) => IsSowingWeather(temperatureAt(t), (temperatureAt(t) - temperatureAt(t - WTimeSpan.FromDays(2))) / 2.0, cfg);
            var daysInYear = (int)WWorld.Spec.Calendar.DaysInYear(now.Year);

            WDateTime? end = null;
            for (var i = 1; i <= daysInYear; i++)
            {
                if (Weather(now - day * i)) { end = now - day * i; break; }
            }

            if (end is not { } spellEnd)
                return field with { Stage = FieldStage.Fallow, SownAt = null, DegreeDays = 0.0 };

            var start = spellEnd;
            while (Weather(start - day) && WDateTime.Difference(spellEnd, start - day).TotalDays < daysInYear / 2.0)
                start -= day;

            var sownAt = start + (spellEnd - start) / 2;
            var heat = 0.0;
            var earthDaysPerStep = WWorld.Spec.HoursPerDay / 24.0;
            for (var t = sownAt; t < now; t += day)
            {
                heat += DegreeDays(temperatureAt(t), earthDaysPerStep, cfg);
                if (heat >= cfg.DegreeDaysToMaturity)
                    return field with { Stage = FieldStage.Ripe, SownAt = sownAt, RipeAt = t, DegreeDays = heat, YearFactor = 1.0 };
            }

            return field with { Stage = FieldStage.Sown, SownAt = sownAt, DegreeDays = heat };
        }

        /// <summary>The field after ploughing; only bare or harvested ground can be ploughed.</summary>
        public static FieldState? Plough(FieldState field)
            => field.Stage is FieldStage.Fallow or FieldStage.Reaped ? field with { Stage = FieldStage.Ploughed } : null;

        /// <summary>The field with seed in the ground; only ploughed ground can be sown. <paramref name="yearFactor"/> is the weather of this season.</summary>
        public static FieldState? Sow(FieldState field, WDateTime now, CropConfig cfg, double yearFactor = 1.0)
            => field.Stage == FieldStage.Ploughed
                ? field with
                {
                    Stage = FieldStage.Sown,
                    SownAt = now,
                    RipeAt = null,
                    DegreeDays = 0.0,
                    SeedUnits = SeedUnits(field.AreaHectares, cfg),
                    YearFactor = yearFactor,
                }
                : null;

        /// <summary>The field after heat has been added; a sown crop that reaches its thermal requirement turns ripe at <paramref name="now"/>.</summary>
        public static FieldState Grow(FieldState field, double degreeDays, WDateTime now, CropConfig cfg)
        {
            if (field.Stage != FieldStage.Sown || degreeDays <= 0.0)
                return field;

            var total = field.DegreeDays + degreeDays;
            return total >= cfg.DegreeDaysToMaturity
                ? field with { Stage = FieldStage.Ripe, DegreeDays = total, RipeAt = now }
                : field with { DegreeDays = total };
        }

        /// <summary>Grain a ripe field gives if reaped at <paramref name="now"/>: seed times the yield ratio and the year, less what shattered while it stood.</summary>
        public static int HarvestUnits(FieldState field, WDateTime now, CropConfig cfg)
        {
            if (field.Stage != FieldStage.Ripe)
                return 0;

            var standingDays = field.RipeAt is { } ripe ? Math.Max(0.0, WDateTime.Difference(now, ripe).TotalDays) : 0.0;
            var kept = Math.Max(0.0, 1.0 - cfg.OverripeLossPerDay * standingDays);
            return (int)Math.Round(field.SeedUnits * cfg.YieldRatio * field.YearFactor * kept);
        }

        /// <summary>The field after reaping; only a ripe field can be reaped.</summary>
        public static FieldState? Reap(FieldState field)
            => field.Stage == FieldStage.Ripe ? field with { Stage = FieldStage.Reaped } : null;

        /// <summary>Units left of a stock after <paramref name="days"/> of storage at the configured yearly loss; whole units, never more than before.</summary>
        public static int AfterStorage(int units, double days, double daysInYear, CropConfig cfg)
        {
            if (units <= 0 || days <= 0.0)
                return Math.Max(0, units);

            var kept = Math.Pow(1.0 - cfg.StorageLossPerYear, days / daysInYear);
            return Math.Min(units, (int)Math.Round(units * kept));
        }
    }
}
