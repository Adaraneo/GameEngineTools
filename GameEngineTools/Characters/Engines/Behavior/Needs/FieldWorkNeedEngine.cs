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
    using GameEngineTools.World.Objects.Production;
    using GameEngineTools.World.Utils.Time;
    using GameEngineTools.World.Work;
    using static ActionNames;

    /// <summary>
    /// Offers the job a field in the character's settlement needs now: reaping a ripe crop, sowing in cooling autumn weather, threshing, ploughing workable ground. Which job is possible is decided by <see cref="FieldWork"/>; this only turns it into a candidate.
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

        // Making the tool for a waiting job ranks a little under the job itself (a design pick).
        private const double SupplyFactor = 0.85;

        // Too hungry to work; eating comes first.
        private const double MaxNeedFoodToWork = 70.0;

        private readonly CropConfig _config;
        private readonly WorkCatalog _catalog;
        private readonly SupplyChain _chain;

        /// <param name="config">Crop settings.</param>
        /// <param name="catalog">The work catalog: ploughing, reaping and threshing need a tool of the class it names at the place or in hand.</param>
        public FieldWorkNeedEngine(WorkCatalog catalog, CropConfig? config = null)
        {
            _config = config ?? CropConfig.Default;
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _chain = new SupplyChain(catalog, new RecipeRegistry(catalog));
        }

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
            var season = new FieldSeason(celestial.TemperatureTrendPerDay, snapshot.AmbientTemperature, context.Now.Year, seed);

            FieldTask? best = null;
            FieldTask? lackingTool = null;
            foreach (var field in fields)
            {
                if (FieldWork.Available(field.Field!, season, _config, _catalog) is not { } task)
                    continue;
                if (HasToolFor(task, context))
                {
                    if (best is null || Utility(task) > Utility(best.Value))
                        best = task;
                }
                else if (lackingTool is null || Utility(task) > Utility(lackingTool.Value))
                {
                    lackingTool = task;
                }
            }

            if (best is null && lackingTool is { } needsTool)
                return SupplyTool(needsTool, context, snapshot.Schedule);

            if (best is not { } chosen)
                return BehaviorNeedOutput.Empty;

            var utility = Utility(chosen) * (IsOnShiftFor(chosen, snapshot.Schedule) ? 1.0 : HelperFactor) + (IsOnShiftFor(chosen, snapshot.Schedule) ? FarmerOnShiftBonus : 0.0);
            var candidate = new BehaviorCandidate(
                NameOf(chosen),
                utility,
                WTimeSpan.FromHours(_config.WorkSessionHours),
                BehaviorDomain.Competence,
                Tags: new[] { "FieldWork" });

            return new BehaviorNeedOutput(Array.Empty<BehaviorDrive>(), new List<BehaviorCandidate> { candidate });
        }

        /// <summary>A job is waiting but its tool is missing: when a craft step here gets closer to such a tool (the tool itself, or what it is made from), offer that step, a little under the job.</summary>
        private BehaviorNeedOutput SupplyTool(FieldTask task, BehaviorContext context, DailyScheduleState? schedule)
        {
            if (ToolClassOf(task) is not { } toolClass || _chain.NextStepForTool(toolClass, context.AvailableObjects!, context.HeldObjects) is not { } step)
                return BehaviorNeedOutput.Empty;

            var utility = Utility(task) * SupplyFactor + (IsOnShiftFor(task, schedule) ? FarmerOnShiftBonus : 0.0);
            var candidate = new BehaviorCandidate(Process, utility, WTimeSpan.FromMinutes(step.DurationMinutes), BehaviorDomain.Competence, Tags: new[] { "FieldWork", "Supply" }, TaskId: step.Id);
            return new BehaviorNeedOutput(Array.Empty<BehaviorDrive>(), new List<BehaviorCandidate> { candidate });
        }

        /// <summary>The tool class a task needs, read from its row in tasks.json; null when it needs none (sowing is done from a seed bag by hand).</summary>
        private string? ToolClassOf(FieldTask task) => _catalog.FieldTaskFor(NameOf(task))?.ToolClass;

        private bool HasToolFor(FieldTask task, BehaviorContext context)
            => ToolClassOf(task) is not { } toolClass || WorkObjects.HasTool(_catalog, toolClass, context.AvailableObjects, context.HeldObjects);

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

        /// <summary>On a Work shift of an occupation that does this field task (the tasks of the occupation come from occupations.json).</summary>
        private bool IsOnShiftFor(FieldTask task, DailyScheduleState? schedule)
            => schedule?.ActiveSlot is { PreferredAction: Work }
               && _catalog.OccupationDoesAction(schedule.Occupation, NameOf(task));
    }
}
