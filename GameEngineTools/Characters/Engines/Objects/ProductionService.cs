// ProductionService.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Objects.Production;
    using GameEngineTools.World.Utils.Time;
    using GameEngineTools.World.Work;

    /// <summary>
    /// Converts labor time into a produced or processed world object. Executes the transformation
    /// once the behavior pipeline has committed to a <c>Produce</c>/<c>Process</c> action — it does
    /// NOT decide when to produce (that is the provisioning bridge's job). Food-economy Tier 1.
    /// </summary>
    public sealed class ProductionService
    {
        #region Construction

        private readonly IMutableWorldObjectProvider _objectProvider;
        private readonly WorkCatalog _catalog;
        private readonly IReadOnlyDictionary<string, Recipe> _recipes;

        /// <summary>Creates the service over the mutable world-object provider.</summary>
        /// <param name="objectProvider">The world objects.</param>
        /// <param name="catalog">The work catalog: its craft tasks are the recipes and its object types are what is made.</param>
        public ProductionService(IMutableWorldObjectProvider objectProvider, WorkCatalog catalog)
        {
            ArgumentNullException.ThrowIfNull(objectProvider);
            _objectProvider = objectProvider;
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _recipes = RecipeRegistry.FromCatalog(catalog);
        }

        #endregion Construction

        #region Public API

        /// <summary>
        /// Raw-material production (no recipe inputs — e.g. harvesting grain, milking): consumes only
        /// labor time and creates <paramref name="yield"/> fresh output objects in the character's hands (the first is returned).
        /// </summary>
        public WorldObject Produce(IHumanContext ctx, string locationId, string outputTypeId, WDateTime now, int yield = 1)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            ArgumentException.ThrowIfNullOrWhiteSpace(locationId);

            return CreateOutputs(ctx, locationId, outputTypeId, now, yield);
        }

        /// <summary>
        /// Recipe-based processing (e.g. milling, baking): verifies and consumes the recipe inputs
        /// (all-or-nothing), then creates the fresh output object in the character's hands. Returns
        /// <c>null</c> with no side effects when the recipe is unknown or inputs are insufficient. One run of the recipe makes
        /// <paramref name="yield"/> outputs (the first is returned).
        /// </summary>
        public WorldObject? Process(IHumanContext ctx, string locationId, string outputItemId, WDateTime now, int yield = 1)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            ArgumentException.ThrowIfNullOrWhiteSpace(locationId);

            var recipe = _recipes.GetValueOrDefault(outputItemId);
            if (recipe is null)
                return null;

            if (!TryConsumeInputs(ctx, locationId, recipe))
                return null; // insufficient inputs — nothing consumed

            return CreateOutputs(ctx, locationId, recipe.OutputItemId, now, yield);
        }

        #endregion Public API

        #region Private helpers

        private WorldObject CreateOutputs(IHumanContext ctx, string locationId, string outputItemId, WDateTime now, int yield)
        {
            var first = CreateOne(ctx, locationId, outputItemId, now);
            _objectProvider.AddObject(first);
            for (var i = 1; i < yield; i++)
                _objectProvider.AddObject(CreateOne(ctx, locationId, outputItemId, now));
            return first;
        }

        // What is made must be an object type of the catalog; it is made from it (name, category, weight, affordances, spoilage clock).
        private WorldObject CreateOne(IHumanContext ctx, string locationId, string outputItemId, WDateTime now)
        {
            if (_catalog.Type(outputItemId) is null)
                throw new InvalidOperationException($"Cannot make '{outputItemId}': the work catalog has no such object type.");
            return WorkObjects.CreateItem(_catalog, outputItemId, locationId, now, ctx.Id);
        }

        /// <summary>A recipe input lying about must be a free, pickable item: not a production-site fixture and not priced shop stock.</summary>
        private static bool IsLooseIngredient(WorldObject o) => o.IsPickable && o.Price is null;

        /// <summary>
        /// Checks and removes every recipe input from the character's held inventory or the current
        /// location. All-or-nothing: if any input is short, nothing is consumed.
        /// </summary>
        private bool TryConsumeInputs(IHumanContext ctx, string locationId, Recipe recipe)
        {
            var held = _objectProvider.GetHeldBy(ctx.Id).ToList();
            var atLocation = _objectProvider.GetObjectsAt(locationId)
                .Where(o => o.HeldBy is null && o.IsAvailable && IsLooseIngredient(o))
                .ToList();

            var usedIds = new HashSet<string>(StringComparer.Ordinal);
            var toRemove = new List<WorldObject>();

            foreach (var input in recipe.Inputs)
            {
                var pool = held.Concat(atLocation)
                    .Where(o => string.Equals(o.TypeId, input.ItemId, StringComparison.Ordinal) && !usedIds.Contains(o.Id))
                    .Take(input.Quantity)
                    .ToList();

                if (pool.Count < input.Quantity)
                    return false; // short on this input — abort before removing anything

                foreach (var o in pool) usedIds.Add(o.Id);
                toRemove.AddRange(pool);
            }

            foreach (var o in toRemove)
                _objectProvider.RemoveObject(o.LocationId, o.Id);

            return true;
        }

        #endregion Private helpers
    }
}
