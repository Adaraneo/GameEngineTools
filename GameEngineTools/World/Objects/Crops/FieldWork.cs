// FieldWork.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Crops
{
    using System;
    using GameEngineTools.World.Utils.Time;

    /// <summary>A job on a field, done in sessions of hours until its labor is spent.</summary>
    public enum FieldTask
    {
        /// <summary>Turn the stubble or fallow ground over by hand.</summary>
        Plough = 0,

        /// <summary>Put seed into ploughed ground.</summary>
        Sow = 1,

        /// <summary>Cut the ripe crop with a sickle.</summary>
        Reap = 2,

        /// <summary>Beat the sheaves with a flail and winnow the grain.</summary>
        Thresh = 3,
    }

    /// <summary>The weather and calendar for working a field: temperature trend per day, air temperature, year, seed in the bin.</summary>
    public readonly record struct FieldSeason(double TemperatureTrendPerDay, double TemperatureCelsius, int Year, int SeedInStore);

    /// <summary>One session's outcome: the field after it, grain threshed for the stores, seed taken from the bin, and whether the task finished.</summary>
    public readonly record struct FieldWorkResult(FieldState Field, int ThreshedUnits, int SeedConsumed, bool Completed);

    /// <summary>Which job a field needs now and how a session of labor moves it on. Pure, so the whole year can be tested without characters.</summary>
    public static class FieldWork
    {
        /// <summary>The job the field can take now, or <c>null</c> when it has to wait (not ripe, wrong season, frozen ground, resting year, no seed).</summary>
        public static FieldTask? Available(FieldState field, FieldSeason now, CropConfig cfg)
        {
            switch (field.Stage)
            {
                case FieldStage.Ripe:
                    return FieldTask.Reap;

                case FieldStage.Reaped when field.PendingUnits > 0:
                    return FieldTask.Thresh;

                case FieldStage.Fallow:
                case FieldStage.Reaped:
                    return CanPlough(field, now, cfg) ? FieldTask.Plough : null;

                case FieldStage.Ploughed:
                    return CanSow(field, now, cfg) ? FieldTask.Sow : null;

                default:
                    return null;
            }
        }

        /// <summary>Whether the ground may be ploughed now: workable and rested.</summary>
        public static bool CanPlough(FieldState field, FieldSeason now, CropConfig cfg)
            => now.TemperatureCelsius >= cfg.MinWorkTemperatureCelsius
               && HasRested(field, now.Year, cfg);

        /// <summary>Whether the field may be sown now: sowing weather (cooling, between the limits), workable ground, rested, and enough seed in the bin.</summary>
        public static bool CanSow(FieldState field, FieldSeason now, CropConfig cfg)
            => now.TemperatureCelsius >= cfg.MinWorkTemperatureCelsius
               && CropMath.IsSowingWeather(now.TemperatureCelsius, now.TemperatureTrendPerDay, cfg)
               && HasRested(field, now.Year, cfg)
               && now.SeedInStore >= CropMath.SeedUnits(field.AreaHectares, cfg) * cfg.MinSowingShare;

        private static bool HasRested(FieldState field, int year, CropConfig cfg)
            => field.ReapedYear is not { } reaped || year >= reaped + (int)Math.Round(cfg.FallowYears);

        /// <summary>Labor the whole job needs on this field, in hours.</summary>
        public static double RequiredHours(FieldTask task, FieldState field, CropConfig cfg) => field.AreaHectares * task switch
        {
            FieldTask.Plough => cfg.PloughHoursPerHectare,
            FieldTask.Sow => cfg.SowHoursPerHectare,
            FieldTask.Reap => cfg.ReapHoursPerHectare,
            _ => cfg.ThreshHoursPerHectare,
        };

        /// <summary>One session of <paramref name="hours"/> of labor; the task moves the field to its next stage once its labor is spent.</summary>
        public static FieldWorkResult Apply(FieldTask task, FieldState field, double hours, WDateTime now, int year, CropConfig cfg, double yearFactor = 1.0, int seedInStore = int.MaxValue)
        {
            hours = Math.Max(0.0, hours);
            if (task == FieldTask.Thresh)
                return Thresh(field, hours);

            if (task == FieldTask.Reap)
                return Reap(field, hours, now, year, cfg);

            var done = field.WorkDoneHours + hours;
            if (done < RequiredHours(task, field, cfg))
                return new FieldWorkResult(field with { WorkDoneHours = done }, 0, 0, false);

            var finished = field with { WorkDoneHours = 0.0 };
            switch (task)
            {
                case FieldTask.Plough when CropMath.Plough(finished) is { } ploughed:
                    return new FieldWorkResult(ploughed, 0, 0, true);

                case FieldTask.Sow when CropMath.Sow(finished, now, cfg, yearFactor) is { } sown:
                    // A bin that falls a little short gives a thinner sowing, and the crop is smaller in proportion.
                    var sownSeed = Math.Min(sown.SeedUnits, seedInStore);
                    return new FieldWorkResult(sown with { SeedUnits = sownSeed }, 0, sownSeed, true);

                default:
                    return new FieldWorkResult(field, 0, 0, false);
            }
        }

        // Each session cuts its share of the field and the grain still standing has lost what shattered by now, so the loss follows the days it stood.
        private static FieldWorkResult Reap(FieldState field, double hours, WDateTime now, int year, CropConfig cfg)
        {
            if (field.Stage != FieldStage.Ripe)
                return new FieldWorkResult(field, 0, 0, false);

            var required = RequiredHours(FieldTask.Reap, field, cfg);
            var spent = Math.Min(hours, required - field.WorkDoneHours);
            var cut = (int)Math.Round(CropMath.HarvestUnits(field, now, cfg) * (spent / required));
            var done = field.WorkDoneHours + spent;

            if (done < required - 1e-9)
                return new FieldWorkResult(field with { WorkDoneHours = done, PendingUnits = field.PendingUnits + cut }, 0, 0, false);

            var reaped = CropMath.Reap(field)! with
            {
                WorkDoneHours = 0.0,
                PendingUnits = field.PendingUnits + cut,
                ThreshHoursRemaining = cfg.ThreshHoursPerHectare * field.AreaHectares,
                ReapedYear = year,
            };
            return new FieldWorkResult(reaped, 0, 0, true);
        }

        private static FieldWorkResult Thresh(FieldState field, double hours)
        {
            if (field.Stage != FieldStage.Reaped || field.PendingUnits <= 0)
                return new FieldWorkResult(field, 0, 0, false);

            var spent = Math.Min(hours, field.ThreshHoursRemaining);
            var remaining = field.ThreshHoursRemaining - spent;
            var units = remaining <= 1e-9
                ? field.PendingUnits
                : (int)Math.Round(field.PendingUnits * (spent / field.ThreshHoursRemaining));
            units = Math.Min(units, field.PendingUnits);

            var left = field.PendingUnits - units;
            if (remaining <= 1e-9 || left <= 0)
                return new FieldWorkResult(field with { Stage = FieldStage.Fallow, PendingUnits = 0, ThreshHoursRemaining = 0.0, WorkDoneHours = 0.0 }, units + left, 0, true);

            return new FieldWorkResult(field with { PendingUnits = left, ThreshHoursRemaining = remaining }, units, 0, false);
        }
    }
}
