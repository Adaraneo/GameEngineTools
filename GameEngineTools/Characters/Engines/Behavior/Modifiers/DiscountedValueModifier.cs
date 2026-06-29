// DiscountedValueModifier.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System.Collections.Generic;

    /// <summary>
    /// Hyperboloid temporal discounting (Green &amp; Myerson 2004, <c>V = A/(1+kD)^s</c>): scales each
    /// candidate's utility by a delay-dependent factor F(D) ∈ (0, 1], so options whose reward is
    /// further off are valued less.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pipeline position (load-bearing):</b> registered in
    /// <see cref="DefaultBehaviorEngine"/> <i>immediately after</i> <see cref="LossAversionModifier"/>.
    /// Discounting is applied to the already loss-aversion-transformed utility, not the raw magnitude —
    /// value transform first, then discount (Loewenstein &amp; Prelec 1992). Reordering would double-count
    /// or mis-sequence the two effects.
    /// </para>
    /// <para>
    /// <b>Delay source "D" (MVP simplification):</b> <see cref="BehaviorCandidate"/> has no explicit
    /// "delay to reward" field, so D is taken from <see cref="BehaviorCandidate.Duration"/> (in days) as
    /// a proxy — longer-running actions deliver their payoff later. This is a deliberate approximation,
    /// not a true delay-to-reward model; a real per-candidate delay (e.g. from goal completion estimates)
    /// is future work.
    /// </para>
    /// <para>
    /// <b>Per-agent k:</b> read from <see cref="Traits.TemporalDiscountProfile.K"/> when present, else
    /// the population mean <see cref="BehaviorConfig.DiscountRateKMean"/> (backward-compatible no per-agent
    /// variance). Deliberately uncorrelated with Big Five (Yeh, Myerson &amp; Green 2021).
    /// </para>
    /// <para>
    /// <b>Quasi-hyperbolic mode:</b> when <see cref="BehaviorConfig.UseQuasiHyperbolicMode"/> is set the
    /// modifier switches to Laibson's (1997) β-δ form for time-inconsistency / commitment-device
    /// scenarios; δ is derived from the per-agent k as <c>1/(1+k)</c> and β is
    /// <see cref="BehaviorConfig.PresentBiasBeta"/>.
    /// </para>
    /// <para>
    /// If <see cref="BehaviorConfig.TemporalDiscountingEnabled"/> is <c>false</c> the modifier is a no-op
    /// (global kill switch).
    /// </para>
    /// </remarks>
    internal sealed class DiscountedValueModifier : IBehaviorModifierEngine
    {
        // ── Domain multiplier on the shared k (research gate (c)) ─────────────────────────────────
        // Money lowest, consumption/health higher — a multiplier on the shared rate, NOT independent
        // per-domain parameters. BehaviorDomain currently has no "money/financial" value, so the lower
        // financial multiplier is unrepresented (left as universal 1.0). Consumption (Physiological)
        // discounts faster than competence/social/etc.
        // TODO: introduce a financial/money BehaviorDomain (×0.8) when an economic action set is added;
        //       expose DiscountDomainMultipliers via config once the enum is no longer internal.
        private const double ConsumptionDomainMultiplier = 1.3; // Physiological (food/drink/consumption)
        private const double DefaultDomainMultiplier = 1.0;     // Competence / Social / Autonomy / Exploration

        /// <inheritdoc/>
        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var cfg = context.Config;
            if (!cfg.TemporalDiscountingEnabled || candidates.Count == 0) return;

            // Per-agent k, falling back to the population mean when no profile is present.
            var baseK = context.HumanContext.Personality.TemporalDiscount?.K ?? cfg.DiscountRateKMean;
            var s = cfg.DiscountHyperboloidExponent;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var delayDays = candidate.Duration.TotalDays; // MVP proxy for delay-to-reward
                if (delayDays <= 0.0) continue;                // present-valued actions are untouched

                var effectiveK = baseK * DomainMultiplier(candidate.Domain);

                double factor;
                if (cfg.UseQuasiHyperbolicMode)
                {
                    // δ derived from the per-agent rate; β is the present-bias jump.
                    var delta = 1.0 / (1.0 + effectiveK);
                    factor = DiscountedValueMath.QuasiHyperbolicFactor(delayDays, cfg.PresentBiasBeta, delta);
                }
                else
                {
                    factor = DiscountedValueMath.HyperboloidFactor(delayDays, effectiveK, s);
                }

                candidates[i] = candidate with { Utility = candidate.Utility * factor };
            }
        }

        /// <summary>
        /// Domain multiplier on the shared k. Consumption (Physiological) discounts faster; all other
        /// domains use the neutral 1.0 (see class remarks and the financial-domain TODO).
        /// </summary>
        private static double DomainMultiplier(BehaviorDomain domain)
            => domain == BehaviorDomain.Physiological ? ConsumptionDomainMultiplier : DefaultDomainMultiplier;
    }
}
