// BereavementBehaviorBridge.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Needs
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using static ActionNames;

    /// <summary>
    /// Behavioral bridge that turns an unburied corpse of a grieved-for person into a
    /// <see cref="ActionNames.Bury"/> candidate, so interring the dead is chosen through normal utility
    /// arbitration rather than scripted by the scene.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors <see cref="ContingencySearchEngine"/> (the food/drink foraging bridge): it inspects the
    /// objects present at the character's location and emits a movement/interaction candidate when an
    /// internal need meets an external object. Here the "need" is an active grief
    /// (<see cref="GameEngineTools.Characters.Engines.Bereavement.LossRecord"/>) and the external object
    /// is a co-located <see cref="WorldObjectCategory.Corpse"/> carrying that person's identity.
    /// </para>
    /// <para>
    /// Only the bereaved bury — a character with no loss record for the deceased produces no candidate.
    /// The scene applies the world mutation (corpse → grave) when the character commits the action.
    /// </para>
    /// </remarks>
    internal sealed class BereavementBehaviorBridge : IBehaviorNeedEngine
    {
        #region Constants

        /// <summary>Minimum grief intensity [0..100] below which the loss is too faint to drive burial.</summary>
        private const double MinGriefToBury = 10.0;

        /// <summary>Salience floor so burial reliably beats idling even when grief has partly faded.</summary>
        private const double BurySalienceFloor = 45.0;

        /// <summary>Utility weight for burial (see <see cref="BehaviorMath.Util"/>).</summary>
        private const double BuryUtilityWeight = 0.7;

        /// <summary>Minimum grief intensity below which a grave is no longer worth visiting.</summary>
        private const double MinGriefToVisit = 10.0;

        /// <summary>Utility weight for visiting / travelling to a grave — well below survival needs.</summary>
        private const double GraveVisitUtilityWeight = 0.2;

        #endregion Constants

        #region IBehaviorNeedEngine

        /// <inheritdoc/>
        public BehaviorNeedOutput Evaluate(BehaviorContext context)
        {
            // No object provider wired (tests / headless) → nothing to bury.
            if (context.AvailableObjects is null)
                return BehaviorNeedOutput.Empty;

            var bereavement = context.HumanContext.Snapshot.Bereavement;
            if (bereavement is null || bereavement.Losses.Count == 0)
                return BehaviorNeedOutput.Empty;

            List<BehaviorCandidate>? candidates = null;
            var grievedGravePresent = false;

            foreach (var obj in context.AvailableObjects)
            {
                if (!BurialObjects.TryGetDeceased(obj, out var deceasedId))
                    continue;

                if (!TryGetLoss(bereavement, deceasedId, out var loss))
                    continue; // only the bereaved act on a corpse / grave

                if (obj.Category == WorldObjectCategory.Corpse && !loss.Buried && loss.GriefIntensity >= MinGriefToBury)
                {
                    // Inter the body — the bereaved tend to their own dead.
                    var score = Math.Max(BurySalienceFloor, loss.GriefIntensity);
                    (candidates ??= new()).Add(new BehaviorCandidate(
                        Bury,
                        BehaviorMath.Util(score, BuryUtilityWeight),
                        WTimeSpan.FromMinutes(30),
                        BehaviorDomain.Social,
                        Tags: new[] { "Bereavement" }));
                }
                else if (obj.Category == WorldObjectCategory.Grave && loss.GriefIntensity >= MinGriefToVisit)
                {
                    // Standing at the grave — a graveside mourning visit.
                    grievedGravePresent = true;
                    (candidates ??= new()).Add(new BehaviorCandidate(
                        MournAtGrave,
                        BehaviorMath.Util(loss.GriefIntensity, GraveVisitUtilityWeight),
                        WTimeSpan.FromMinutes(60),
                        BehaviorDomain.Social,
                        Tags: new[] { "Bereavement" }));
                }
            }

            // No grieved grave here, but a buried loss exists somewhere → travel toward the cemetery.
            if (!grievedGravePresent && TryGetStrongestBuriedGrief(bereavement, out var visitGrief))
            {
                (candidates ??= new()).Add(new BehaviorCandidate(
                    MoveToGrave,
                    BehaviorMath.Util(visitGrief, GraveVisitUtilityWeight),
                    WTimeSpan.FromMinutes(20),
                    BehaviorDomain.Social,
                    Tags: new[] { "Bereavement", "EnvironmentMovement" }));
            }

            return candidates is null
                ? BehaviorNeedOutput.Empty
                : new BehaviorNeedOutput(Array.Empty<BehaviorDrive>(), candidates);
        }

        #endregion IBehaviorNeedEngine

        #region Helpers

        /// <summary>Finds the loss record for <paramref name="deceasedId"/>, if the character grieves them.</summary>
        private static bool TryGetLoss(
            GameEngineTools.Characters.Engines.Bereavement.BereavementState bereavement,
            Core.HumanId deceasedId,
            out GameEngineTools.Characters.Engines.Bereavement.LossRecord loss)
        {
            foreach (var l in bereavement.Losses)
            {
                if (l.DeceasedId == deceasedId)
                {
                    loss = l;
                    return true;
                }
            }

            loss = null!;
            return false;
        }

        /// <summary>
        /// The strongest grief among the character's <i>buried</i> losses (graves to visit), or
        /// <c>false</c> when none qualifies.
        /// </summary>
        private static bool TryGetStrongestBuriedGrief(
            GameEngineTools.Characters.Engines.Bereavement.BereavementState bereavement, out double grief)
        {
            grief = 0.0;
            foreach (var loss in bereavement.Losses)
            {
                if (loss.Buried && loss.GriefIntensity >= MinGriefToVisit)
                    grief = Math.Max(grief, loss.GriefIntensity);
            }

            return grief > 0.0;
        }

        #endregion Helpers
    }
}
