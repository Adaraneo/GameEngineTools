// FFFSEscapeModifier.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.World.Objects;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Fast defensive-escape activation: under detected proximal threat, boosts escape/disengagement
    /// candidates and suppresses cautious-approach candidates — distinct from the existing slow,
    /// deliberative avoidance (LossAversion λ + Prevention focus).
    /// </summary>
    /// <remarks>
    /// <para>
    /// McNaughton &amp; Corr (2004): fear/FFFS = "get me out of here" (active escape from a
    /// clear/proximal threat); anxiety/BIS = cautious approach under ambiguous/distal threat — already
    /// covered by the LossAversion/Prevention machinery and deliberately NOT duplicated here.
    /// </para>
    /// <para>
    /// <b>Trigger is a narrow, proximal signal, not chronic stress.</b> Threat is detected from a
    /// physically present hazard object (a <see cref="WorldObject"/> in the location carrying a
    /// <see cref="AffordanceType.StressRaise"/> affordance — the same hazard concept
    /// <see cref="WorldObjectAffordanceEngine"/> uses). It explicitly does NOT read
    /// <c>PsychologyState.Stress</c>, so it cannot re-create the BIS/anxiety overlap the redundancy
    /// audit rejected (Subsystem D scope: FFFS only).
    /// </para>
    /// <para>
    /// Gated OFF by default via <see cref="BehaviorConfig.FFFSEnabled"/>; a null
    /// <see cref="Traits.FFFSProfile"/> is also a no-op (backward compatible). Placed last in the
    /// modifier pipeline so that, when it fires, the fast escape boost dominates deliberative weighting.
    /// </para>
    /// </remarks>
    internal sealed class FFFSEscapeModifier : IBehaviorModifierEngine
    {
        /// <summary>Fraction of the escape boost applied as a suppression to cautious-approach candidates.</summary>
        private const double CautiousApproachSuppressionFactor = 0.5;

        /// <inheritdoc/>
        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var cfg = context.Config;
            if (!cfg.FFFSEnabled) return;

            // null FFFS profile → no escape-panic mechanics (backward compatible no-op).
            if (context.HumanContext.Personality.FFFS is not { } fffs) return;

            var threatLevel = DetectProximalThreat(context);
            if (threatLevel <= 0.0) return; // no proximal threat → fast system stays dormant

            var escapeUrgency = threatLevel * fffs.Sensitivity * cfg.FFFSEscapeMagnitude;
            if (escapeUrgency <= 0.0) return;

            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (IsEscapeCandidate(c.Name))
                    candidates[i] = c with { Utility = c.Utility + escapeUrgency };
                else if (IsCautiousApproachCandidate(c.Name))
                    // Acute escape overrides deliberative approach-under-risk with disengagement.
                    candidates[i] = c with
                    {
                        Utility = Math.Max(0.0, c.Utility - escapeUrgency * CautiousApproachSuppressionFactor)
                    };
            }
        }

        /// <summary>
        /// Proximal-threat level [0..1] = the strongest <see cref="AffordanceType.StressRaise"/> hazard
        /// among objects physically present in the character's location. No object data (tests / headless
        /// runs) or no hazard → 0 (FFFS dormant). Deliberately independent of chronic stress.
        /// </summary>
        private static double DetectProximalThreat(BehaviorContext context)
        {
            if (context.AvailableObjects is not { Count: > 0 } objects) return 0.0;

            var max = 0.0;
            foreach (var obj in objects)
            {
                foreach (var affordance in obj.Affordances)
                {
                    if (affordance.Type == AffordanceType.StressRaise)
                        max = Math.Max(max, affordance.Satisfaction);
                }
            }

            return max;
        }

        /// <summary>Active escape from a clear, proximal threat. <see cref="Flee"/> is the canonical escape.</summary>
        private static bool IsEscapeCandidate(string actionName) => actionName == Flee;

        /// <summary>
        /// Deliberative approach-under-risk actions that fast escape overrides — social approach
        /// (<see cref="ReachOut"/>, <see cref="InviteIntimacy"/>). Fight is excluded: it is
        /// antagonistic approach, not cautious approach.
        /// </summary>
        private static bool IsCautiousApproachCandidate(string actionName)
            => actionName == ReachOut || actionName == InviteIntimacy;
    }
}
