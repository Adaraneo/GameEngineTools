// LearnedHabitEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Logging;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Applies bounded utility pressure from long-term learned habit traces.
    /// </summary>
    /// <remarks>
    /// Locomotion actions (<c>MoveTo:*</c>) use a separate, lower ceiling defined by
    /// <see cref="BehaviorConfig.LocomotionHabitMultiplierCap"/> and
    /// <see cref="BehaviorConfig.LocomotionHabitFlatBiasCap"/>. This prevents the habit
    /// system from reinforcing instrumental movement so strongly that it permanently
    /// outbids the terminal interaction it was meant to enable (e.g. <c>ReachOut</c>).
    /// </remarks>
    internal sealed class LearnedHabitEngine : IBehaviorModifierEngine
    {
        #region Private fields

        private readonly ILogger? _log;

        #endregion Private fields

        #region Construction

        public LearnedHabitEngine(ILogger? log = null)
        {
            _log = log;
        }

        #endregion Construction

        #region IBehaviorModifierEngine

        /// <inheritdoc/>
        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];

                var bias = BehaviorHabitLearning.ComputeCandidateBias(
                    context,
                    candidate,
                    context.HabitApplicabilityModulator ?? NoOpHabitApplicabilityModulator.Instance);

                if (bias <= 0.0)
                    continue;

                // Locomotion actions use a lower ceiling than terminal actions.
                // Moving toward a social space is a means, not an end — over-reinforcing
                // it blocks the very interaction the character is trying to have.
                var isLocomotion = IsLocomotionAction(candidate.Name);
                var maxMultiplier = isLocomotion
                    ? Math.Clamp(context.Config.LocomotionHabitMultiplierCap, 0.0, 0.30)
                    : Math.Clamp(context.Config.HabitMaxUtilityMultiplier, 0.0, 0.30);
                var maxFlatBias = isLocomotion
                    ? Math.Clamp(context.Config.LocomotionHabitFlatBiasCap, 0.0, 6.0)
                    : Math.Clamp(context.Config.HabitMaxFlatBias, 0.0, 6.0);

                var beforeUtility = candidate.Utility;
                var multiplier = 1.0 + Math.Min(maxMultiplier, bias * maxMultiplier);
                var flatBias = bias * maxFlatBias;
                var afterUtility = Math.Max(0.0, candidate.Utility * multiplier + flatBias);

                candidates[i] = candidate with { Utility = afterUtility };

                LogBiasApplied(context, candidate.Name, beforeUtility, afterUtility, bias, multiplier, flatBias);

                if (_log is not null && afterUtility > 0.0)
                {
                    var habitContribution = afterUtility - beforeUtility;
                    var ratio = habitContribution / afterUtility;
                    if (ratio > 0.40)
                    {
                        using (_log.BeginCharacterScope(context.HumanContext.Id.Value, nameof(LearnedHabitEngine)))
                        {
                            _log.HabitDominatedDecision(
                                context.HumanContext.Id.Value.ToString(),
                                candidate.Name,
                                habitContribution,
                                afterUtility,
                                ratio);
                        }
                    }
                }
            }
        }

        #endregion IBehaviorModifierEngine

        #region Helpers

        /// <summary>
        /// Returns <c>true</c> when the action is a locomotion candidate (<c>MoveTo:*</c>).
        /// Locomotion is instrumental — it positions the character for a terminal action
        /// but does not directly satisfy the underlying need.
        /// </summary>
        /// <param name="actionName">Candidate action name.</param>
        private static bool IsLocomotionAction(string actionName)
            => actionName.StartsWith("MoveTo:", StringComparison.OrdinalIgnoreCase);

        #endregion Helpers

        #region Logging

        private void LogBiasApplied(
            BehaviorContext context,
            string actionName,
            double beforeUtility,
            double afterUtility,
            double bias,
            double multiplier,
            double flatBias)
        {
            if (_log is null)
                return;

            using (_log.BeginCharacterScope(context.HumanContext.Id.Value, nameof(LearnedHabitEngine)))
            {
                _log.BehaviorHabitBiasApplied(
                    context.HumanContext.Id.Value.ToString(),
                    actionName,
                    beforeUtility,
                    afterUtility,
                    bias,
                    multiplier,
                    flatBias);
            }
        }

        #endregion Logging
    }
}
