// RecipeRegistry.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Production
{
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using GameEngineTools.World.Work;

    /// <summary>
    /// The <see cref="Recipe"/> table of one <see cref="WorkCatalog"/>: its craft tasks (<c>tasks.json</c>), keyed by product. Design data, not runtime state. Food-economy Tier 1.
    /// </summary>
    public sealed class RecipeRegistry
    {
        private readonly List<Recipe> _recipes = new();

        /// <summary>Reads the recipes of a catalog: its craft tasks. An item is named by the id of an object type of the catalog.</summary>
    
        public RecipeRegistry(WorkCatalog catalog)
        {
            System.ArgumentNullException.ThrowIfNull(catalog);
            string Item(string taskId, string item)
                => catalog.Type(item) is not null
                    ? item
                    : throw new System.FormatException($"tasks.json: task '{taskId}': '{item}' is not an object type of objecttypes.json.");

            foreach (var task in catalog.Tasks.Where(t => t.Kind == TaskKind.Craft))
            {
                var output = Item(task.Id, task.Output!.Item);
                var recipe = new Recipe(
                    task.Id,
                    task.Name,
                    task.Inputs.Select(i => new RecipeInput(Item(task.Id, i.Item), i.Quantity)).ToImmutableArray(),
                    output,
                    task.OutputName ?? catalog.Type(output)?.Name ?? output,
                    task.DurationMinutes,
                    task.Station!);
                _recipes.Add(recipe);
            }
        }

        /// <summary>The first recipe (in the order of the file) that produces the object type with this id, or <c>null</c>; a product may have several recipes, see <see cref="ForOutput"/>.</summary>
        public Recipe? FindByOutput(string? itemId) => ForOutput(itemId).FirstOrDefault();

        /// <summary>All recipes that produce the object type with this id, in the order of the file.</summary>
        public IEnumerable<Recipe> ForOutput(string? itemId) => _recipes.Where(r => string.Equals(r.OutputItemId, itemId, System.StringComparison.Ordinal));

        /// <summary>The recipe of the craft task with this id, or <c>null</c>.</summary>
        public Recipe? ById(string? taskId) => _recipes.FirstOrDefault(r => string.Equals(r.Id, taskId, System.StringComparison.Ordinal));

        /// <summary>The recipes done at a workstation type, in the order of the file.</summary>
        public IEnumerable<Recipe> ForStation(string? stationTypeId)
            => _recipes.Where(r => string.Equals(r.StationTypeId, stationTypeId, System.StringComparison.Ordinal));

        /// <summary>All recipes (for the provisioning bridge to consider).</summary>
        public IEnumerable<Recipe> All => _recipes;
    }
}
