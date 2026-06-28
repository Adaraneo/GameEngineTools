// DailyScheduleBehaviorModifier.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Schedule
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Logging;
    using Microsoft.Extensions.Logging;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Applies a flat utility bias to the behavior candidate that matches the currently
    /// active schedule slot. This is a soft nudge — a stressed or exhausted character
    /// can still override their schedule through the physiological need hierarchy.
    /// </summary>
    internal sealed class DailyScheduleBehaviorModifier : IBehaviorModifierEngine
    {
        #region Private fields

        private readonly ILogger? _log;
        private readonly DailyScheduleConfig _config;

        #endregion Private fields

        #region Construction

        public DailyScheduleBehaviorModifier(ILogger? log = null, DailyScheduleConfig? config = null)
        {
            _log = log;
            _config = config ?? new DailyScheduleConfig();
        }

        #endregion Construction

        #region IBehaviorModifierEngine

        /// <inheritdoc/>
        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var activeSlot = context.HumanContext.Snapshot.Schedule?.ActiveSlot;
            if (activeSlot is null) return;

            // Check skip conditions for skippable slots
            if (activeSlot.CanSkipWhenStressed)
            {
                var stress = context.HumanContext.Snapshot.Psychology.Stress;
                var energy = context.HumanContext.Snapshot.Physiology.Energy;

                if (stress > _config.SkipStressThreshold || energy < _config.SkipEnergyThreshold)
                {
                    if (_log is not null)
                    {
                        using (_log.BeginCharacterScope(context.HumanContext.Id.Value, nameof(DailyScheduleBehaviorModifier)))
                        {
                            _log.ScheduleSlotSkipped(
                                context.HumanContext.Id.Value.ToString(),
                                activeSlot.SlotId,
                                stress,
                                energy);
                        }
                    }

                    return;
                }
            }

            var flatBias = activeSlot.BiasStrength * _config.MaxSlotFlatBias;
            var humanId = context.HumanContext.Id.Value.ToString();

            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];

                // Primary action bias
                if (c.Name == activeSlot.PreferredAction)
                {
                    candidates[i] = c with { Utility = Math.Max(0.0, c.Utility + flatBias) };

                    context.Outbox.Add(new ScheduleSlotBiasApplied(
                        context.Now, context.HumanContext.Id, activeSlot.SlotId, c.Name, flatBias));

                    if (_log is not null)
                    {
                        using (_log.BeginCharacterScope(context.HumanContext.Id.Value, nameof(DailyScheduleBehaviorModifier)))
                        {
                            _log.ScheduleSlotBiasApplied(humanId, flatBias, c.Name, activeSlot.SlotId);
                        }
                    }
                }
            }

            // ── Commute bias ─────────────────────────────────────────────────────────
            // The scheduled action (e.g. Work) is location-bound: it is hard-gated away when the
            // character is not where it can be performed (no Tool at home). Without help the slot
            // bias above is wasted on a candidate that gets removed, and the character never sets
            // off. So when the slot's action needs a kind of place the character is not currently
            // at, nudge the matching MoveTo:<type> candidate — this is the "commute" that gets them
            // to the office / social venue / etc. The orchestrator resolves the concrete location.
            ApplyCommuteBias(context, candidates, activeSlot, flatBias, humanId);
        }

        /// <summary>
        /// Boosts the <c>MoveTo:&lt;type&gt;</c> candidate that takes the character toward a place
        /// where the active slot's action is possible, when they are not already at such a place.
        /// </summary>
        /// <param name="flatBias">
        /// The bias the slot's primary action received. When that action is location-bound and gated
        /// away here, its urgency transfers to the commute so the move competes as strongly as the
        /// action itself would have on this single trigger tick — otherwise the wasted primary bias
        /// never translates into actually setting off.
        /// </param>
        private void ApplyCommuteBias(BehaviorContext context, List<BehaviorCandidate> candidates, ScheduleSlot activeSlot, double flatBias, string humanId)
        {
            var commute = ResolveCommute(activeSlot.PreferredAction);
            if (commute is null)
                return;

            var (moveToAction, targetKind) = commute.Value;

            // Already on a suitable surface → no commute needed (the action can be performed here).
            var currentKind = context.HumanContext.Snapshot.InteractionSurface.Kind;
            if (currentKind == targetKind)
                return;

            // Transfer the gated-away action's urgency to the move, plus the location nudge.
            var moveBias = flatBias + activeSlot.BiasStrength * _config.MoveToLocationBias;
            if (moveBias <= 0.0)
                return;

            // No-op when the MoveTo candidate is absent (e.g. that need engine is not wired).
            BehaviorCandidateEditor.Add(candidates, moveToAction, moveBias);

            context.Outbox.Add(new ScheduleSlotBiasApplied(
                context.Now, context.HumanContext.Id, activeSlot.SlotId, moveToAction, moveBias));

            if (_log is not null)
            {
                using (_log.BeginCharacterScope(context.HumanContext.Id.Value, nameof(DailyScheduleBehaviorModifier)))
                {
                    _log.ScheduleSlotBiasApplied(humanId, moveBias, moveToAction, activeSlot.SlotId);
                }
            }
        }

        /// <summary>
        /// Maps a scheduled action to the movement that reaches a place it can be performed, plus
        /// the <see cref="SurfaceKind"/> that counts as "already there". Object-need actions
        /// (Eat/Drink) are intentionally excluded — foraging movement is need-driven via
        /// <see cref="Behavior.Needs.ContingencySearchEngine"/>. Returns <c>null</c> for actions
        /// with no location-type movement.
        /// </summary>
        private static (string MoveToAction, SurfaceKind TargetKind)? ResolveCommute(string preferredAction)
            => preferredAction switch
            {
                Work or Create               => (MoveToWork, SurfaceKind.Work),
                ReachOut or InviteIntimacy   => (MoveToSocial, SurfaceKind.Social),
                SelfCare                     => (MoveToPrivate, SurfaceKind.Private),
                Idle                         => (MoveToRest, SurfaceKind.Rest),
                _                            => null
            };

        #endregion IBehaviorModifierEngine
    }
}
