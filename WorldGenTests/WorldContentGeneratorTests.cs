// WorldContentGeneratorTests.cs
// Copyright (c) 50PSoftware

using GameEngineTools.World.Data;
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

    /// <summary>Flat 1x1km tile at a fixed elevation — the simplest possible terrain, land everywhere.</summary>
    private static TerrainHeightmap FlatTile(double elevation, double originX = 0.0, double originY = 0.0, int size = 200, double cellSize = 5.0)
        => new("flat", originX, originY, cellSize, size, size, Enumerable.Repeat((float)elevation, size * size).ToArray());

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
        Assert.AreEqual(GameEngineTools.World.Location.TerrainType.Mountain, descriptor.Terrain);
        Assert.IsTrue(descriptor.Id.StartsWith("mountain_", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Generate_BelowMountainThreshold_ClassifiesAsForest()
    {
        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) };
        var options = new WorldContentGenerator.Options(Count: 1, MinDistanceMeters: 10.0);

        WorldContentGenerator.Generate(db, tiles, options, new Random(5), Catalog);

        var (descriptor, _) = db.GetAllLocations().Single();
        Assert.AreEqual(GameEngineTools.World.Location.TerrainType.Forest, descriptor.Terrain);
        Assert.IsTrue(descriptor.Id.StartsWith("forest_", StringComparison.Ordinal));
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
        var tiles = new[] { FlatTile(50.0, size: 300, cellSize: 5.0) };
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
        var options = new WorldContentGenerator.Options(Count: 3, MinDistanceMeters: 20.0);

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
            forest_only;Forest Only;Forest;Hunger;0.3;Food;50;600;true;10;;;;5;;
            mountain_only;Mountain Only;Mountain;Hunger;0.3;Food;50;600;true;10;;;;5;;
            any_water;Any Water;Any;Thirst;0.8;Drink;500;120;false;0;;;;80;;
            """);

        using var db = NewWorldDb();
        var tiles = new[] { FlatTile(50.0) }; // below threshold — classified Forest
        var options = new WorldContentGenerator.Options(Count: 3, MinDistanceMeters: 20.0);

        WorldContentGenerator.Generate(db, tiles, options, new Random(20), customCatalog);

        foreach (var (descriptor, _) in db.GetAllLocations())
        {
            var objectIds = db.GetAllObjectsAt(descriptor.Id).Select(o => o.Id).ToList();
            Assert.IsTrue(objectIds.Any(id => id.Contains("forest_only")), "Expected the Forest-tagged food template to be used on a Forest location.");
            Assert.IsFalse(objectIds.Any(id => id.Contains("mountain_only")), "Mountain-tagged template must not be used on a Forest location.");
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
    public void Generate_EmptyTileList_Throws()
    {
        using var db = NewWorldDb();
        var options = new WorldContentGenerator.Options(Count: 1);

        Assert.Throws<ArgumentException>(() =>
            WorldContentGenerator.Generate(db, [], options, new Random(12), Catalog));
    }
}
