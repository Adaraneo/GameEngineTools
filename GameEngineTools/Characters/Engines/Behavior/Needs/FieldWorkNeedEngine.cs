// FieldWorkNeedEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Needs
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Engines.Economy;
    using GameEngineTools.Characters.Engines.Schedule;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Objects.Crops;
    using GameEngineTools.World.Utils.Time;
    using static ActionNames;

    /// <summary>
    /// Offers the job a field in the character's settlement needs now: reaping a ripe crop, sowing in the autumn window, threshing in the
    /// winter, ploughing in the warm months. Which job is possible is decided by <see cref="FieldWork"/>; this only turns it into a candidate.
    /// Works only where a celestial context supplies the season.
    /// </summary>
    internal sealed class FieldWorkNeedEngine : IBehaviorNeedEngine
    {
        // On the scale of the other candidates at a farm (Work 62-82, Eat above 100): reaping cannot wait (the crop shatters), sowing has a window,
        // threshing and ploughing fit the quiet months and compete with ordinary work.
        private const double ReapUtility = 90.0;
        private const double SowUtility = 88.0;
        private const double ThreshUtility = 70.0;
        private const double PloughUtility = 74.0;

        // A farmer on shift puts the field first; everyone else helps, a little less.
        private const double FarmerOnShiftBonus = 10.0;
        private const double HelperFactor = 0.9;

        // Too hungry to work; eating comes first.
        private const double MaxNeedFoodToWork = 70.0;

        private readonly CropConfig _config;

        public FieldWorkNeedEngine(CropConfig? config = null) => _config = config ?? CropConfig.Default;

        /// <inheritdoc/>
        public BehaviorNeedOutput Evaluate(BehaviorContext context)
        {
            if (context.AvailableObjects is null || context.State.NeedFood >= MaxNeedFoodToWork)
                return BehaviorNeedOutput.Empty;

            var snapshot = context.HumanContext.Snapshot;
            if (snapshot.Celestial is not { } celestial)
                return BehaviorNeedOutput.Empty;

            var fields = context.AvailableObjects.Where(o => o.Field is not null).ToList();
            if (fields.Count == 0)
                return BehaviorNeedOutput.Empty;

            var seed = context.AvailableObjects.Where(GrainStores.IsSeedBin).Sum(o => o.Quantity);
            var season = new FieldSeason(celestial.SeasonFraction, snapshot.AmbientTemperature, context.Now.Year, seed);

            FieldTask? best = null;
            foreach (var field in fields)
            {
                if (FieldWork.Available(field.Field!, season, _config) is { } task && (best is null || Utility(task) > Utility(best.Value)))
                    best = task;
            }

            if (best is not { } chosen)
                return BehaviorNeedOutput.Empty;

            var utility = Utility(chosen) * (IsFarmerOnShift(snapshot.Schedule) ? 1.0 : HelperFactor) + (IsFarmerOnShift(snapshot.Schedule) ? FarmerOnShiftBonus : 0.0);
            var candidate = new BehaviorCandidate(
                NameOf(chosen),
                utility,
                WTimeSpan.FromHours(_config.WorkSessionHours),
                BehaviorDomain.Competence,
                Tags: new[] { "FieldWork" });

            return new BehaviorNeedOutput(Array.Empty<BehaviorDrive>(), new List<BehaviorCandidate> { candidate });
        }

        internal static string NameOf(FieldTask task) => task switch
        {
            FieldTask.Plough => Plough,
            FieldTask.Sow => Sow,
            FieldTask.Reap => Reap,
            _ => Thresh,
        };

        private static double Utility(FieldTask task) => task switch
        {
            FieldTask.Reap => ReapUtility,
            FieldTask.Sow => SowUtility,
            FieldTask.Thresh => ThreshUtility,
            _ => PloughUtility,
        };

        private static bool IsFarmerOnShift(DailyScheduleState? schedule)
            => schedule?.ActiveSlot is { PreferredAction: Work }
               && !string.IsNullOrEmpty(schedule.Occupation)
               && OccupationIds.FoodProducerOccupations.Contains(schedule.Occupation);
    }
}
