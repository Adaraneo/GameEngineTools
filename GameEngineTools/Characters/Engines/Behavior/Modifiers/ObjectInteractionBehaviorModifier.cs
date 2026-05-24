// ObjectInteractionBehaviorModifier.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Generates <see cref="ActionNames.InteractWithObject"/> candidates when pickable or
    /// usable world objects are present at the character's current location.
    /// Need-relevance gates utility: low-need situations produce low utility so that
    /// core physiological actions (eat, drink, sleep) remain dominant when pressing.
    /// Also generates Drop candidates when the character is holding an object.
    /// </summary>
    internal sealed class ObjectInteractionBehaviorModifier : IBehaviorModifierEngine
    {
        private const double BaseCandidateUtility = 15.0;
        private const double NeedScaleWeight = 0.4;
        private const double SatisfactionWeight = 0.3;
        private const double MinNeedThreshold = 20.0; // below this, need is not pressing enough
        private const double DropBaseUtility = 5.0;   // low — only selected if truly nothing better

        /// <summary>
        /// Optional concrete provider used to find held objects.
        /// <c>null</c> disables Drop candidate generation.
        /// </summary>
        private readonly IMutableWorldObjectProvider? _objectProvider;

        public ObjectInteractionBehaviorModifier(IMutableWorldObjectProvider? objectProvider = null)
        {
            _objectProvider = objectProvider;
        }

        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            // ── Take / UseInPlace candidates: objects present at current location ──
            if (context.AvailableObjects is not null)
            {
                var snapshot = context.HumanContext.Snapshot;

                foreach (var obj in context.AvailableObjects.Where(o => o.IsAvailable))
                {
                    ObjectInteractionData? bestInteraction = null;
                    double bestUtility = 0.0;

                    foreach (var affordance in obj.Affordances)
                    {
                        var needScore = GetNeedScore(affordance, snapshot);
                        if (needScore < MinNeedThreshold && affordance.Type != AffordanceType.Ownership)
                            continue;

                        var kind = affordance.Type == AffordanceType.Ownership
                            ? ObjectInteractionKind.Take
                            : ObjectInteractionKind.UseInPlace;

                        // Only generate Take candidates for pickable objects
                        if (kind == ObjectInteractionKind.Take && !obj.IsPickable)
                            continue;

                        var utility = BaseCandidateUtility
                            + (needScore / 100.0) * NeedScaleWeight * 100.0
                            + affordance.Satisfaction * SatisfactionWeight * 100.0;

                        if (utility > bestUtility)
                        {
                            bestUtility = utility;
                            bestInteraction = new ObjectInteractionData(obj.Id, obj.LocationId, kind);
                        }
                    }

                    if (bestInteraction is not null)
                    {
                        candidates.Add(new BehaviorCandidate(
                            Name: ActionNames.InteractWithObject,
                            Utility: bestUtility,
                            Duration: WTimeSpan.FromMinutes(1),
                            Domain: BehaviorDomain.Physiological,
                            ObjectInteraction: bestInteraction));
                    }
                }
            }

            // ── Drop candidates: character is holding something ────────────────
            if (_objectProvider is null)
                return;

            var currentLocationId = context.HumanContext.Snapshot.InteractionSurface?.Location;
            if (string.IsNullOrEmpty(currentLocationId))
                return;

            foreach (var heldObj in _objectProvider.GetHeldBy(context.HumanContext.Id))
            {
                candidates.Add(new BehaviorCandidate(
                    Name: ActionNames.InteractWithObject,
                    Utility: DropBaseUtility,
                    Duration: WTimeSpan.FromMinutes(0.5),
                    Domain: BehaviorDomain.Physiological,
                    ObjectInteraction: new ObjectInteractionData(
                        heldObj.Id,
                        currentLocationId,
                        ObjectInteractionKind.Drop)));
            }
        }

        private static double GetNeedScore(
            WorldObjectAffordance affordance,
            Characters.Core.EnginesSnapshot snapshot)
        {
            return affordance.Type switch
            {
                AffordanceType.Hunger => snapshot.Physiology.Hunger,
                AffordanceType.Rest => snapshot.Behavior.NeedRest,
                AffordanceType.Social => snapshot.Behavior.NeedBelonging,
                AffordanceType.Work => snapshot.Behavior.NeedCompetence,
                AffordanceType.Entertainment => snapshot.Behavior.NeedCompetence,
                AffordanceType.Warmth => snapshot.Physiology.BodyTempDelta < -1.0 ? 70.0 : 10.0,
                AffordanceType.MoodBoost => (100.0 - snapshot.Psychology.MoodBaseline),
                AffordanceType.Ownership => 50.0, // flat moderate desire to take items
                _ => 0.0
            };
        }
    }
}
