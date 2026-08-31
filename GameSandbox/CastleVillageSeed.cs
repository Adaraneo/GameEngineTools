// CastleVillageSeed.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Engines.Physiology;
using GameEngineTools.World.Location;
using GameEngineTools.World.Objects;

namespace GameSandbox;

/// <summary>
/// Faithful C# port of the Castle/Village/Forest/Wilds content that used to live in the shared
/// <c>seed_data.sql</c> — 19 locations, their 34 connections, and the ~60 food/furniture
/// <see cref="WorldObject"/>s that were tied to them. The default seed no longer ships any
/// locations (worlds start empty, authored per-caller — see <c>WorldDatabaseSeeder.Initialize</c>),
/// so GameSandbox now authors this content itself, the same way it already self-authors
/// <c>village_house_01..25</c>/<c>cemetery</c> in <c>Program.cs</c>. Same ids, coordinates,
/// capacities, affordances and nutrition values as the removed SQL — not new content.
/// </summary>
public static class CastleVillageSeed
{
    /// <summary>Adds the 19 Castle/Village/Forest/Wilds locations and their 34 connections.
    /// Call BEFORE any code (including <see cref="SeedObjects"/>) references these location ids.</summary>
    public static void SeedLocations(WorldMap worldMap, ILocationService locationService)
    {
        AddLocations(worldMap, locationService);
        AddConnections(worldMap);
    }

    /// <summary>Adds the food/drink/furniture/tool/light/ambient objects tied to those locations.
    /// Call AFTER <see cref="SeedLocations"/> and after <see cref="IWorldObjectProvider"/> is available.</summary>
    public static void SeedObjects(IWorldObjectProvider objectProvider)
    {
        AddFoodObjects(objectProvider);
        AddFurnitureObjects(objectProvider);
    }

    #region Locations

    private static void AddLocations(WorldMap worldMap, ILocationService locationService)
    {
        void L(string id, string name, LocationType type, string region, double baseNoise, double noisePerPerson,
            int capacity, bool allowsPrivacy, TerrainType terrain, double dangerLevel, bool allowsPickup,
            string? normId, double x, double y)
            => worldMap.AddLocation(
                new LocationDescriptor(id, name, baseNoise, noisePerPerson, capacity, allowsPrivacy, type, terrain,
                    dangerLevel, allowsPickup, normId, x, y),
                region, locationService);

        L("castle_hall", "Castle Hall", LocationType.Social, "Castle", 0.20, 0.05, 20, false, TerrainType.Indoor, 0.0, true, null, -127.0, 8.5);
        L("library", "Library", LocationType.Private, "Castle", 0.05, 0.02, 5, true, TerrainType.Indoor, 0.0, true, null, -83.0, -17.6);
        L("courtyard", "Courtyard", LocationType.Public, "Castle", 0.30, 0.04, 30, false, TerrainType.Courtyard, 0.0, true, null, -122.3, -32.2);
        L("throne_room", "Throne Room", LocationType.Social, "Castle", 0.10, 0.03, 15, false, TerrainType.Indoor, 0.0, true, null, -152.9, 28.4);
        L("stables", "Stables", LocationType.Work, "Castle", 0.40, 0.06, 10, false, TerrainType.Indoor, 0.0, true, null, -138.8, -90.0);
        L("dungeon_entrance", "Dungeon Entrance", LocationType.Public, "Castle", 0.05, 0.02, 10, false, TerrainType.Indoor, 0.4, false, null, -191.8, -38.4);
        L("dungeon_cell", "Dungeon Cell", LocationType.Private, "Castle", 0.02, 0.01, 2, true, TerrainType.Indoor, 0.6, true, null, -201.0, -69.4);
        L("crypt", "Ancient Crypt", LocationType.Private, "Castle", 0.01, 0.01, 4, true, TerrainType.Indoor, 0.7, false, null, -211.6, 7.9);
        L("tavern", "Tavern", LocationType.Social, "Village", 0.50, 0.08, 25, false, TerrainType.Indoor, 0.0, true, null, 82.8, 189.1);
        L("market_square", "Market Square", LocationType.Public, "Village", 0.60, 0.07, 50, false, TerrainType.Courtyard, 0.0, true, null, 155.8, 154.7);
        L("blacksmith", "Blacksmith", LocationType.Work, "Village", 0.70, 0.05, 8, false, TerrainType.Indoor, 0.0, true, null, 115.8, 145.0);
        L("inn_room", "Inn Room", LocationType.Rest, "Village", 0.05, 0.03, 3, true, TerrainType.Indoor, 0.0, true, null, 87.1, 214.0);
        L("chapel", "Chapel", LocationType.Private, "Village", 0.05, 0.01, 20, false, TerrainType.Indoor, 0.0, true, "norm_funeral", 60.6, 123.8);
        L("herb_garden", "Herb Garden", LocationType.Work, "Village", 0.05, 0.03, 8, false, TerrainType.Courtyard, 0.0, true, null, 36.8, 176.3);
        L("abandoned_mill", "Abandoned Mill", LocationType.Work, "Village", 0.05, 0.03, 6, false, TerrainType.Indoor, 0.2, true, null, -25.9, 230.1);
        L("forest", "Forest", LocationType.Public, "Forest", 0.05, 0.06, 1000, true, TerrainType.Forest, 0.1, true, null, 68.3, -210.9);
        L("forest_clearing", "Forest Clearing", LocationType.Public, "Wilds", 0.10, 0.05, 20, false, TerrainType.Forest, 0.2, true, null, 194.1, -292.8);
        L("river_crossing", "River Crossing", LocationType.Public, "Wilds", 0.15, 0.05, 10, false, TerrainType.Water, 0.3, false, null, 44.1, -426.8);
        L("mountain_pass", "Mountain Pass", LocationType.Public, "Wilds", 0.05, 0.02, 8, false, TerrainType.Mountain, 0.5, false, null, 408.8, -99.9);
    }

