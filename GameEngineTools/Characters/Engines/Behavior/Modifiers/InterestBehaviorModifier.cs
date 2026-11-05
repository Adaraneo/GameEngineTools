// InterestBehaviorModifier.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Engines.Interests;

    /// <summary>Nudges the utility of actions that exercise a RIASEC dimension toward the character's relative interest in it.</summary>
    internal sealed class InterestBehaviorModifier : IBehaviorModifierEngine
    {
        // Utility points per unit of relative interest (≈ ±0.2 for a pronounced profile → ±4); interest scores alone correlate only ≈ .16 with performance and persistence (Nye 2017).
        private const double UtilityPerRelativeInterest = 20.0;

        /// <inheritdoc/>
        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var snapshot = context.HumanContext.Snapshot;
            var interests = snapshot.Interests?.Current;
            if (interests is null) return;

            var occupation = snapshot.Schedule?.Occupation;
            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                var dim = InterestActionMap.DimensionOf(c.Name, occupation);
                if (dim < 0) continue;

                var delta = InterestActionMap.Relative(interests, dim) * UtilityPerRelativeInterest;
                candidates[i] = c with { Utility = Math.Max(0.0, c.Utility + delta) };
            }
        }
    }
}
