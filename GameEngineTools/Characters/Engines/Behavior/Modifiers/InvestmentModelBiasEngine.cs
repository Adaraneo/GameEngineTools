// InvestmentModelBiasEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Engines.Relationships;
    using static ActionNames;

    /// <summary>
    /// Biases social arbitration according to the Rusbult Investment Model: low commitment to the
    /// current partner combined with a high-quality alternative shifts utility away from partner
    /// maintenance and toward approaching the alternative — the emergent "leaving" pressure.
    /// </summary>
    /// <remarks>
    /// Reads per-edge Commitment / AlternativeQuality (computed by <see cref="DefaultRelationshipsEngine"/>).
    /// Only adjusts utility; it never selects an action or emits dissolution itself
    /// (single-responsibility — state transitions stay in the relationship engine).
    /// </remarks>
    internal sealed class InvestmentModelBiasEngine : IBehaviorModifierEngine
    {
        #region IBehaviorModifierEngine

        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var edges = context.HumanContext.Snapshot.Relationships?.Edges;
            if (edges is null || edges.Count == 0)
            {
                return;
            }

            // Identify the current primary partner (highest-commitment romantic/partner edge).
            var partner = edges.Values
                .Where(e => e.KinRole == KinRole.Partner || e.IntimateAffinity >= 30.0)
                .OrderByDescending(e => e.Commitment)
                .FirstOrDefault();
            if (partner is null)
            {
                return;
            }

            // Low commitment + better alternative available → leave pressure in [0, 1].
            var leavePressure = Math.Clamp(
                (partner.AlternativeQuality - partner.Commitment) / 100.0, 0.0, 1.0);
            if (leavePressure <= 0.0)
            {
                return;
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c.SocialTargeting is null)
                {
                    continue;
                }

                var towardPartner = c.SocialTargeting.TargetHuman == partner.B;
                var delta = c.Name switch
                {
                    // Dampen approaching the partner; boost approaching anyone else.
                    InviteIntimacy or ReachOut when towardPartner => -leavePressure * 10.0,
                    InviteIntimacy or ReachOut => +leavePressure * 6.0,
                    _ => 0.0
                };

                if (Math.Abs(delta) > 0.001)
                {
                    candidates[i] = c with { Utility = Math.Max(0.0, c.Utility + delta) };
                }
            }
        }

        #endregion IBehaviorModifierEngine
    }
}
