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
                {
                    continue;
                }

                var beforeUtility = candidate.Utility;
                var multiplier = 1.0 + Math.Min(Math.Clamp(context.Config.HabitMaxUtilityMultiplier, 0.0, 0.30), bias * Math.Clamp(context.Config.HabitMaxUtilityMultiplier, 0.0, 0.30));
                var flatBias = bias * Math.Clamp(context.Config.HabitMaxFlatBias, 0.0, 6.0);
                var afterUtility = Math.Max(0.0, (candidate.Utility * multiplier) + flatBias);
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
            {
                return;
            }

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