    #endregion Locations

    #region Connections

    private static void AddConnections(WorldMap worldMap)
    {
        void C(string a, string b, double distanceMeters)
        {
            worldMap.AddConnection(a, b, distanceMeters);
            worldMap.AddConnection(b, a, distanceMeters);
        }

        C("castle_hall", "library", 50.0);
        C("castle_hall", "courtyard", 40.0);
        C("castle_hall", "throne_room", 30.0);
        C("courtyard", "stables", 60.0);
        C("castle_hall", "dungeon_entrance", 80.0);
        C("dungeon_entrance", "dungeon_cell", 30.0);
        C("dungeon_entrance", "crypt", 50.0);
        C("courtyard", "tavern", 300.0);
        C("tavern", "market_square", 80.0);
        C("market_square", "blacksmith", 40.0);
        C("tavern", "inn_room", 20.0);
        C("market_square", "chapel", 100.0);
        C("market_square", "herb_garden", 120.0);
        C("market_square", "abandoned_mill", 200.0);
        C("tavern", "forest", 400.0);
        C("forest", "forest_clearing", 150.0);
        C("forest_clearing", "river_crossing", 200.0);
        C("river_crossing", "mountain_pass", 500.0);
    }

    #endregion Connections

    #region Food & drink objects

    private static void AddFoodObjects(IWorldObjectProvider objectProvider)
    {
        void Food(string id, string name, string locationId, double heat, double noise, int weight, int respawnMinutes,
            double hungerSatisfaction, double? calorie, double? protein, double? iron, double? vitaminD, double? hydration,
            double? heme = null, double? vitaminC = null)
            => objectProvider.AddObject(new WorldObject
            {
                Category = WorldObjectCategory.Food,
                Id = id,
                DisplayName = name,
                LocationId = locationId,
                HeatSignature = heat,
                AmbientNoise = noise,
                IsAvailable = true,
                IsPickable = true,
                BlocksLineOfSight = false,
                ItemKind = PickupItemKind.Food,
                Respawns = true,
                RespawnMinutes = respawnMinutes,
                WeightGrams = weight,
                Affordances = [new WorldObjectAffordance(AffordanceType.Hunger, hungerSatisfaction)],
                NutritionalProfile = new NutritionalProfile(calorie, protein, iron, vitaminD, hydration, heme, vitaminC),
            });

        void Drink(string id, string name, string locationId, double heat, double noise, int weight, int respawnMinutes,
            double thirstSatisfaction, double? calorie, double? protein, double? iron, double? vitaminD, double? hydration,
            bool pickable = true)
            => objectProvider.AddObject(new WorldObject
            {
                Category = WorldObjectCategory.Drink,
                Id = id,
                DisplayName = name,
                LocationId = locationId,
                HeatSignature = heat,
                AmbientNoise = noise,
                IsAvailable = true,
                IsPickable = pickable,
                BlocksLineOfSight = false,
                ItemKind = PickupItemKind.Drink,
                Respawns = true,
                RespawnMinutes = respawnMinutes,
                WeightGrams = weight,
                Affordances = [new WorldObjectAffordance(AffordanceType.Thirst, thirstSatisfaction)],
                NutritionalProfile = new NutritionalProfile(calorie, protein, iron, vitaminD, hydration),
            });

        // ── Tavern ───────────────────────────────────────────────────────────
        Food("tavern_pottage_01", "Pottage Bowl", "tavern", 0.3, 0.0, 400, 360, 0.65, 45.0, 12.0, 3.5, null, 30.0);
        Food("tavern_pottage_02", "Pottage Bowl", "tavern", 0.3, 0.0, 400, 360, 0.65, 45.0, 12.0, 3.5, null, 30.0);
        Food("tavern_pottage_03", "Pottage Bowl", "tavern", 0.3, 0.0, 400, 360, 0.65, 45.0, 12.0, 3.5, null, 30.0);
        Food("tavern_roast_01", "Roasted Chicken", "tavern", 0.4, 0.0, 350, 480, 0.80, 70.0, 40.0, 8.0, null, 5.0, heme: 0.4);
        Food("tavern_roast_02", "Roasted Chicken", "tavern", 0.4, 0.0, 350, 480, 0.80, 70.0, 40.0, 8.0, null, 5.0, heme: 0.4);
        Food("tavern_cheese_01", "Aged Cheese", "tavern", 0.0, 0.0, 120, 720, 0.40, 35.0, 15.0, 1.0, 3.0, 5.0);
        Drink("tavern_water_01", "Water Jug", "tavern", 0.0, 0.0, 500, 120, 0.90, 2.0, null, null, null, 90.0);
        Drink("tavern_water_02", "Water Jug", "tavern", 0.0, 0.0, 500, 120, 0.90, 2.0, null, null, null, 90.0);
        Drink("tavern_ale_01", "Ale Mug", "tavern", 0.0, 0.0, 300, 480, 0.60, 20.0, null, null, null, 40.0);

        // ── Castle Hall ──────────────────────────────────────────────────────
        Food("castle_salted_meat_01", "Salted Pork", "castle_hall", 0.0, 0.0, 300, 480, 0.75, 65.0, 45.0, 10.0, null, 2.0, heme: 0.4);
        Food("castle_salted_meat_02", "Salted Pork", "castle_hall", 0.0, 0.0, 300, 480, 0.75, 65.0, 45.0, 10.0, null, 2.0, heme: 0.4);
        Food("castle_dried_fish_01", "Dried Herring", "castle_hall", 0.0, 0.0, 150, 720, 0.60, 50.0, 35.0, 4.0, 40.0, 3.0, heme: 0.4);
        Food("castle_dried_fish_02", "Dried Herring", "castle_hall", 0.0, 0.0, 150, 720, 0.60, 50.0, 35.0, 4.0, 40.0, 3.0, heme: 0.4);
        Food("castle_fine_bread_01", "Fine White Bread", "castle_hall", 0.0, 0.0, 200, 480, 0.55, 55.0, 8.0, 2.0, null, 5.0);
        Drink("castle_wine_01", "Wine Cup", "castle_hall", 0.0, 0.0, 250, 360, 0.50, 15.0, null, null, null, 35.0);
        Drink("castle_wine_02", "Wine Cup", "castle_hall", 0.0, 0.0, 250, 360, 0.50, 15.0, null, null, null, 35.0);

        // ── Courtyard ────────────────────────────────────────────────────────
        Food("courtyard_dark_bread_01", "Dark Rye Bread", "courtyard", 0.0, 0.0, 200, 480, 0.50, 50.0, 7.0, 4.5, null, 8.0);
        Food("courtyard_dark_bread_02", "Dark Rye Bread", "courtyard", 0.0, 0.0, 200, 480, 0.50, 50.0, 7.0, 4.5, null, 8.0);
        Food("courtyard_apple_01", "Apple", "courtyard", 0.0, 0.0, 80, 720, 0.25, 18.0, 0.5, 0.5, null, 15.0);
        Food("courtyard_apple_02", "Apple", "courtyard", 0.0, 0.0, 80, 720, 0.25, 18.0, 0.5, 0.5, null, 15.0);
        Drink("courtyard_water_01", "Well Water", "courtyard", 0.0, 0.0, 500, 60, 0.95, 2.0, null, null, null, 92.0);
        Drink("courtyard_water_02", "Well Water", "courtyard", 0.0, 0.0, 500, 60, 0.95, 2.0, null, null, null, 92.0);

        // ── Stables ──────────────────────────────────────────────────────────
        Food("stables_bread_01", "Coarse Bread", "stables", 0.0, 0.0, 180, 720, 0.45, 45.0, 6.0, 3.5, null, 6.0);
        Drink("stables_water_01", "Trough Water", "stables", 0.0, 0.0, 500, 60, 0.80, 2.0, null, null, null, 85.0, pickable: false);

        // ── Herb Garden ──────────────────────────────────────────────────────
        Food("herb_garden_kale_01", "Wild Kale", "herb_garden", 0.0, 0.0, 100, 1440, 0.30, 12.0, 3.5, 6.0, null, 20.0, vitaminC: 20);
        Food("herb_garden_kale_02", "Wild Kale", "herb_garden", 0.0, 0.0, 100, 1440, 0.30, 12.0, 3.5, 6.0, null, 20.0, vitaminC: 20);
        Food("herb_garden_legumes_01", "Lentils", "herb_garden", 0.0, 0.0, 150, 1440, 0.55, 40.0, 10.0, 7.5, null, 12.0);
        Food("herb_garden_legumes_02", "Lentils", "herb_garden", 0.0, 0.0, 150, 1440, 0.55, 40.0, 10.0, 7.5, null, 12.0);
        Food("herb_garden_herbs_01", "Healing Herbs", "herb_garden", 0.0, 0.0, 30, 720, 0.15, 5.0, 1.0, 2.0, null, 8.0);
        Drink("herb_garden_water_01", "Rain Barrel", "herb_garden", 0.0, 0.0, 500, 120, 0.90, 2.0, null, null, null, 90.0, pickable: false);

        // ── Market Square ────────────────────────────────────────────────────
        Food("market_bread_01", "Peasant Bread", "market_square", 0.0, 0.0, 200, 480, 0.50, 48.0, 7.0, 3.5, null, 7.0);
        Food("market_bread_02", "Peasant Bread", "market_square", 0.0, 0.0, 200, 480, 0.50, 48.0, 7.0, 3.5, null, 7.0);
        Food("market_cheese_01", "Fresh Cheese", "market_square", 0.0, 0.0, 120, 720, 0.40, 32.0, 14.0, 1.0, 3.0, 6.0);
        Food("market_dried_01", "Dried Peas", "market_square", 0.0, 0.0, 200, 720, 0.45, 38.0, 9.0, 5.0, null, 5.0);
        Food("market_apple_01", "Market Apple", "market_square", 0.0, 0.0, 90, 480, 0.25, 18.0, 0.5, 0.5, null, 15.0);
        Food("market_apple_02", "Market Apple", "market_square", 0.0, 0.0, 90, 480, 0.25, 18.0, 0.5, 0.5, null, 15.0);
        Drink("market_water_01", "Water Barrel", "market_square", 0.0, 0.0, 500, 60, 0.90, 2.0, null, null, null, 92.0, pickable: false);

        // ── Forest ───────────────────────────────────────────────────────────
        Food("forest_mushrooms_01", "Wild Mushrooms", "forest", 0.0, 0.0, 80, 720, 0.30, 10.0, 3.0, 2.5, 18.0, 10.0);
        Food("forest_mushrooms_02", "Wild Mushrooms", "forest", 0.0, 0.0, 80, 720, 0.30, 10.0, 3.0, 2.5, 18.0, 10.0);
        Food("forest_mushrooms_03", "Wild Mushrooms", "forest", 0.0, 0.0, 80, 720, 0.30, 10.0, 3.0, 2.5, 18.0, 10.0);
        Food("forest_berries_01", "Elderberries", "forest", 0.0, 0.0, 60, 1440, 0.20, 15.0, 0.8, 1.5, null, 25.0, vitaminC: 15);
        Food("forest_berries_02", "Elderberries", "forest", 0.0, 0.0, 60, 1440, 0.20, 15.0, 0.8, 1.5, null, 25.0, vitaminC: 15);
        Food("forest_nuts_01", "Hazelnuts", "forest", 0.0, 0.0, 100, 1440, 0.35, 28.0, 5.0, 2.0, null, 3.0);
        Drink("forest_stream_01", "Forest Stream", "forest", 0.0, 0.1, 500, 120, 0.85, 2.0, null, null, null, 88.0, pickable: false);

        // ── Forest Clearing ──────────────────────────────────────────────────
        Food("clearing_mushrooms_01", "Forest Mushrooms", "forest_clearing", 0.0, 0.0, 70, 720, 0.25, 10.0, 2.5, 2.0, 15.0, 10.0);
        Food("clearing_berries_01", "Wild Berries", "forest_clearing", 0.0, 0.0, 50, 1440, 0.18, 12.0, 0.6, 1.2, null, 22.0);
        Food("clearing_nuts_01", "Wild Nuts", "forest_clearing", 0.0, 0.0, 90, 1440, 0.30, 26.0, 4.5, 1.8, null, 3.0);
        Drink("clearing_water_01", "Creek Water", "forest_clearing", 0.0, 0.1, 500, 120, 0.85, 2.0, null, null, null, 88.0, pickable: false);

        // ── River Crossing ───────────────────────────────────────────────────
        Food("river_fish_01", "Fresh River Fish", "river_crossing", 0.1, 0.1, 200, 360, 0.70, 55.0, 38.0, 5.0, 55.0, 10.0, heme: 0.4);
        Food("river_fish_02", "Fresh River Fish", "river_crossing", 0.1, 0.1, 200, 360, 0.70, 55.0, 38.0, 5.0, 55.0, 10.0, heme: 0.4);
        Food("river_fish_03", "Fresh River Fish", "river_crossing", 0.1, 0.1, 200, 360, 0.70, 55.0, 38.0, 5.0, 55.0, 10.0, heme: 0.4);
        Food("river_cress_01", "Watercress", "river_crossing", 0.0, 0.0, 50, 720, 0.15, 5.0, 2.0, 5.5, null, 25.0, vitaminC: 25);
        Drink("river_water_01", "River Water", "river_crossing", 0.0, 0.1, 500, 30, 0.95, 2.0, null, null, null, 95.0, pickable: false);
        Drink("river_water_02", "River Water", "river_crossing", 0.0, 0.1, 500, 30, 0.95, 2.0, null, null, null, 95.0, pickable: false);

        // ── Mountain Pass ────────────────────────────────────────────────────
        Food("mountain_jerky_01", "Dried Venison", "mountain_pass", 0.0, 0.0, 120, 1440, 0.60, 55.0, 42.0, 12.0, null, 1.0, heme: 0.4);
        Food("mountain_jerky_02", "Dried Venison", "mountain_pass", 0.0, 0.0, 120, 1440, 0.60, 55.0, 42.0, 12.0, null, 1.0, heme: 0.4);
        Food("mountain_berries_01", "Mountain Berries", "mountain_pass", 0.0, 0.0, 40, 1440, 0.15, 10.0, 0.5, 1.0, null, 18.0);
        Drink("mountain_snow_01", "Melted Snow", "mountain_pass", 0.0, 0.0, 500, 60, 0.80, 1.0, null, null, null, 80.0, pickable: false);
    }

