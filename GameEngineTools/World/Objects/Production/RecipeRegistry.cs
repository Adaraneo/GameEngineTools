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
        private readonly Dictionary<string, Recipe> _table;

        /// <summary>Reads the recipes of a catalog: its craft tasks. An item is named by the id of an object type of the catalog.</summary>
        /// <exception cref="System.FormatException">A task names an item that is not an object type, or two tasks make the same product.</exception>
        public RecipeRegistry(WorkCatalog catalog)
        {
            System.ArgumentNullException.ThrowIfNull(catalog);
            string Item(string taskId, string item)
                => catalog.Type(item) is not null
                    ? item
                    : throw new System.FormatException($"tasks.json: task '{taskId}': '{item}' is not an object type of objecttypes.json.");

            _table = new Dictionary<string, Recipe>(System.StringComparer.Ordinal);
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
                    task.Location!);
                if (!_table.TryAdd(output, recipe))
                    throw new System.FormatException($"tasks.json: task '{task.Id}': a second recipe for {output}; the registry holds one recipe per product.");
            }
        }

        /// <summary>Looks up the recipe that produces the object type with this id, or <c>null</c>.</summary>
        public Recipe? FindByOutput(string? itemId)
            => itemId is not null && _table.TryGetValue(itemId, out var recipe) ? recipe : null;

        /// <summary>All recipes (for the provisioning bridge to consider).</summary>
        public IEnumerable<Recipe> All => _table.Values;
    }
}
