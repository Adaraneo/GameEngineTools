// SiteBuilding.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Production
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>What a settlement can still build of the grain-flour-bread chain, and the standard sites a build makes.</summary>
    public static class SiteBuilding
    {
        private static readonly PickupItemKind[] ChainOrder = { PickupItemKind.Grain, PickupItemKind.Flour, PickupItemKind.Bread };

        /// <summary>
        /// Outputs one action makes at a standard site; WorldGen and a build both use these. A loaf then costs about 1.3 labor hours
        /// instead of 3.25 - own estimate, aimed at roughly 5 hours of food work per person-day.
        /// </summary>
        public static double StandardYield(PickupItemKind kind) => kind switch
        {
            PickupItemKind.Grain => 3.0,
            PickupItemKind.Flour => 2.0,
            PickupItemKind.Bread => 2.0,
            _ => 1.0,
        };

        /// <summary>Display name of the standard site for an output kind.</summary>
        public static string SiteName(PickupItemKind kind) => kind switch
        {
            PickupItemKind.Grain => "Obilné pole",
            PickupItemKind.Flour => "Mlýn",
            PickupItemKind.Bread => "Pekárna",
            _ => "Dílna",
        };

        /// <summary>A settlement has something to eat or drink lying about, or already works the chain; a house or a road does not.</summary>
        public static bool IsSettlement(IEnumerable<WorldObject> atLocation)
            => atLocation.Any(o => o.Price is null && (o.Category is WorldObjectCategory.Food or WorldObjectCategory.Drink || IsSite(o)));

        /// <summary>The first link of the chain (field, mill, bakery) that the location lacks, or <c>null</c> when it has them all.</summary>
        public static PickupItemKind? NextSite(IEnumerable<WorldObject> atLocation)
        {
            var present = atLocation.Where(IsSite).Select(o => o.ItemKind).ToHashSet();
            foreach (var kind in ChainOrder)
            {
                if (!present.Contains(kind))
                    return kind;
            }

            return null;
        }

        /// <summary>A new standard site of <paramref name="kind"/> at <paramref name="locationId"/>.</summary>
        public static WorldObject Create(PickupItemKind kind, string locationId)
            => ProductionSiteFactory.Create($"{locationId}_{kind.ToString().ToLowerInvariant()}_built_{Guid.NewGuid():N}", locationId, kind, SiteName(kind), StandardYield(kind));

        private static bool IsSite(WorldObject o) => o.Affordances.Any(a => a.Type == AffordanceType.Production);
    }
}