    #endregion Food & drink objects

    #region Non-food furniture/tool/light/ambient objects

    private static void AddFurnitureObjects(IWorldObjectProvider objectProvider)
    {
        void Obj(string id, string name, WorldObjectCategory category, string locationId, double heat, double noise,
            bool blocksLos, int weight, PickupItemKind itemKind, bool pickable, params WorldObjectAffordance[] affordances)
            => objectProvider.AddObject(new WorldObject
            {
                Category = category,
                Id = id,
                DisplayName = name,
                LocationId = locationId,
                HeatSignature = heat,
                AmbientNoise = noise,
                IsAvailable = true,
                IsPickable = pickable,
                BlocksLineOfSight = blocksLos,
                ItemKind = itemKind,
                Respawns = false,
                RespawnMinutes = 0,
                WeightGrams = weight,
                Affordances = [.. affordances],
            });

        // ── Rest objects ─────────────────────────────────────────────────────
        Obj("inn_bed_01", "Inn Bed", WorldObjectCategory.Shelter, "inn_room", 0.1, 0.0, false, 50000, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Rest, 1.00));
        Obj("tavern_bench_01", "Tavern Bench", WorldObjectCategory.Furniture, "tavern", 0.0, 0.0, false, 15000, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Rest, 0.45));
        Obj("tavern_bench_02", "Tavern Bench", WorldObjectCategory.Furniture, "tavern", 0.0, 0.0, false, 15000, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Rest, 0.45));
        Obj("castle_chair_01", "Carved Chair", WorldObjectCategory.Furniture, "castle_hall", 0.0, 0.0, false, 8000, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Rest, 0.60));
        Obj("castle_chair_02", "Carved Chair", WorldObjectCategory.Furniture, "castle_hall", 0.0, 0.0, false, 8000, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Rest, 0.60));
        Obj("library_chair_01", "Reading Chair", WorldObjectCategory.Furniture, "library", 0.0, 0.0, false, 7000, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Rest, 0.65));
        Obj("throne_bench_01", "Stone Bench", WorldObjectCategory.Furniture, "throne_room", 0.0, 0.0, false, 20000, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Rest, 0.40));
        Obj("stables_hay_01", "Hay Pile", WorldObjectCategory.Shelter, "stables", 0.1, 0.0, false, 5000, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Rest, 0.35));
        Obj("dungeon_straw_01", "Straw Mat", WorldObjectCategory.Shelter, "dungeon_cell", 0.0, 0.0, false, 1000, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Rest, 0.20));
        Obj("clearing_ground_01", "Mossy Ground", WorldObjectCategory.Shelter, "forest_clearing", 0.0, 0.0, false, 0, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Rest, 0.25));
        Obj("courtyard_bench_01", "Stone Bench", WorldObjectCategory.Furniture, "courtyard", 0.0, 0.0, false, 20000, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Rest, 0.40));
        Obj("chapel_pew_01", "Chapel Pew", WorldObjectCategory.Furniture, "chapel", 0.0, 0.0, false, 12000, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Rest, 0.35));
        Obj("chapel_pew_02", "Chapel Pew", WorldObjectCategory.Furniture, "chapel", 0.0, 0.0, false, 12000, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Rest, 0.35));

        // ── Work objects ─────────────────────────────────────────────────────
        Obj("blacksmith_anvil_01", "Iron Anvil", WorldObjectCategory.Tool, "blacksmith", 0.2, 0.4, false, 80000, PickupItemKind.Tool, false,
            new WorldObjectAffordance(AffordanceType.Work, 0.90));
        Obj("blacksmith_bellows_01", "Bellows", WorldObjectCategory.Tool, "blacksmith", 0.1, 0.2, false, 5000, PickupItemKind.Tool, false,
            new WorldObjectAffordance(AffordanceType.Work, 0.60));
        Obj("herb_trowel_01", "Garden Trowel", WorldObjectCategory.Tool, "herb_garden", 0.0, 0.0, false, 500, PickupItemKind.Tool, false,
            new WorldObjectAffordance(AffordanceType.Work, 0.80));
        Obj("herb_basket_01", "Harvest Basket", WorldObjectCategory.Tool, "herb_garden", 0.0, 0.0, false, 800, PickupItemKind.Tool, false,
            new WorldObjectAffordance(AffordanceType.Work, 0.60));
        Obj("stables_brush_01", "Horse Brush", WorldObjectCategory.Tool, "stables", 0.0, 0.0, false, 400, PickupItemKind.Tool, false,
            new WorldObjectAffordance(AffordanceType.Work, 0.70));
        Obj("stables_pitchfork_01", "Pitchfork", WorldObjectCategory.Tool, "stables", 0.0, 0.0, false, 2000, PickupItemKind.Tool, false,
            new WorldObjectAffordance(AffordanceType.Work, 0.80));
        Obj("mill_millstone_01", "Millstone", WorldObjectCategory.Tool, "abandoned_mill", 0.0, 0.2, false, 200000, PickupItemKind.Tool, false,
            new WorldObjectAffordance(AffordanceType.Work, 0.85));
        Obj("library_desk_01", "Writing Desk", WorldObjectCategory.Furniture, "library", 0.0, 0.0, true, 25000, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Work, 0.75));
        Obj("castle_map_table_01", "Map Table", WorldObjectCategory.Furniture, "castle_hall", 0.0, 0.0, true, 40000, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Work, 0.70));

        // ── Entertainment objects ────────────────────────────────────────────
        Obj("tavern_lute_01", "Lute", WorldObjectCategory.Tool, "tavern", 0.0, 0.3, false, 1500, PickupItemKind.Instrument, true,
            new WorldObjectAffordance(AffordanceType.Entertainment, 0.80));
        Obj("tavern_dice_01", "Dice Set", WorldObjectCategory.Tool, "tavern", 0.0, 0.1, false, 100, PickupItemKind.Instrument, true,
            new WorldObjectAffordance(AffordanceType.Entertainment, 0.65));
        Obj("castle_chess_01", "Chess Board", WorldObjectCategory.Tool, "castle_hall", 0.0, 0.0, false, 2000, PickupItemKind.Instrument, false,
            new WorldObjectAffordance(AffordanceType.Entertainment, 0.75));
        Obj("castle_harp_01", "Harp", WorldObjectCategory.Tool, "castle_hall", 0.0, 0.2, false, 8000, PickupItemKind.Instrument, false,
            new WorldObjectAffordance(AffordanceType.Entertainment, 0.80));
        Obj("library_book_01", "Ancient Tome", WorldObjectCategory.Tool, "library", 0.0, 0.0, false, 1200, PickupItemKind.Instrument, true,
            new WorldObjectAffordance(AffordanceType.Entertainment, 0.75), new WorldObjectAffordance(AffordanceType.Work, 0.50));
        Obj("library_book_02", "Leather-Bound Book", WorldObjectCategory.Tool, "library", 0.0, 0.0, false, 900, PickupItemKind.Instrument, true,
            new WorldObjectAffordance(AffordanceType.Entertainment, 0.75), new WorldObjectAffordance(AffordanceType.Work, 0.50));
        Obj("chapel_altar_01", "Stone Altar", WorldObjectCategory.Furniture, "chapel", 0.0, 0.0, false, 500000, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Entertainment, 0.60), new WorldObjectAffordance(AffordanceType.Social, 0.40));
        Obj("market_stage_01", "Storyteller Stage", WorldObjectCategory.Furniture, "market_square", 0.0, 0.2, false, 30000, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Entertainment, 0.55), new WorldObjectAffordance(AffordanceType.Social, 0.45));

        // ── Warmth objects ───────────────────────────────────────────────────
        Obj("tavern_fireplace_01", "Fireplace", WorldObjectCategory.LightSource, "tavern", 0.8, 0.1, false, 0, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Warmth, 0.85), new WorldObjectAffordance(AffordanceType.MoodBoost, 0.30), new WorldObjectAffordance(AffordanceType.Social, 0.30));
        Obj("castle_hearth_01", "Grand Hearth", WorldObjectCategory.LightSource, "castle_hall", 0.7, 0.1, false, 0, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Warmth, 0.80), new WorldObjectAffordance(AffordanceType.MoodBoost, 0.25));
        Obj("inn_hearth_01", "Small Hearth", WorldObjectCategory.LightSource, "inn_room", 0.5, 0.0, false, 0, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Warmth, 0.70));
        Obj("blacksmith_forge_01", "Forge", WorldObjectCategory.Tool, "blacksmith", 1.0, 0.3, false, 0, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Warmth, 1.00), new WorldObjectAffordance(AffordanceType.Work, 0.85));
        Obj("clearing_campfire_01", "Campfire", WorldObjectCategory.LightSource, "forest_clearing", 0.6, 0.1, false, 0, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Warmth, 0.65), new WorldObjectAffordance(AffordanceType.MoodBoost, 0.35));
        Obj("chapel_candles_01", "Altar Candles", WorldObjectCategory.LightSource, "chapel", 0.2, 0.0, false, 0, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.Warmth, 0.25));

        // ── Mood boost objects ───────────────────────────────────────────────
        Obj("herb_flowers_01", "Wild Flowers", WorldObjectCategory.Ambient, "herb_garden", 0.0, 0.0, false, 0, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.MoodBoost, 0.60));
        Obj("castle_tapestry_01", "Woven Tapestry", WorldObjectCategory.Ambient, "castle_hall", 0.0, 0.0, false, 0, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.MoodBoost, 0.40));
        Obj("chapel_glass_01", "Stained Glass", WorldObjectCategory.Ambient, "chapel", 0.0, 0.0, false, 0, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.MoodBoost, 0.50));
        Obj("library_shelves_01", "Bookshelves", WorldObjectCategory.Furniture, "library", 0.0, 0.0, true, 0, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.MoodBoost, 0.45));
        Obj("forest_nature_01", "Forest Canopy", WorldObjectCategory.Ambient, "forest", 0.0, 0.1, false, 0, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.MoodBoost, 0.55));
        Obj("river_sound_01", "Flowing River", WorldObjectCategory.Ambient, "river_crossing", 0.0, 0.2, false, 0, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.MoodBoost, 0.50));
        Obj("market_atmosphere_01", "Market Bustle", WorldObjectCategory.Ambient, "market_square", 0.0, 0.3, false, 0, PickupItemKind.None, false,
            new WorldObjectAffordance(AffordanceType.MoodBoost, 0.35));
    }

    #endregion Non-food furniture/tool/light/ambient objects
}
