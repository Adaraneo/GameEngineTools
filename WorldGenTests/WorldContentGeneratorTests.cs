// WorldContentGeneratorTests.cs
// Copyright (c) 50PSoftware

using GameEngineTools.World.Data;
using GameEngineTools.World.Location;
using GameEngineTools.World.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorldGen.Generation;

namespace WorldGenTests;

[TestClass]
public class WorldContentGeneratorTests
{
    // The real embedded default catalog — exercises the actual production content, not a
    // hand-rolled test fixture, same spirit as TerraGenTests using real noise/erosion params.
    private static readonly IReadOnlyList<FoodTemplate> Catalog = NutritionCatalogLoader.Load();

    private static SqliteWorldDatabase NewWorldDb()
    {
        var db = new SqliteWorldDatabase(":memory:");
        WorldDatabaseSeeder.InitializeSchemaOnly(db);
        return db;
    }

    /// <summary>Flat 1x1km tile at a fixed elevation — the simplest possible terrain, land
    /// everywhere, zero slope (classifies Plains below the mountain threshold).</summary>
    private static TerrainHeightmap FlatTile(double elevation, double originX = 0.0, double originY = 0.0, int size = 200, double cellSize = 5.0)
        => new("flat", originX, originY, cellSize, size, size, Enumerable.Repeat((float)elevation, size * size).ToArray());

