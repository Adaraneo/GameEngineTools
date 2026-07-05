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

    /// <summary>
    /// Converts labor time into a produced or processed world object. Executes the transformation
    /// once the behavior pipeline has committed to a <c>Produce</c>/<c>Process</c> action — it does
    /// NOT decide when to produce (that is the provisioning bridge's job). Food-economy Tier 1.
    /// </summary>
    public sealed class ProductionService
    {
        #region Construction

        private readonly IMutableWorldObjectProvider _objectProvider;

        /// <summary>Creates the service over the mutable world-object provider.</summary>
        public ProductionService(IMutableWorldObjectProvider objectProvider)
        {
            ArgumentNullException.ThrowIfNull(objectProvider);
            _objectProvider = objectProvider;
        }

        #endregion Construction

        #region Public API

        /// <summary>
        /// Raw-material production (no recipe inputs — e.g. harvesting grain, milking): consumes only
        /// labor time and creates one fresh output object in the character's hands.
        /// </summary>
        public WorldObject Produce(IHumanContext ctx, string locationId, PickupItemKind outputKind, WDateTime now)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            ArgumentException.ThrowIfNullOrWhiteSpace(locationId);

            var obj = FoodItemCatalog.Create(outputKind, locationId, now, heldBy: ctx.Id);
            _objectProvider.AddObject(obj);
            return obj;
        }

        /// <summary>
        /// Recipe-based processing (e.g. milling, baking): verifies and consumes the recipe inputs
        /// (all-or-nothing), then creates the fresh output object in the character's hands. Returns
        /// <c>null</c> with no side effects when the recipe is unknown or inputs are insufficient.
        /// </summary>
        public WorldObject? Process(IHumanContext ctx, string locationId, PickupItemKind outputKind, WDateTime now)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            ArgumentException.ThrowIfNullOrWhiteSpace(locationId);

            var recipe = RecipeRegistry.FindByOutput(outputKind);
            if (recipe is null)
                return null;

            if (!TryConsumeInputs(ctx, locationId, recipe))
                return null; // insufficient inputs — nothing consumed

            var obj = FoodItemCatalog.Create(outputKind, locationId, now, heldBy: ctx.Id);
            _objectProvider.AddObject(obj);
            return obj;
        }

        #endregion Public API

        #region Private helpers

        /// <summary>
        /// Checks and removes every recipe input from the character's held inventory or the current
        /// location. All-or-nothing: if any input is short, nothing is consumed.
        /// </summary>
        private bool TryConsumeInputs(IHumanContext ctx, string locationId, Recipe recipe)
        {
            var held = _objectProvider.GetHeldBy(ctx.Id).ToList();
            var atLocation = _objectProvider.GetObjectsAt(locationId)
                .Where(o => o.HeldBy is null && o.IsAvailable)
                .ToList();

            var usedIds = new HashSet<string>(StringComparer.Ordinal);
            var toRemove = new List<WorldObject>();

            foreach (var input in recipe.Inputs)
            {
                var pool = held.Concat(atLocation)
                    .Where(o => o.ItemKind == input.Kind && !usedIds.Contains(o.Id))
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
