// LossAversionModifier.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System;
    using System.Collections.Generic;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Prospect-theory loss aversion: evaluates each candidate relative to the status-quo
    /// <i>reference point</i> (the currently committed action) and weights perceived <i>losses</i>
    /// (utilities below the reference) by λ, while leaving gains unchanged. This is the loss-weighting
    /// half of status-quo bias; the inaction half lives in <see cref="BehaviorConfig.InertiaWeight"/>
    /// (Gal &amp; Rucker 2018: status-quo bias is not fully reducible to loss aversion).
    /// </summary>
    /// <remarks>
    /// λ is domain-contingent — risky/uncertain-outcome domains (e.g. social approach with rejection
    /// risk) use the lower <see cref="BehaviorConfig.LossAversionLambdaRiskyChoice"/>; everyday domains
    /// use <see cref="BehaviorConfig.LossAversionLambda"/>. λ is modestly scaled by Neuroticism and,
    /// when the character carries a <see cref="Traits.RegulatoryFocusProfile"/>, by regulatory focus:
    /// Prevention pushes λ up, Promotion pulls it down (Idson, Liberman &amp; Higgins 2000; Halamish et
    /// al. 2008). A null RegulatoryFocus leaves λ at its Neuroticism-scaled value (backward compatible).
    /// Sources: Brown et al. (2024, <i>JEL</i> 62(2)); Walasek et al. (2024, <i>J. Econ. Psych.</i> 103);
    /// Kahneman &amp; Tversky (1979); Gal &amp; Rucker (2018).
    /// </remarks>
    internal sealed class LossAversionModifier : IBehaviorModifierEngine
    {
        /// <inheritdoc/>
        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            if (candidates.Count == 0) return;

            // Reference point = the status quo: the currently committed plan, else Idle.
            var reference = ReferenceUtility(context, candidates);
            if (reference is not { } refUtility) return;

            var cfg = context.Config;
            var neuroticism = context.HumanContext.Personality.BigFive.Neuroticism;

            // RegulatoryFocus λ modulation — trait-level, computed once. Prevention pushes λ up,
            // Promotion pulls it down. null RegulatoryFocus = 1.0 (no-op, backward compatible).
            var focusMultiplier = 1.0;
            if (context.HumanContext.Personality.RegulatoryFocus is { } rf)
                focusMultiplier = 1.0 + (rf.Prevention - rf.Promotion) * cfg.RegulatoryFocusLambdaModulation;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var delta = candidate.Utility - refUtility;
                if (delta >= 0) continue; // gains and the reference itself are untouched

                var lambdaBase = IsRiskyChoiceDomain(candidate.Domain)
                    ? cfg.LossAversionLambdaRiskyChoice
                    : cfg.LossAversionLambda;
                var lambda = lambdaBase
                    * (1.0 + (neuroticism - 0.5) * cfg.LossAversionNeuroticismScale)
                    * focusMultiplier;

                var adjusted = refUtility + lambda * delta; // delta < 0 → steeper loss
                candidates[i] = candidate with { Utility = Math.Max(0.0, adjusted) };
            }
        }

        /// <summary>
        /// Returns the utility of the status-quo reference candidate (current plan, then Idle), or
        /// <c>null</c> when neither is present (no reference → modifier is a no-op).
        /// </summary>
        private static double? ReferenceUtility(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var currentPlan = context.State.CurrentPlan?.Name;
            if (currentPlan is not null && TryFindUtility(candidates, currentPlan, out var planUtility))
                return planUtility;
            if (TryFindUtility(candidates, Idle, out var idleUtility))
                return idleUtility;
            return null;
        }

        private static bool TryFindUtility(List<BehaviorCandidate> candidates, string name, out double utility)
        {
            foreach (var c in candidates)
            {
                if (c.Name == name)
                {
                    utility = c.Utility;
                    return true;
                }
            }
            utility = 0.0;
            return false;
        }

        /// <summary>
        /// Social actions carry uncertain outcomes (rejection risk) and are treated as risky choices,
        /// taking the lower risky-choice λ; all other domains use the general λ.
        /// </summary>
        private static bool IsRiskyChoiceDomain(BehaviorDomain domain) => domain == BehaviorDomain.Social;
    }
}
