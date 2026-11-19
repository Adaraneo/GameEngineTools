// ToolReturnNeedEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Needs
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using static ActionNames;

    /// <summary>Off the work shift, brings a carried tool back to its shed and puts it down there (which also ends a loan).</summary>
    internal sealed class ToolReturnNeedEngine : IBehaviorNeedEngine
    {
        // Design picks: the walk back ranks under ordinary work; putting the tool down once at the shed takes a minute, so it goes first (a year-run showed a borrowed tool never put down at 58 against hunger, thirst and work).
        private const double WalkUtility = 56.0;
        private const double PutDownUtility = 90.0;

        private static readonly WTimeSpan WalkDuration = WTimeSpan.FromMinutes(20);
        private static readonly WTimeSpan PutDownDuration = WTimeSpan.FromMinutes(1);

        /// <inheritdoc/>
        public BehaviorNeedOutput Evaluate(BehaviorContext context)
        {
            var snapshot = context.HumanContext.Snapshot;
            if (snapshot.Schedule?.ActiveSlot is { PreferredAction: Work } || context.HeldObjects is not { Count: > 0 } held)
            {
                return BehaviorNeedOutput.Empty;
            }

            var tool = held.FirstOrDefault(o => o.Category == WorldObjectCategory.Tool && o.StoredAt is not null);
            if (tool is null || snapshot.InteractionSurface?.Location is not { } here)
            {
                return BehaviorNeedOutput.Empty;
            }

            var candidate = string.Equals(tool.StoredAt, here, StringComparison.Ordinal)
                ? new BehaviorCandidate(
                    InteractWithObject,
                    PutDownUtility,
                    PutDownDuration,
                    BehaviorDomain.Competence,
                    Tags: new[] { "Supply" },
                    ObjectInteraction: new ObjectInteractionData(tool.Id, here, ObjectInteractionKind.Drop))
                : new BehaviorCandidate(MoveToStorage, WalkUtility, WalkDuration, BehaviorDomain.Competence, Tags: new[] { "Supply" });
            return new BehaviorNeedOutput(Array.Empty<BehaviorDrive>(), new List<BehaviorCandidate> { candidate });
        }
    }
}
