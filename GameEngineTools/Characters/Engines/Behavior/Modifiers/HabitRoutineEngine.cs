// HabitRoutineEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System.Collections.Generic;
    using static ActionNames;

    /// <summary>
    /// Preserves short-horizon behavioral continuity through inertia and novelty shaping.
    /// </summary>
    internal sealed class HabitRoutineEngine : IBehaviorModifierEngine
    {
        #region Static data

        private static readonly HashSet<string> InertiaEligible = new() { Work, Create, ReachOut };
        private static readonly Dictionary<string, ActionCategory> ActionCategories = new()
        {
            { Work, ActionCategory.Productive }, { Create, ActionCategory.Productive }, { ReachOut, ActionCategory.Social }, { InviteIntimacy, ActionCategory.Social },
            { Eat, ActionCategory.Biological }, { Drink, ActionCategory.Biological }, { SelfCare, ActionCategory.Biological }, { Idle, ActionCategory.Rest },
            { MoveToSocial, ActionCategory.Social }, { MoveToPrivate, ActionCategory.Rest }, { MoveToWork, ActionCategory.Productive }, { MoveToRest, ActionCategory.Rest }, { MoveToPublic, ActionCategory.Social },
        };

        #endregion

        #region IBehaviorModifierEngine

        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            if (context.State.CurrentPlan is not { } cp) return;

            var bf = context.HumanContext.Personality.BigFive;

            // B3 — Conscientiousness scales InertiaWeight.
            // High-C characters resist switching away from productive actions more strongly.
            // Formula: C ∈ [0,1] → factor ∈ [0, InertiaWeight].
            var effectiveInertia = context.Config.InertiaWeight * bf.Conscientiousness;

            // B5 — Openness reduces NoveltyPenalty (variety-seeking).
            // High-O characters experience a weaker penalty for switching to a new category.
            // Formula: O=0 → full penalty; O=1.0 → 40% of penalty (60% reduction).
            var effectiveNoveltyPenalty = context.Config.NoveltyPenalty * (1.0 - bf.Openness * 0.60);

            var currentCategory = GetCategory(cp.Name);
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate.Name == cp.Name && InertiaEligible.Contains(cp.Name))
                {
                    candidates[i] = candidate with { Utility = candidate.Utility * (1.0 + effectiveInertia) };
                    continue;
                }
                var candidateCategory = GetCategory(candidate.Name);
                if (candidateCategory != currentCategory && candidateCategory != ActionCategory.Biological)
                    // Biological regulation should be able to break routine more easily than elective behavior.
                    candidates[i] = candidate with { Utility = candidate.Utility * (1.0 - effectiveNoveltyPenalty) };
            }
        }

        #endregion

        #region Helpers

        private static ActionCategory GetCategory(string actionName) => ActionCategories.TryGetValue(actionName, out var cat) ? cat : ActionCategory.Rest;

        #endregion
    }
}
