// DailyScheduleBehaviorModifier.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Schedule
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Logging;
    using Microsoft.Extensions.Logging;

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

        #endregion

        #region Construction

        public DailyScheduleBehaviorModifier(ILogger? log = null, DailyScheduleConfig? config = null)
        {
            _log = log;
            _config = config ?? new DailyScheduleConfig();
        }

        #endregion

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
            var humanId  = context.HumanContext.Id.Value.ToString();

            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];

                // Primary action bias
                if (c.Name == activeSlot.PreferredAction)
                {
                    candidates[i] = c with { Utility = Math.Max(0.0, c.Utility + flatBias) };

                    if (_log is not null)
                    {
                        using (_log.BeginCharacterScope(context.HumanContext.Id.Value, nameof(DailyScheduleBehaviorModifier)))
                        {
                            _log.ScheduleSlotBiasApplied(humanId, flatBias, c.Name, activeSlot.SlotId);
                        }
                    }

                    // TODO: MoveTo location bias — requires BehaviorCandidate.TargetLocationId field
                    // which does not exist yet. Implement when that field is added.
                    // If activeSlot.PreferredLocationId is not null and character is not there,
                    // also boost the matching MoveTo[location] candidate by Config.MoveToLocationBias.
                }
            }
        }

        #endregion
    }
}
