// SupplyChain.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Production
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.World.Work;

    /// <summary>Bounded backward chain over craft tasks: which craft step can be done here and now to get a missing tool or input (only what is at the place counts).</summary>
    public sealed class SupplyChain
    {
        /// <summary>How many craft steps deep the search goes: the item, and what its inputs are made from. A design pick; deeper chains are for a real planner.</summary>
        public const int MaxDepth = 2;

        private readonly WorkCatalog _catalog;
        private readonly RecipeRegistry _recipes;

        /// <summary>Creates the chain over a catalog and its recipes.</summary>
        public SupplyChain(WorkCatalog catalog, RecipeRegistry recipes)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
        }

        /// <summary>
        /// All-or-nothing check that held plus loose co-located items cover every input of a recipe, counting each item toward one input line only (mirrors <c>ProductionService</c>).
        /// </summary>
        public static bool CanSatisfy(Recipe recipe, IReadOnlyList<WorldObject> available, IReadOnlyList<WorldObject>? held)
        {
            var pool = (held ?? Array.Empty<WorldObject>()).Concat(available.Where(o => o.IsPickable && o.Price is null));
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in recipe.Inputs)
            {
                var matched = pool.Where(o => string.Equals(o.TypeId, input.ItemId, StringComparison.Ordinal) && !used.Contains(o.Id)).Take(input.Quantity).ToList();
                if (matched.Count < input.Quantity)
                {
                    return false;
                }
                foreach (var o in matched)
                {
                    used.Add(o.Id);
                }
            }

            return true;
        }

        /// <summary>The craft step to take here to get a tool of this class: a recipe making such a tool, or (one level down) one making what that recipe lacks. <c>null</c> when none is possible here.</summary>
        public Recipe? NextStepForTool(string toolClass, IReadOnlyList<WorldObject> available, IReadOnlyList<WorldObject>? held)
            => NextStep(item => _catalog.ToolQuality(item, toolClass) > 0.0, available, held, MaxDepth);

        /// <summary>The craft step to take here to get the missing inputs of <paramref name="goal"/>: a recipe making one of them, doable now. <c>null</c> when none is.</summary>
        public Recipe? NextStepForInputs(Recipe goal, IReadOnlyList<WorldObject> available, IReadOnlyList<WorldObject>? held)
        {
            foreach (var input in goal.Inputs)
            {
                if (NextStep(item => string.Equals(item, input.ItemId, StringComparison.Ordinal), available, held, MaxDepth) is { } step)
                {
                    return step;
                }
            }

            return null;
        }

        private Recipe? NextStep(Func<string, bool> wanted, IReadOnlyList<WorldObject> available, IReadOnlyList<WorldObject>? held, int depth)
        {
            foreach (var recipe in _recipes.All.Where(r => wanted(r.OutputItemId)))
            {
                if (!available.Any(o => ProductionSites.IsStation(_catalog, o) && string.Equals(o.TypeId, recipe.StationTypeId, StringComparison.Ordinal)))
                {
                    continue; // the workstation is not here
                }

                if (CanSatisfy(recipe, available, held))
                {
                    return recipe;
                }

                if (depth <= 1)
                {
                    continue;
                }
                foreach (var input in recipe.Inputs)
                {
                    if (NextStep(item => string.Equals(item, input.ItemId, StringComparison.Ordinal), available, held, depth - 1) is { } deeper)
                    {
                        return deeper;
                    }
                }
            }

            return null;
        }
    }
}
