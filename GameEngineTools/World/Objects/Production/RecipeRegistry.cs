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
            => outputKind == PickupItemKind.None ? null : FindByOutput(outputKind.ToString());

        /// <summary>Looks up the recipe that produces the item with this id (a legacy kind name or a catalog type id), or <c>null</c>.</summary>
        public static Recipe? FindByOutput(string? itemId)
            => itemId is not null && Table.TryGetValue(itemId, out var recipe) ? recipe : null;

        /// <summary>All registered recipes (for the provisioning bridge to consider).</summary>
        public static IEnumerable<Recipe> All => Table.Values;

        #endregion Public accessors

        #region Internal table

        // The recipes are the craft tasks of tasks.json (disk override, else built in): grain to flour to bread and milk to cheese for now.
        private static readonly IReadOnlyDictionary<string, Recipe> Table =
            new ReadOnlyDictionary<string, Recipe>(FromCatalog(WorkCatalogLoader.Load()));

        /// <summary>
        /// The recipes of a catalog: its craft tasks. An item is named by a legacy <see cref="PickupItemKind"/> name (Grain, Flour, Bread ...) or by the id of an object type of the catalog.
        /// </summary>
        /// <exception cref="System.FormatException">A task names an item that is neither, or two tasks make the same product.</exception>
        public static Dictionary<string, Recipe> FromCatalog(WorkCatalog catalog)
        {
            string Item(string taskId, string item)
                => (System.Enum.TryParse<PickupItemKind>(item, ignoreCase: false, out var kind) && kind != PickupItemKind.None && System.Enum.IsDefined(kind)) || catalog.Type(item) is not null
                    ? item
                    : throw new System.FormatException($"tasks.json: task '{taskId}': '{item}' is neither an item kind nor an object type.");

            var table = new Dictionary<string, Recipe>(System.StringComparer.Ordinal);
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
                if (!table.TryAdd(output, recipe))
                    throw new System.FormatException($"tasks.json: task '{task.Id}': a second recipe for {output}; the registry holds one recipe per product.");
            }

            return table;
        }

        #endregion Internal table
    }
}
