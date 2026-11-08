// GrainStores.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Crops
{
    using System;
    using System.Collections.Immutable;
    using System.Linq;

    /// <summary>The two stocks of a farming settlement: the seed bin kept for next year's sowing and the food store people eat from.</summary>
    public static class GrainStores
    {
        /// <summary>Id of the seed bin at a location.</summary>
        public static string SeedBinId(string locationId) => $"{locationId}_seed_bin";

        /// <summary>Id of the food store at a location.</summary>
        public static string FoodStoreId(string locationId) => $"{locationId}_grain_store";

        /// <summary>True for the seed bin: a finite grain stock that is not food.</summary>
        public static bool IsSeedBin(WorldObject o) => o.IsStock && o.ItemKind == PickupItemKind.Grain && o.Category == WorldObjectCategory.Ambient;

        /// <summary>True for the food store: a finite grain stock people can eat from.</summary>
        public static bool IsFoodStore(WorldObject o) => o.IsStock && o.ItemKind == PickupItemKind.Grain && o.Category == WorldObjectCategory.Food;

        /// <summary>A seed bin holding <paramref name="units"/>.</summary>
        public static WorldObject CreateSeedBin(string locationId, int units)
            => new()
            {
                Id = SeedBinId(locationId),
                DisplayName = "Sýpka - osivo",
                Category = WorldObjectCategory.Ambient,
                LocationId = locationId,
                ItemKind = PickupItemKind.Grain,
                IsPickable = false,
                IsStock = true,
                Quantity = Math.Max(1, units),
                WeightGrams = 500_000,
            };

        /// <summary>A food store holding <paramref name="units"/>; eaten as porridge, one unit a meal.</summary>
        public static WorldObject CreateFoodStore(string locationId, int units)
            => new()
            {
                Id = FoodStoreId(locationId),
                DisplayName = "Sýpka - zásoba obilí",
                Category = WorldObjectCategory.Food,
                LocationId = locationId,
                ItemKind = PickupItemKind.Grain,
                IsPickable = false,
                IsStock = true,
                Quantity = Math.Max(1, units),
                WeightGrams = 500_000,
                Affordances = ImmutableArray.Create(new WorldObjectAffordance(AffordanceType.Hunger, PorridgeSatisfaction)),
            };

        /// <summary>How well grain porridge answers hunger on the object scale (bread is 0.6); my pick.</summary>
        public const double PorridgeSatisfaction = 0.4;

        /// <summary>
        /// Puts threshed grain into the settlement's stores: first the seed bin up to what the fields sown in one year need, the rest into
        /// the food store. Creates a store that does not exist yet.
        /// </summary>
        public static void Deposit(IMutableWorldObjectProvider provider, string locationId, int units, CropConfig cfg)
        {
            if (units <= 0)
                return;

            var here = provider.GetAllObjectsAt(locationId).Where(o => o.HeldBy is null).ToList();
            var seedBin = here.FirstOrDefault(IsSeedBin);
            var fields = here.Where(o => o.Field is not null).ToList();
            var croppedPerYear = Math.Max(1, (int)Math.Round(fields.Count / (1.0 + cfg.FallowYears)));
            var seedWanted = fields.OrderByDescending(o => o.Field!.AreaHectares).Take(croppedPerYear).Sum(o => CropMath.SeedUnits(o.Field!.AreaHectares, cfg));
            var toSeed = Math.Clamp(seedWanted - (seedBin?.Quantity ?? 0), 0, units);
            var toFood = units - toSeed;

            if (toSeed > 0)
                Put(provider, seedBin ?? CreateSeedBin(locationId, 0) with { Quantity = 0 }, toSeed, seedBin is null);
            if (toFood > 0)
            {
                var food = here.FirstOrDefault(IsFoodStore);
                Put(provider, food ?? CreateFoodStore(locationId, 0) with { Quantity = 0 }, toFood, food is null);
            }
        }

        /// <summary>Takes up to <paramref name="units"/> from a stock, removing it when emptied; returns what was taken.</summary>
        public static int Take(IMutableWorldObjectProvider provider, WorldObject stock, int units)
        {
            var taken = Math.Min(Math.Max(0, units), stock.Quantity);
            if (taken <= 0)
                return 0;

            provider.RemoveObject(stock.LocationId, stock.Id);
            if (stock.Quantity - taken > 0)
                provider.AddObject(stock with { Quantity = stock.Quantity - taken });
            return taken;
        }

        private static void Put(IMutableWorldObjectProvider provider, WorldObject stock, int units, bool isNew)
        {
            if (!isNew)
                provider.RemoveObject(stock.LocationId, stock.Id);
            provider.AddObject(stock with { Quantity = stock.Quantity + units });
        }
    }
}
