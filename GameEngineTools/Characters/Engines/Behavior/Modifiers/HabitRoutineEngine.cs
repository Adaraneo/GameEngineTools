// HabitRoutineEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System.Collections.Generic;
    using static ActionNames;

    internal sealed class HabitRoutineEngine : IBehaviorModifierEngine
    {
        private static readonly HashSet<string> InertiaEligible = new() { Work, Create, ReachOut };
        private static readonly Dictionary<string, ActionCategory> ActionCategories = new()
        {
            { Work, ActionCategory.Productive }, { Create, ActionCategory.Productive }, { ReachOut, ActionCategory.Social }, { InviteIntimacy, ActionCategory.Social },
            { Eat, ActionCategory.Biological }, { Drink, ActionCategory.Biological }, { SelfCare, ActionCategory.Biological }, { Idle, ActionCategory.Rest },
            { MoveToSocial, ActionCategory.Social }, { MoveToPrivate, ActionCategory.Rest }, { MoveToWork, ActionCategory.Productive }, { MoveToRest, ActionCategory.Rest }, { MoveToPublic, ActionCategory.Social },
        };

        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            if (context.State.CurrentPlan is not { } cp) return;
            var currentCategory = GetCategory(cp.Name);
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate.Name == cp.Name && InertiaEligible.Contains(cp.Name))
                {
                    candidates[i] = candidate with { Utility = candidate.Utility * (1.0 + context.Config.InertiaWeight) };
                    continue;
                }
                var candidateCategory = GetCategory(candidate.Name);
                if (candidateCategory != currentCategory && candidateCategory != ActionCategory.Biological)
                    candidates[i] = candidate with { Utility = candidate.Utility * (1.0 - context.Config.NoveltyPenalty) };
            }
        }

        private static ActionCategory GetCategory(string actionName) => ActionCategories.TryGetValue(actionName, out var cat) ? cat : ActionCategory.Rest;
    }
}