    /// <summary>A steady east-west ramp — well below the mountain threshold but with a real,
    /// constant slope, so it classifies Forest (not Plains) regardless of where on it a
    /// candidate lands.</summary>
    private static TerrainHeightmap RampTile(double slope, int size = 200, double cellSize = 5.0)
    {
        var values = new float[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                values[y * size + x] = (float)(x * cellSize * slope);
        return new TerrainHeightmap("ramp", 0.0, 0.0, cellSize, size, size, values);
    }

    [TestMethod]
    public void Generate_FlatLandTile_PlacesRequestedCount()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) }; // plenty of room, well below the mountain threshold
        var options = new WorldContentGenerator.Options(Count: 5, MinDistanceMeters: 20.0);

        var result = WorldContentGenerator.Generate(db, tiles, options, new Random(1), Catalog);

        Assert.AreEqual(5, result.LocationsPlaced);
        Assert.AreEqual(5, db.GetAllLocations().Count);
    }

    [TestMethod]
    public void Generate_PlacedLocations_AreWithinTileBounds()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(10.0, originX: 1000.0, originY: 2000.0, size: 100, cellSize: 2.0) };
        var options = new WorldContentGenerator.Options(Count: 8, MinDistanceMeters: 5.0);

        WorldContentGenerator.Generate(db, tiles, options, new Random(2), Catalog);

        var maxX = 1000.0 + 100 * 2.0;
        var maxY = 2000.0 + 100 * 2.0;
        foreach (var (descriptor, _) in db.GetAllLocations())
        {
            Assert.IsTrue(descriptor.X >= 1000.0 && descriptor.X <= maxX, $"X {descriptor.X} outside tile bounds.");
            Assert.IsTrue(descriptor.Y >= 2000.0 && descriptor.Y <= maxY, $"Y {descriptor.Y} outside tile bounds.");
        }
    }

    [TestMethod]
    public void Generate_NeverPlacesUnderwater()
    {
        using var db = NewWorldDb();
        // Left half of the grid is underwater (negative), right half is dry land.
        const int size = 100;
        var values = new float[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                values[y * size + x] = x < size / 2 ? -50f : 50f;
        var tile = new TerrainHeightmap("halfwater", 0.0, 0.0, 5.0, size, size, values);

        var options = new WorldContentGenerator.Options(Count: 10, MinDistanceMeters: 5.0);
        WorldContentGenerator.Generate(db, new[] { tile }, options, new Random(3), Catalog);

        foreach (var (descriptor, _) in db.GetAllLocations())
            Assert.IsTrue(descriptor.AltitudeMeters >= 0.0, "A location was placed underwater.");
    }

    [TestMethod]
    public void Generate_AboveMountainThreshold_ClassifiesAsMountain()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(500.0) }; // above the default 300m threshold everywhere
        var options = new WorldContentGenerator.Options(Count: 1, MinDistanceMeters: 10.0);

        WorldContentGenerator.Generate(db, tiles, options, new Random(4), Catalog);

        var (descriptor, _) = db.GetAllLocations().Single();
        Assert.AreEqual(TerrainType.Mountain, descriptor.Terrain);
        Assert.IsTrue(descriptor.Id.StartsWith("mountain_", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Generate_FlatBelowMountainThreshold_ClassifiesAsPlains()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) }; // zero slope, dry, far from any water sample
        var options = new WorldContentGenerator.Options(Count: 1, MinDistanceMeters: 10.0);

        WorldContentGenerator.Generate(db, tiles, options, new Random(5), Catalog);

        var (descriptor, _) = db.GetAllLocations().Single();
        Assert.AreEqual(TerrainType.Plains, descriptor.Terrain);
        Assert.IsTrue(descriptor.Id.StartsWith("plains_", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Generate_SlopedBelowMountainThreshold_ClassifiesAsForest()
    {
        using var db = NewWorldDb();
        // 5% grade — comfortably above the default 3% Plains threshold, comfortably below Mountain.
        var tiles = new[] { RampTile(slope: 0.05) };
        var options = new WorldContentGenerator.Options(Count: 1, MinDistanceMeters: 10.0);

        WorldContentGenerator.Generate(db, tiles, options, new Random(6), Catalog);

        var (descriptor, _) = db.GetAllLocations().Single();
        Assert.AreEqual(TerrainType.Forest, descriptor.Terrain);
        Assert.IsTrue(descriptor.Id.StartsWith("forest_", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Generate_FlatLandNearWater_ClassifiesAsCoastline()
    {
        using var db = NewWorldDb();
        // Underwater strip along the west edge; everything else flat dry land — any candidate
        // within the default 60m coast radius of x=100 should read as Coastline.
        const int size = 200;
        const double cellSize = 5.0;
        var values = new float[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                values[y * size + x] = x < 20 ? -20f : 30f; // water for x<100m, land beyond
        var tile = new TerrainHeightmap("coast", 0.0, 0.0, cellSize, size, size, values);

        var options = new WorldContentGenerator.Options(Count: 1, MinDistanceMeters: 10.0, MaxPlacementAttempts: 500);

        // Force a candidate near the shoreline by keeping the search tight: retry until we land
        // one within the coast radius, then assert on it directly (placement itself is random).
        var found = false;
        for (var seed = 0; seed < 200 && !found; seed++)
        {
            using var probeDb = NewWorldDb();
            WorldContentGenerator.Generate(probeDb, new[] { tile }, options, new Random(seed), Catalog);
            var loc = probeDb.GetAllLocations().SingleOrDefault();
            if (loc.Descriptor is not null && loc.Descriptor.X is >= 100.0 and <= 160.0)
            {
                Assert.AreEqual(TerrainType.Coastline, loc.Descriptor.Terrain,
                    $"Candidate at X={loc.Descriptor.X} should be within the coast radius of the shoreline at x=100.");
                found = true;
            }
        }

        Assert.IsTrue(found, "Never landed a candidate near the shoreline across 200 seeds — test setup is broken.");
    }

    [TestMethod]
    public void Generate_MinDistanceTooTight_SkipsUnplaceable()
    {
        using var db = NewWorldDb();
        // A tiny 10x10m tile can't possibly fit 20 locations 500m apart from each other.
        var tiles = new[] { FlatTile(10.0, size: 5, cellSize: 2.0) };
        var options = new WorldContentGenerator.Options(Count: 20, MinDistanceMeters: 500.0, MaxPlacementAttempts: 10);

        var result = WorldContentGenerator.Generate(db, tiles, options, new Random(6), Catalog);

        Assert.IsTrue(result.LocationsPlaced < 20, "Expected some locations to be skipped as unplaceable.");
        Assert.AreEqual(result.LocationsPlaced, db.GetAllLocations().Count);
    }

    [TestMethod]
    public void Generate_ConnectsNearestNeighbors_Bidirectionally()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0, size: 150, cellSize: 10.0) }; // same 1500m extent, fewer cells for a faster RoadPathfinder run
        var options = new WorldContentGenerator.Options(Count: 6, MinDistanceMeters: 50.0, ConnectionsPerLocation: 2);

        WorldContentGenerator.Generate(db, tiles, options, new Random(7), Catalog);

        var connections = db.GetAllConnections();
        Assert.IsTrue(connections.Count > 0, "Expected at least some connections to be created.");

        // Every connection must have its exact-distance reverse counterpart.
        foreach (var (from, to, distance) in connections)
        {
            var reverse = connections.SingleOrDefault(c => c.FromId == to && c.ToId == from);
            Assert.IsNotNull(reverse, $"Missing reverse connection for {from}->{to}.");
            Assert.AreEqual(distance, reverse.DistanceMeters, 1e-6);
        }
    }

    [TestMethod]
    public void Generate_RoadDistance_UsesTerrainAwarePathNotStraightLine()
    {
        using var db = NewWorldDb();
        // A steep ridge sits directly between two same-tile locations forced to opposite ends —
        // the terrain-aware road should cost measurably more than the straight-line distance.
        const int size = 100;
        const double cellSize = 5.0;
        var values = new float[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                values[y * size + x] = Math.Abs(x - size / 2) < 5 ? 200f : 0f; // a steep wall down the middle
        var tile = new TerrainHeightmap("ridge", 0.0, 0.0, cellSize, size, size, values);

        var straightLine = RoadPathfinder.FindPath(tile, 10.0, 250.0, 490.0, 250.0);
        Assert.IsNotNull(straightLine);
        var euclidean = Math.Sqrt(Math.Pow(490.0 - 10.0, 2) + 0);

        Assert.IsTrue(straightLine.LengthMeters >= euclidean,
            "A path crossing a steep ridge must never be cheaper than crossing flat ground of the same span.");
    }

    [TestMethod]
    public void Generate_ZeroConnectionsPerLocation_CreatesNoConnections()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(Count: 5, MinDistanceMeters: 20.0, ConnectionsPerLocation: 0);

        var result = WorldContentGenerator.Generate(db, tiles, options, new Random(8), Catalog);

        Assert.AreEqual(0, result.ConnectionsCreated);
        Assert.AreEqual(0, db.GetAllConnections().Count);
    }

    [TestMethod]
    public void Generate_EachLocation_GetsFoodDrinkAndRestObjects()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        // Force Camp tier so the "exactly one object per need" expectation is deterministic —
        // Village/Town depth is covered separately below.
        var options = new WorldContentGenerator.Options(
            Count: 3, MinDistanceMeters: 20.0, ForcedTier: WorldContentGenerator.SettlementTier.Camp);

        var result = WorldContentGenerator.Generate(db, tiles, options, new Random(9), Catalog);

        Assert.AreEqual(9, result.ObjectsCreated); // 3 objects per location
        foreach (var (descriptor, _) in db.GetAllLocations())
        {
            var objects = db.GetAllObjectsAt(descriptor.Id);
            Assert.AreEqual(3, objects.Count);

            var affordanceTypes = objects.SelectMany(o => o.Affordances).Select(a => a.Type).ToHashSet();
            Assert.IsTrue(affordanceTypes.Contains(AffordanceType.Hunger));
            Assert.IsTrue(affordanceTypes.Contains(AffordanceType.Thirst));
            Assert.IsTrue(affordanceTypes.Contains(AffordanceType.Rest));
        }
    }

    [TestMethod]
    public void Generate_TownTier_GetsDeeperObjectSetAndSocialObject()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(
            Count: 1, MinDistanceMeters: 10.0, ForcedTier: WorldContentGenerator.SettlementTier.Town);

        WorldContentGenerator.Generate(db, tiles, options, new Random(13), Catalog);

        var (descriptor, _) = db.GetAllLocations().Single();
        var objects = db.GetAllObjectsAt(descriptor.Id);
        var affordanceTypes = objects.SelectMany(o => o.Affordances).Select(a => a.Type).ToList();

        // Plains catalog only has 1 Hunger + 1 Thirst template (plus 1 Rest, Any) — Town's requested
        // depth of 3 per need simply saturates at however many distinct templates actually exist.
        Assert.IsTrue(affordanceTypes.Contains(AffordanceType.Social), "Town tier should pick up the Any-tagged Social template.");
        Assert.AreEqual(40, descriptor.Capacity);
        Assert.IsTrue(descriptor.AllowsPrivacy);
    }

    [TestMethod]
    public void Generate_CampTier_HasSmallCapacityAndNoPrivacy()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(
            Count: 1, MinDistanceMeters: 10.0, ForcedTier: WorldContentGenerator.SettlementTier.Camp);

        WorldContentGenerator.Generate(db, tiles, options, new Random(14), Catalog);

        var (descriptor, _) = db.GetAllLocations().Single();
        Assert.AreEqual(6, descriptor.Capacity);
        Assert.IsFalse(descriptor.AllowsPrivacy);
    }

    [TestMethod]
    public void Generate_SameSeed_IsDeterministic()
    {
        using var dbA = NewWorldDb();
        using var dbB = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(Count: 5, MinDistanceMeters: 20.0);

        WorldContentGenerator.Generate(dbA, tiles, options, new Random(42), Catalog);
        WorldContentGenerator.Generate(dbB, tiles, options, new Random(42), Catalog);

        var locationsA = dbA.GetAllLocations().OrderBy(l => l.Descriptor.Id).ToList();
        var locationsB = dbB.GetAllLocations().OrderBy(l => l.Descriptor.Id).ToList();

        Assert.AreEqual(locationsA.Count, locationsB.Count);
        for (var i = 0; i < locationsA.Count; i++)
        {
            Assert.AreEqual(locationsA[i].Descriptor.Id, locationsB[i].Descriptor.Id);
            Assert.AreEqual(locationsA[i].Descriptor.X, locationsB[i].Descriptor.X, 1e-9);
            Assert.AreEqual(locationsA[i].Descriptor.Y, locationsB[i].Descriptor.Y, 1e-9);
            Assert.AreEqual(locationsA[i].Descriptor.Terrain, locationsB[i].Descriptor.Terrain);
        }
    }

    [TestMethod]
    public void Generate_TwoRunsAgainstSameDb_DoNotCollideIds()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(Count: 3, MinDistanceMeters: 20.0);

        // Two separate runs (as if worldgen were invoked twice against the same world.db) — the
        // run token must keep their ids from colliding, or INSERT OR IGNORE would silently drop
        // the second run's rows entirely.
        WorldContentGenerator.Generate(db, tiles, options, new Random(10), Catalog);
        WorldContentGenerator.Generate(db, tiles, options, new Random(11), Catalog);

        Assert.AreEqual(6, db.GetAllLocations().Count);
    }

    [TestMethod]
    public void Generate_UsesOnlyMatchingBiomeOrAnyTemplates()
    {
        var customCatalog = NutritionCatalogLoader.Parse("""
            TemplateId;DisplayName;Biome;AffordanceType;Satisfaction;ItemKind;WeightGrams;RespawnMinutes;Pickable;CalorieGain;ProteinGain;IronGain;VitaminDGain;HydrationGain;HemeIronFraction;VitaminCMilligrams
            plains_only;Plains Only;Plains;Hunger;0.3;Food;50;600;true;10;;;;5;;
            mountain_only;Mountain Only;Mountain;Hunger;0.3;Food;50;600;true;10;;;;5;;
            any_water;Any Water;Any;Thirst;0.8;Drink;500;120;false;0;;;;80;;
            """);

        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) }; // flat + below threshold — classified Plains
        var options = new WorldContentGenerator.Options(Count: 3, MinDistanceMeters: 20.0);

        WorldContentGenerator.Generate(db, tiles, options, new Random(20), customCatalog);

        foreach (var (descriptor, _) in db.GetAllLocations())
        {
            var objectIds = db.GetAllObjectsAt(descriptor.Id).Select(o => o.Id).ToList();
            Assert.IsTrue(objectIds.Any(id => id.Contains("plains_only")), "Expected the Plains-tagged food template to be used on a Plains location.");
            Assert.IsFalse(objectIds.Any(id => id.Contains("mountain_only")), "Mountain-tagged template must not be used on a Plains location.");
            Assert.IsTrue(objectIds.Any(id => id.Contains("any_water")), "Expected the Any-tagged drink template to be used regardless of biome.");
        }
    }

    [TestMethod]
    public void Generate_NoMatchingTemplateForANeed_SkipsThatObjectWithoutError()
    {
        var foodOnlyCatalog = NutritionCatalogLoader.Parse("""
            TemplateId;DisplayName;Biome;AffordanceType;Satisfaction;ItemKind;WeightGrams;RespawnMinutes;Pickable;CalorieGain;ProteinGain;IronGain;VitaminDGain;HydrationGain;HemeIronFraction;VitaminCMilligrams
            only_food;Only Food;Any;Hunger;0.3;Food;50;600;true;10;;;;5;;
            """);

        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(Count: 1, MinDistanceMeters: 10.0);

        var result = WorldContentGenerator.Generate(db, tiles, options, new Random(21), foodOnlyCatalog);

        Assert.AreEqual(1, result.ObjectsCreated); // only Hunger has a template — Thirst/Rest skipped
    }

    [TestMethod]
    public void Generate_TectonicBoundaryProximity_RaisesDangerLevel()
    {
        using var dbNear = NewWorldDb();
        using var dbOff = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };

        var withTectonics = new WorldContentGenerator.Options(
            Count: 20, MinDistanceMeters: 10.0, TectonicPlateCount: 6, TectonicSeed: 99);
        var withoutTectonics = new WorldContentGenerator.Options(
            Count: 20, MinDistanceMeters: 10.0, TectonicPlateCount: 0);

        WorldContentGenerator.Generate(dbNear, tiles, withTectonics, new Random(30), Catalog);
        WorldContentGenerator.Generate(dbOff, tiles, withoutTectonics, new Random(30), Catalog);

        var dangerWithTectonics = dbNear.GetAllLocations().Sum(l => l.Descriptor.DangerLevel);
        var dangerWithoutTectonics = dbOff.GetAllLocations().Sum(l => l.Descriptor.DangerLevel);

        Assert.IsTrue(dangerWithTectonics >= dangerWithoutTectonics,
            "Tectonic weighting should never LOWER total danger versus the same run with it disabled.");
    }

    [TestMethod]
    public void Generate_HousesEnabled_VillageGetsRestHousesReachableFromParent()
    {
        // Houses now lay out along a small number of radial streets, each a CHAIN (house connects
        // to the next one down its own street, not straight back to the parent) — so the
        // invariant to check is "every house is transitively reachable from the parent", not
        // "every house has a direct edge to the parent" (only each street's first house does).
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(
            Count: 1, MinDistanceMeters: 10.0, ForcedTier: WorldContentGenerator.SettlementTier.Village,
            GenerateHouses: true, HousesPerVillage: 3);

        WorldContentGenerator.Generate(db, tiles, options, new Random(50), Catalog);

        var locations = db.GetAllLocations();
        var parent = locations.Single(l => !l.Descriptor.Id.Contains("_house_"));
        var houses = locations.Where(l => l.Descriptor.Id.StartsWith(parent.Descriptor.Id + "_house_", StringComparison.Ordinal)).ToList();

        Assert.AreEqual(3, houses.Count);
        Assert.IsTrue(houses.All(h => h.Descriptor.Type == LocationType.Rest));

        var adjacency = locations.ToDictionary(l => l.Descriptor.Id, _ => new HashSet<string>());
        foreach (var (from, to, _) in db.GetAllConnections())
            adjacency[from].Add(to);

        var visited = new HashSet<string> { parent.Descriptor.Id };
        var queue = new Queue<string>();
        queue.Enqueue(parent.Descriptor.Id);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in adjacency[current])
                if (visited.Add(next)) queue.Enqueue(next);
        }

        foreach (var house in houses)
            Assert.IsTrue(visited.Contains(house.Descriptor.Id), $"Expected {house.Descriptor.Id} to be reachable from its parent settlement.");
    }

    [TestMethod]
    public void Generate_HousesEnabled_TownGetsASquare_ConnectedToParent()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(
            Count: 1, MinDistanceMeters: 10.0, ForcedTier: WorldContentGenerator.SettlementTier.Town,
            GenerateHouses: true, HousesPerTown: 6);

        WorldContentGenerator.Generate(db, tiles, options, new Random(53), Catalog);

        var locations = db.GetAllLocations();
        var parent = locations.Single(l => !l.Descriptor.Id.Contains("_house_") && !l.Descriptor.Id.EndsWith("_square", StringComparison.Ordinal));
        var square = locations.SingleOrDefault(l => l.Descriptor.Id == $"{parent.Descriptor.Id}_square");

        Assert.IsNotNull(square.Descriptor, "Expected a Town to get exactly one '_square' sub-location.");
        Assert.AreEqual(LocationType.Social, square.Descriptor.Type);
        Assert.IsFalse(square.Descriptor.AllowsPrivacy, "A public square should never allow privacy.");

        var connections = db.GetAllConnections();
        Assert.IsTrue(connections.Any(c => c.FromId == square.Descriptor.Id && c.ToId == parent.Descriptor.Id),
            "Expected the square to connect back to its parent settlement.");
    }

    [TestMethod]
    public void Generate_HousesEnabled_VillageDoesNotGetASquare()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(
            Count: 1, MinDistanceMeters: 10.0, ForcedTier: WorldContentGenerator.SettlementTier.Village,
            GenerateHouses: true, HousesPerVillage: 4);

        WorldContentGenerator.Generate(db, tiles, options, new Random(54), Catalog);

        var locations = db.GetAllLocations();
        Assert.IsFalse(locations.Any(l => l.Descriptor.Id.EndsWith("_square", StringComparison.Ordinal)),
            "Only Town tier should get a square — Village settlements are too small for a formal one.");
    }

    [TestMethod]
    public void Generate_HousesEnabled_ConnectionCount_MatchesOneEdgePerHousePlusSquare()
    {
        // Chain topology: every house creates EXACTLY one edge (to whatever's immediately behind
        // it on its street — the square/parent hub, or the previous house). Town tier adds
        // exactly one more edge for the square itself.
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(
            Count: 1, MinDistanceMeters: 10.0, ForcedTier: WorldContentGenerator.SettlementTier.Town,
            GenerateHouses: true, HousesPerTown: 7);

        var result = WorldContentGenerator.Generate(db, tiles, options, new Random(55), Catalog);

        Assert.AreEqual(7 + 1, result.ConnectionsCreated, "Expected 7 house edges + 1 square edge (no other connections for a single ForcedTier location with ConnectionsPerLocation's own hub logic finding nothing else to link).");
    }

    [TestMethod]
    public void Generate_HousesEnabled_CampTierGetsNoHouses()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(
            Count: 1, MinDistanceMeters: 10.0, ForcedTier: WorldContentGenerator.SettlementTier.Camp,
            GenerateHouses: true, HousesPerVillage: 3, HousesPerTown: 5);

        WorldContentGenerator.Generate(db, tiles, options, new Random(51), Catalog);

        Assert.AreEqual(1, db.GetAllLocations().Count, "Camp tier should not get any house sub-locations.");
    }

    [TestMethod]
    public void Generate_HousesDisabledByDefault_NoHousesCreated()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(
            Count: 1, MinDistanceMeters: 10.0, ForcedTier: WorldContentGenerator.SettlementTier.Town);

        WorldContentGenerator.Generate(db, tiles, options, new Random(52), Catalog);

        Assert.AreEqual(1, db.GetAllLocations().Count, "GenerateHouses defaults to false — existing callers should see exactly what they asked for.");
    }

    [TestMethod]
    public void Generate_CemeteryEnabled_CreatedAtDeterministicIdAndConnected()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(
            Count: 3, MinDistanceMeters: 20.0, Region: "Village", ConnectionsPerLocation: 2,
            GenerateCemetery: true);

        var result = WorldContentGenerator.Generate(db, tiles, options, new Random(53), Catalog);

        Assert.AreEqual("Village_cemetery", result.CemeteryLocationId);
        var cemetery = db.GetAllLocations().Single(l => l.Descriptor.Id == "Village_cemetery");
        Assert.AreEqual(LocationType.Public, cemetery.Descriptor.Type);

        var connections = db.GetAllConnections();
        Assert.IsTrue(connections.Any(c => c.FromId == "Village_cemetery"), "Expected the cemetery to be connected to at least one settlement.");
    }

    [TestMethod]
    public void Generate_CemeteryDisabledByDefault_NoCemeteryCreated()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(Count: 3, MinDistanceMeters: 20.0);

        var result = WorldContentGenerator.Generate(db, tiles, options, new Random(54), Catalog);

        Assert.IsNull(result.CemeteryLocationId);
        Assert.IsFalse(db.GetAllLocations().Any(l => l.Descriptor.Id.EndsWith("_cemetery", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Generate_ProductionChainEnabled_AttachedToLargestSettlementOnly()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(
            Count: 1, MinDistanceMeters: 10.0, ForcedTier: WorldContentGenerator.SettlementTier.Town,
            GenerateProductionChain: true);

        WorldContentGenerator.Generate(db, tiles, options, new Random(55), Catalog);

        var (descriptor, _) = db.GetAllLocations().Single();
        var itemKinds = db.GetAllObjectsAt(descriptor.Id).Select(o => o.ItemKind).ToHashSet();
        Assert.IsTrue(itemKinds.Contains(PickupItemKind.Grain));
        Assert.IsTrue(itemKinds.Contains(PickupItemKind.Flour));
        Assert.IsTrue(itemKinds.Contains(PickupItemKind.Bread));
    }

    [TestMethod]
    public void Generate_ProductionChainEnabled_CampOnlySkipsChain()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(
            Count: 1, MinDistanceMeters: 10.0, ForcedTier: WorldContentGenerator.SettlementTier.Camp,
            GenerateProductionChain: true);

        var result = WorldContentGenerator.Generate(db, tiles, options, new Random(56), Catalog);

        var (descriptor, _) = db.GetAllLocations().Single();
        var itemKinds = db.GetAllObjectsAt(descriptor.Id).Select(o => o.ItemKind).ToHashSet();
        Assert.IsFalse(itemKinds.Contains(PickupItemKind.Grain), "Camp tier is too small to host a production chain.");
    }

    [TestMethod]
    public void Generate_EmptyTileList_Throws()
    {
        using var db = NewWorldDb();
        var options = new WorldContentGenerator.Options(Count: 1);

        Assert.Throws<ArgumentException>(() =>
            WorldContentGenerator.Generate(db, [], options, new Random(12), Catalog));
    }
}
