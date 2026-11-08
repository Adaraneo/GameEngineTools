// CropMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Crops
{
    using System;
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
