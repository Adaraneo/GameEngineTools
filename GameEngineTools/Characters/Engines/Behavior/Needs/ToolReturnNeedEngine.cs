// ToolReturnNeedEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Needs
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Engines.Objects;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using static ActionNames;

    /// <summary>Brings a carried tool back to its shed and puts it down there (which also ends a loan): off the work shift, or on it when the memory that the tool is owed back is strong.</summary>
    internal sealed class ToolReturnNeedEngine : IBehaviorNeedEngine
    {
        // Design picks: the walk back ranks under ordinary work; putting the tool down once at the shed is an instant act, so it goes first (a year-run showed a borrowed tool never put down at 58 against hunger, thirst and work).
        private const double WalkUtility = 56.0;
        private const double PutDownUtility = 90.0;

        private static readonly WTimeSpan WalkDuration = WTimeSpan.FromMinutes(20);
        private static readonly WTimeSpan PutDownDuration = WTimeSpan.FromSeconds(10);

        /// <inheritdoc/>
        public BehaviorNeedOutput Evaluate(BehaviorContext context)
        {
            var snapshot = context.HumanContext.Snapshot;
            if (context.HeldObjects is not { Count: > 0 } held)
            {
                return BehaviorNeedOutput.Empty;
            }

            var tool = held.FirstOrDefault(o => o.Category == WorldObjectCategory.Tool && o.StoredAt is not null);
            if (tool is null || snapshot.InteractionSurface?.Location is not { } here)
            {
                return BehaviorNeedOutput.Empty;
            }

            // The memory that the tool is owed back; on the work shift only a strongly felt one brings the tool back.
            var strength = ToolObligation.StrengthFor(snapshot.Memory.Episodes, tool.Id);
            if (snapshot.Schedule?.ActiveSlot is { PreferredAction: Work } && !ToolObligation.FeltOnShift(strength))
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
                : new BehaviorCandidate(MoveToStorage, ToolObligation.WalkUtility(WalkUtility, strength), WalkDuration, BehaviorDomain.Competence, Tags: new[] { "Supply" });
            return new BehaviorNeedOutput(Array.Empty<BehaviorDrive>(), new List<BehaviorCandidate> { candidate });
        }
    }
}
