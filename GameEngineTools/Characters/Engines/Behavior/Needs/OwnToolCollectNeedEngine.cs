// OwnToolCollectNeedEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Needs
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using static ActionNames;

    /// <summary>Offers to take back one's own tool seen lying away from its shed: a soft pull that a busy or careless character may pass by, not a rule.</summary>
    internal sealed class OwnToolCollectNeedEngine : IBehaviorNeedEngine
    {
        // Design picks: a base just under the tool-carry pull, raised by Conscientiousness; two hands, so at most two tools are carried.
        private const double BaseUtility = 50.0;
        private const double ConscientiousnessUtility = 20.0;
        private const int MaxCarriedTools = 2;

        private static readonly WTimeSpan PickUpDuration = WTimeSpan.FromMinutes(1);

        /// <inheritdoc/>
        public BehaviorNeedOutput Evaluate(BehaviorContext context)
        {
            if (context.AvailableObjects is null)
            {
                return BehaviorNeedOutput.Empty;
            }

            var self = context.HumanContext.Id;
            if (context.HeldObjects is { } held && held.Count(o => o.Category == WorldObjectCategory.Tool) >= MaxCarriedTools)
            {
                return BehaviorNeedOutput.Empty;
            }

            var lying = context.AvailableObjects.FirstOrDefault(
                o => o.Category == WorldObjectCategory.Tool && o.IsPickable && o.IsAvailable && o.HeldBy is null && o.LentTo is null
                     && o.OwnerId == self && o.StoredAt is { } shed && !string.Equals(shed, o.LocationId, StringComparison.Ordinal));
            if (lying is null)
            {
                return BehaviorNeedOutput.Empty;
            }

            var utility = BaseUtility + (ConscientiousnessUtility * context.HumanContext.Personality.BigFive.Conscientiousness);
            var take = new BehaviorCandidate(
                InteractWithObject,
                utility,
                PickUpDuration,
                BehaviorDomain.Competence,
                Tags: new[] { "Supply" },
                ObjectInteraction: new ObjectInteractionData(lying.Id, lying.LocationId, ObjectInteractionKind.Take));
            return new BehaviorNeedOutput(Array.Empty<BehaviorDrive>(), new List<BehaviorCandidate> { take });
        }
    }
}
