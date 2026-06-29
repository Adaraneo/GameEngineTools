// RegulatoryFitModifier.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System.Collections.Generic;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Small, low-confidence regulatory-fit bonus: eager-strategy actions get a Promotion-scaled bonus,
    /// vigilant-strategy actions get a Prevention-scaled bonus (Higgins' "feeling right" from strategy↔focus
    /// match).
    /// </summary>
    /// <remarks>
    /// Anchored to r≈.10 (Motyka et al. 2014); post-2015 replication is mixed (Janson, Siebert &amp;
    /// Dickhäuser 2022 found most predicted fit effects non-significant). Feature-flagged OFF by default
    /// via <see cref="BehaviorConfig.RegulatoryFitEnabled"/> — treat as a flavor effect, not load-bearing.
    /// A null <see cref="Traits.RegulatoryFocusProfile"/> is a no-op.
    /// </remarks>
    internal sealed class RegulatoryFitModifier : IBehaviorModifierEngine
    {
        /// <summary>Strategic orientation of an action, matched against a character's regulatory focus.</summary>
        private enum Strategy
        {
            /// <summary>Neither eager nor vigilant — receives no fit bonus.</summary>
            Neutral,

            /// <summary>Approach toward gains/growth/connection — fits Promotion.</summary>
            Eager,

            /// <summary>Caution / avoidance of loss / maintenance of safety — fits Prevention.</summary>
            Vigilant
        }

        /// <inheritdoc/>
        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var cfg = context.Config;
            if (!cfg.RegulatoryFitEnabled) return;
            if (context.HumanContext.Personality.RegulatoryFocus is not { } rf) return;

            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                var bonus = ClassifyStrategy(c) switch
                {
                    Strategy.Eager => rf.Promotion * cfg.RegulatoryFitBonusMagnitude,
                    Strategy.Vigilant => rf.Prevention * cfg.RegulatoryFitBonusMagnitude,
                    _ => 0.0
                };
                if (bonus != 0.0)
                    candidates[i] = c with { Utility = c.Utility + bonus };
            }
        }

        /// <summary>
        /// Classifies a candidate as eager (approach/growth → fits Promotion) or vigilant
        /// (cautious/maintenance → fits Prevention), analogous to <c>IsRiskyChoiceDomain</c> in
        /// <see cref="LossAversionModifier"/>. Action name takes precedence, then the broad domain.
        /// </summary>
        private static Strategy ClassifyStrategy(BehaviorCandidate c)
        {
            switch (c.Name)
            {
                // Approach toward gains / growth / connection.
                case Create:
                case ReachOut:
                case InviteIntimacy:
                    return Strategy.Eager;

                // Self-protective / loss-avoidant.
                case Flee:
                case SelfCare:
                    return Strategy.Vigilant;
            }

            return c.Domain switch
            {
                BehaviorDomain.Exploration => Strategy.Eager,   // novelty seeking — promotion strategy
                BehaviorDomain.Autonomy => Strategy.Eager,      // self-expansion — promotion strategy
                BehaviorDomain.Physiological => Strategy.Vigilant, // homeostatic deficit-avoidance — prevention
                _ => Strategy.Neutral
            };
        }
    }
}
