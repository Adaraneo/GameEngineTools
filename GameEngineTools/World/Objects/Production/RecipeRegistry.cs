// RecipeRegistry.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Production
{
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Collections.ObjectModel;
    using System.Linq;
    using GameEngineTools.World.Work;

    /// <summary>
    /// Immutable table of all <see cref="Recipe"/> definitions, read once from the craft tasks of the work catalog (<c>tasks.json</c>). Design data, not runtime
    /// state, so it is not a SQLite table. Food-economy Tier 1.
    /// </summary>
    public static class RecipeRegistry
    {
        #region Public accessors

        /// <summary>Looks up the recipe that produces <paramref name="outputKind"/>, or <c>null</c>.</summary>
        public static Recipe? FindByOutput(PickupItemKind outputKind)
            => Table.TryGetValue(outputKind, out var recipe) ? recipe : null;

        /// <summary>All registered recipes (for the provisioning bridge to consider).</summary>
        public static IEnumerable<Recipe> All => Table.Values;

        #endregion Public accessors

        #region Internal table

        // The recipes are the craft tasks of tasks.json (disk override, else built in): grain to flour to bread and milk to cheese for now.
        private static readonly IReadOnlyDictionary<PickupItemKind, Recipe> Table =
            new ReadOnlyDictionary<PickupItemKind, Recipe>(FromCatalog(WorkCatalogLoader.Load()));

        /// <summary>The recipes of a catalog: its craft tasks, with item names read as <see cref="PickupItemKind"/> names.</summary>
        /// <exception cref="System.FormatException">A task names an item that is not a known item kind.</exception>
        public static Dictionary<PickupItemKind, Recipe> FromCatalog(WorkCatalog catalog)
        {
            PickupItemKind Kind(string taskId, string item)
                => System.Enum.TryParse<PickupItemKind>(item, ignoreCase: false, out var kind)
                    ? kind
                    : throw new System.FormatException($"tasks.json: task '{taskId}': unknown item kind '{item}'.");

            var table = new Dictionary<PickupItemKind, Recipe>();
            foreach (var task in catalog.Tasks.Where(t => t.Kind == TaskKind.Craft))
            {
                var output = Kind(task.Id, task.Output!.Item);
                var recipe = new Recipe(
                    task.Id,
                    task.Name,
                    task.Inputs.Select(i => new RecipeInput(Kind(task.Id, i.Item), i.Quantity)).ToImmutableArray(),
                    output,
                    task.OutputName ?? task.Output.Item,
                    task.DurationMinutes,
                    task.Location!);
                if (!table.TryAdd(output, recipe))
                    throw new System.FormatException($"tasks.json: task '{task.Id}': a second recipe for {output}; the registry holds one recipe per product.");
            }

            return table;
        }

        #endregion Internal table
    }
}
