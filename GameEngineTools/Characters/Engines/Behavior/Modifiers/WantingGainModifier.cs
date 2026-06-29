// WantingGainModifier.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Applies a cue-gated multiplicative incentive-salience gain κ to already-transformed candidate
    /// utility (post <see cref="LossAversionModifier"/>, post <see cref="DiscountedValueModifier"/>).
    /// κ ≥ 1; κ = 1 is neutral.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Formalism: Zhang, Berridge, Tindell, Smith &amp; Aldridge (2009, <i>PLoS Comput Biol</i>
    /// 5(7):e1000437), simplified to a pure multiplicative gain per Smith &amp; Read (2021).
    /// </para>
    /// <para>
    /// <b>Cue-gated</b> — fires only for candidates whose reward cue is physically present, i.e. a
    /// <see cref="World.Objects.WorldObject"/> in the character's location carries an affordance that
    /// targets this action (reusing <see cref="AffordanceCandidateMap"/>). It is deliberately NOT a
    /// constant trait multiplier on every candidate — that would collapse Wanting into a second
    /// Promotion/RegulatoryFocus trait and violate the redundancy guardrail.
    /// </para>
    /// <para>
    /// A null <see cref="Traits.WantingSensitivityProfile"/> is a no-op (κ = 1.0), and the whole
    /// modifier is gated OFF by default via <see cref="BehaviorConfig.WantingGainEnabled"/>.
    /// </para>
    /// </remarks>
    internal sealed class WantingGainModifier : IBehaviorModifierEngine
    {
        /// <inheritdoc/>
        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var cfg = context.Config;
            if (!cfg.WantingGainEnabled) return;

            // null profile → κ = 1.0 (neutral, backward compatible).
            if (context.HumanContext.Personality.WantingSensitivity is not { } profile) return;

            var kappa = 1.0 + profile.WantingSensitivity * cfg.WantingGainMaxBoost;
            if (kappa == 1.0) return;

            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (!IsCueTriggered(context, c)) continue; // only present-cue candidates get the gain
                candidates[i] = c with { Utility = c.Utility * kappa };
            }
        }

        /// <summary>
        /// A candidate is cue-triggered when a reward-associated object affording its action is present
        /// in the character's location. Reuses <see cref="AffordanceCandidateMap"/> so the cue mapping
        /// stays consistent with <see cref="WorldObjectAffordanceEngine"/>. No object data (tests /
        /// headless runs) → not cue-triggered.
        /// </summary>
        private static bool IsCueTriggered(BehaviorContext context, BehaviorCandidate candidate)
        {
            if (context.AvailableObjects is not { Count: > 0 } objects) return false;

            foreach (var obj in objects)
            {
                foreach (var affordance in obj.Affordances)
                {
                    if (Array.IndexOf(AffordanceCandidateMap.TargetsFor(affordance.Type), candidate.Name) >= 0)
                        return true;
                }
            }

            return false;
        }
    }
}
