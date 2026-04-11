// LearnedHabitEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Applies bounded utility pressure from long-term learned habit traces.
    /// </summary>
    internal sealed class LearnedHabitEngine : IBehaviorModifierEngine
    {
        #region IBehaviorModifierEngine

        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var bias = BehaviorHabitLearning.ComputeCandidateBias(context, candidate);
                if (bias <= 0.0)
                {
                    continue;
                }

                var multiplier = 1.0 + Math.Min(context.Config.HabitMaxUtilityMultiplier, bias * context.Config.HabitMaxUtilityMultiplier);
                var flatBias = bias * Math.Max(0.0, context.Config.HabitMaxFlatBias);
                candidates[i] = candidate with { Utility = Math.Max(0.0, (candidate.Utility * multiplier) + flatBias) };
            }
        }

        #endregion IBehaviorModifierEngine
    }
}
