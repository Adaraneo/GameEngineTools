// ProductionSiteFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Production
{
    using System.Collections.Immutable;

    /// <summary>
    /// Builds the immovable <see cref="WorldObject"/> fixtures that mark a location as a production
    /// or processing site (a field, a mill, a bakery). A site carries a
    /// <see cref="AffordanceType.Production"/> affordance and an <c>ItemKind</c> naming its output;
    /// whether that output has a <see cref="Recipe"/> distinguishes a processing site (mill/bakery)
    /// from a raw-harvest site (field/pasture). Food-economy Tier 1.
    /// </summary>
    public static class ProductionSiteFactory
    {
        /// <summary>
        /// Creates a non-pickable, non-respawning production-site fixture at <paramref name="locationId"/>
        /// whose <see cref="AffordanceType.Production"/> yields <paramref name="outputKind"/>.
        /// </summary>
        public static WorldObject Create(string id, string locationId, PickupItemKind outputKind, string displayName)
            => new()
            {
                Id = id,
                DisplayName = displayName,
                // Ambient: deliberately not a gated category (Food/Drink/Tool/Furniture/Shelter), so a
                // site satisfies only the Produce/Process gates via its Production affordance.
                Category = WorldObjectCategory.Ambient,
                LocationId = locationId,
                IsAvailable = true,
                IsPickable = false,
                Respawns = false,
                WeightGrams = 500_000, // effectively immovable
                ItemKind = outputKind,
                Affordances = ImmutableArray.Create(new WorldObjectAffordance(AffordanceType.Production, 1.0)),
            };
    }
}
