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

    /// <summary>Brings a carried tool back: its own to the shed, to put it down there, a borrowed one to its owner, to hand it over; off the work shift, or on it when the memory that the tool is owed back is strong.</summary>
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

            // A borrowed tool goes back to the owner it came from, not to the shed, and only after it has been kept a while.
            var lent = tool.LentTo == context.HumanContext.Id;
            if (lent && tool.LentAt is { } lentAt && (context.Now - lentAt).TotalHours < ToolObligation.MinHoldHours)
            {
                return BehaviorNeedOutput.Empty;
            }

            // The memory that the tool is owed back; on the work shift only a strongly felt one brings the tool back.
            var strength = ToolObligation.StrengthFor(snapshot.Memory.Episodes, tool.Id);
            if (!ToolObligation.ReturnsNow(snapshot.Schedule?.ActiveSlot is { PreferredAction: Work }, strength))
            {
                return BehaviorNeedOutput.Empty;
            }

            if (!lent && string.Equals(tool.StoredAt, here, StringComparison.Ordinal))
            {
                var putDown = new BehaviorCandidate(
                    InteractWithObject,
                    PutDownUtility,
                    PutDownDuration,
                    BehaviorDomain.Competence,
                    Tags: new[] { "Supply" },
                    ObjectInteraction: new ObjectInteractionData(tool.Id, here, ObjectInteractionKind.Drop));
                return new BehaviorNeedOutput(Array.Empty<BehaviorDrive>(), new List<BehaviorCandidate> { putDown });
            }

            // A walk back that was judged too far is not tried again for a while.
            if (context.State.ToolReturnRetryAt is { } retryAt && context.Now < retryAt)
            {
                return BehaviorNeedOutput.Empty;
            }

            var walk = new BehaviorCandidate(lent ? MoveToOwner : MoveToStorage, ToolObligation.WalkUtility(WalkUtility, strength), WalkDuration, BehaviorDomain.Competence, Tags: new[] { "Supply" });
            return new BehaviorNeedOutput(Array.Empty<BehaviorDrive>(), new List<BehaviorCandidate> { walk });
        }
    }
}
