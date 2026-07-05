// FoodItemCatalog.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Production
{
    using System;
    using System.Collections.Immutable;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Turns a produced/harvested <see cref="PickupItemKind"/> into a fresh <see cref="WorldObject"/>.
    /// Centralises category, display name, affordances and weight for the Tier 1 food chain so both
    /// raw production and recipe output build items consistently. Food-economy Tier 1.
    /// </summary>
    public static class FoodItemCatalog
    {
        /// <summary>
        /// Builds a fresh, undecayed object of <paramref name="kind"/> at <paramref name="locationId"/>,
        /// optionally already in a character's hands (<paramref name="heldBy"/>).
        /// </summary>
        public static WorldObject Create(
            PickupItemKind kind, string locationId, WDateTime producedAt, HumanId? heldBy)
        {
            var (category, name, affordances, weight) = Profile(kind);
            return new WorldObject
            {
                Id = $"{kind.ToString().ToLowerInvariant()}_{Guid.NewGuid():N}",
                DisplayName = name,
                Category = category,
                LocationId = locationId,
                IsAvailable = true,
                IsPickable = true,
                WeightGrams = weight,
                ItemKind = kind,
                HeldBy = heldBy,
                Affordances = affordances,
                ProducedAt = producedAt,
            };
        }

        private static (WorldObjectCategory Category, string Name, ImmutableArray<WorldObjectAffordance> Affordances, int Weight)
            Profile(PickupItemKind kind) => kind switch
        {
            // Raw materials — pickable inputs, not directly satisfying hunger.
            PickupItemKind.Grain => (WorldObjectCategory.Food, "Obilí", Own(0.25), 200),
            PickupItemKind.Flour => (WorldObjectCategory.Food, "Mouka", Own(0.25), 250),
            PickupItemKind.Milk  => (WorldObjectCategory.Drink, "Mléko", With(AffordanceType.Thirst, 0.5, 0.3), 400),

            // Processed, edible foods.
            PickupItemKind.Bread  => (WorldObjectCategory.Food, "Čerstvý chléb", With(AffordanceType.Hunger, 0.6, 0.4), 300),
            PickupItemKind.Cheese => (WorldObjectCategory.Food, "Čerstvý sýr", With(AffordanceType.Hunger, 0.55, 0.4), 250),

            // Fallback: a generic edible food item.
            _ => (WorldObjectCategory.Food, kind.ToString(), With(AffordanceType.Hunger, 0.5, 0.3), 250),
        };

        private static ImmutableArray<WorldObjectAffordance> Own(double ownership)
            => ImmutableArray.Create(new WorldObjectAffordance(AffordanceType.Ownership, ownership));

        private static ImmutableArray<WorldObjectAffordance> With(AffordanceType type, double satisfaction, double ownership)
            => ImmutableArray.Create(
                new WorldObjectAffordance(type, satisfaction),
                new WorldObjectAffordance(AffordanceType.Ownership, ownership));
    }
}
