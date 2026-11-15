// SiteBuilding.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Production
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.World.Work;

    /// <summary>What a settlement can still build of the grain-flour-bread chain, and the standard sites a build makes.</summary>
    public static class SiteBuilding
    {
        private static readonly string[] ChainOrder = { "grain", "flour", "bread" };

        /// <summary>
        /// Outputs one action makes at a standard field; WorldGen and a build both use it. A loaf then costs about 1.3 labor hours
        /// instead of 3.25 - own estimate, aimed at roughly 5 hours of food work per person-day. The yield of a workstation is the Production affordance of its object type.
        /// </summary>
        public const double StandardFieldYield = 3.0;

        /// <summary>A settlement has something to eat or drink lying about, or already works the chain; a house or a road does not.</summary>
        public static bool IsSettlement(IEnumerable<WorldObject> atLocation)
            => atLocation.Any(o => o.Price is null && (o.Category is WorldObjectCategory.Food or WorldObjectCategory.Drink || IsSite(o)));

        /// <summary>The first link of the chain (field, mill, bakery) that the location lacks, or <c>null</c> when it has them all. A link is present as a raw site of that product or as the workstation of the recipe making it.</summary>
        public static string? NextSite(IEnumerable<WorldObject> atLocation, RecipeRegistry recipes)
        {
            var sites = atLocation.Where(IsSite).ToList();
            foreach (var kind in ChainOrder)
            {
                var station = recipes.FindByOutput(kind)?.StationTypeId;
                if (!sites.Any(o => station is null ? o.ItemTypeId == kind : string.Equals(o.TypeId, station, StringComparison.Ordinal)))
                    return kind;
            }

            return null;
        }

        /// <summary>A new standard site that makes <paramref name="kind"/> at <paramref name="locationId"/>: a field for a raw product, else the workstation of the recipe making it.</summary>
        public static WorldObject Create(string kind, string locationId, WorkCatalog catalog, RecipeRegistry recipes)
            => recipes.FindByOutput(kind) is { } recipe
                ? WorkObjects.Spawn(catalog, recipe.StationTypeId, $"{locationId}_{kind}_built_{Guid.NewGuid():N}", locationId)
                : ProductionSiteFactory.Create($"{locationId}_{kind}_built_{Guid.NewGuid():N}", locationId, kind, "Obilné pole", StandardFieldYield);

        private static bool IsSite(WorldObject o) => o.Affordances.Any(a => a.Type == AffordanceType.Production);
    }
}
