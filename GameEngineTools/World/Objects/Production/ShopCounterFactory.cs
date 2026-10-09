// ShopCounterFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Production
{
    /// <summary>Shop identity that outlives its stock: a counter keeps trading an item kind after the last unit is sold.</summary>
    public static class ShopCounterFactory
    {
        /// <summary>Creates an immovable, unpriced counter at which <paramref name="shopId"/> trades <paramref name="kind"/>.</summary>
        public static WorldObject Create(string id, string locationId, string shopId, PickupItemKind kind, string displayName)
            => new()
            {
                Id = id,
                DisplayName = displayName,
                Category = WorldObjectCategory.Ambient,
                LocationId = locationId,
                IsAvailable = true,
                IsPickable = false,
                Respawns = false,
                WeightGrams = 500_000,
                ItemKind = kind,
                ShopId = shopId,
            };

        /// <summary>True for a priced unit on a shop's shelf (a counter carries a shop id but no price).</summary>
        public static bool IsStock(WorldObject o) => o.ShopId is not null && o.Price is not null;
    }
}
