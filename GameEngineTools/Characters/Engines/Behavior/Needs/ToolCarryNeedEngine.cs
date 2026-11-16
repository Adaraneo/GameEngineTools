// ToolCarryNeedEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Needs
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.World.Utils.Time;
    using GameEngineTools.World.Work;
    using static ActionNames;

    /// <summary>Offers to pick up a tool the character's work needs when one they may use lies away from any field and they carry none of that class yet.</summary>
    internal sealed class ToolCarryNeedEngine : IBehaviorNeedEngine
    {
        // A design pick: above ordinary work, so the tool is taken on the way to the job.
        private const double CarryUtility = 66.0;

        private static readonly WTimeSpan PickUpDuration = WTimeSpan.FromMinutes(1);

        private readonly WorkCatalog _catalog;

        /// <summary>Initializes a new instance of the <see cref="ToolCarryNeedEngine"/> class.</summary>
        /// <param name="catalog">The work catalog: which tool classes the occupation's tasks need and which types serve as them.</param>
        public ToolCarryNeedEngine(WorkCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <inheritdoc/>
        public BehaviorNeedOutput Evaluate(BehaviorContext context)
        {
            var schedule = context.HumanContext.Snapshot.Schedule;
            if (context.AvailableObjects is null || schedule?.ActiveSlot is not { PreferredAction: Work })
            {
                return BehaviorNeedOutput.Empty;
            }

            // At a field the tool is used where it lies; carrying is for tools found elsewhere.
            if (context.AvailableObjects.Any(o => o.Field is not null))
            {
                return BehaviorNeedOutput.Empty;
            }

            var self = context.HumanContext.Id;
            var held = context.HeldObjects ?? new List<World.Objects.WorldObject>();
            foreach (var toolClass in _catalog.ToolClassesOfOccupation(schedule.Occupation))
            {
                if (held.Any(o => WorkObjects.CanUse(o, self) && _catalog.ToolQuality(o.TypeId, toolClass) > 0.0))
                {
                    continue;
                }

                var lying = context.AvailableObjects.FirstOrDefault(
                    o => o.IsPickable && o.IsAvailable && o.HeldBy is null && WorkObjects.CanUse(o, self) && _catalog.ToolQuality(o.TypeId, toolClass) > 0.0);
                if (lying is null)
                {
                    continue;
                }

                var take = new BehaviorCandidate(
                    InteractWithObject,
                    CarryUtility,
                    PickUpDuration,
                    BehaviorDomain.Competence,
                    Tags: new[] { "FieldWork", "Supply" },
                    ObjectInteraction: new ObjectInteractionData(lying.Id, lying.LocationId, ObjectInteractionKind.Take));
                return new BehaviorNeedOutput(Array.Empty<BehaviorDrive>(), new List<BehaviorCandidate> { take });
            }

            return BehaviorNeedOutput.Empty;
        }
    }
}
