using Microsoft.VisualStudio.TestTools.UnitTesting;
using TerrainEditor.Models;
using TerrainEditor.Services;

namespace TerrainEditorTests;

[TestClass]
public class WorldDatabaseServicePersistenceTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TerrainEditorTests_Db_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void InsertLocation_SurvivesCloseAndReopenOfTheSameFile()
    {
        var dbPath = Path.Combine(_tempDir, "world.db");

        using (var svc = new WorldDatabaseService())
        {
            svc.Open(dbPath);
            svc.InsertLocation(new LocationInfo("test_loc", "Test Location", "Lowlands", 12.5, 34.5, 5.0));
        } // Dispose -> Close() -> underlying SqliteConnection disposed

        using var reopened = new WorldDatabaseService();
        reopened.Open(dbPath);
        var locations = reopened.GetLocations();

        var found = locations.SingleOrDefault(l => l.Id == "test_loc");
        Assert.IsNotNull(found, "The newly inserted location must survive a close+reopen of the same db file.");
        Assert.AreEqual("Test Location", found.DisplayName);
        Assert.AreEqual(12.5, found.X, 1e-9);
        Assert.AreEqual(34.5, found.Y, 1e-9);
    }

    [TestMethod]
    public void InsertLocation_SurvivesWithoutExplicitClose_ReopeningTheSameServiceInstance()
    {
        // Mirrors what MainWindow actually does: WorldDatabaseService is a long-lived singleton,
        // and "reopening" a db means calling Open() again on the SAME instance (which internally
        // Close()s the previous connection first) — not necessarily disposing/recreating the service.
        var dbPath = Path.Combine(_tempDir, "world.db");

        using var svc = new WorldDatabaseService();
        svc.Open(dbPath);
        svc.InsertLocation(new LocationInfo("test_loc2", "Test Location 2", "Coast", 1.0, 2.0, 3.0));

        svc.Open(dbPath); // re-open the same path on the same service instance

        var locations = svc.GetLocations();
        var found = locations.SingleOrDefault(l => l.Id == "test_loc2");
        Assert.IsNotNull(found, "The newly inserted location must survive re-opening the same path on the same service instance.");
    }

    [TestMethod]
    public void OpenBlank_FreshPath_StartsWithNoLocations()
    {
        var dbPath = Path.Combine(_tempDir, "blank_world.db");

        using var svc = new WorldDatabaseService();
        svc.OpenBlank(dbPath);

        Assert.AreEqual(0, svc.GetLocations().Count,
            "OpenBlank() must not pull in the built-in seed_data.sql locations — 'New World' should start empty.");
        Assert.AreEqual(0, svc.GetConnections().Count);
        Assert.IsNull(svc.LoadHeightmap());
    }

    [TestMethod]
    public void Open_FreshPath_SeedsNoLocations()
    {
        // The default seed_data.sql no longer ships any locations (see WorldDatabaseSeeder.Initialize) —
        // a brand-new path opened via Open() starts just as empty as one opened via OpenBlank().
        var dbPath = Path.Combine(_tempDir, "seeded_world.db");

        using var svc = new WorldDatabaseService();
        svc.Open(dbPath);

        Assert.AreEqual(0, svc.GetLocations().Count,
            "Open() on a fresh path should start with no locations — the default seed no longer ships any.");
    }

    [TestMethod]
    public void OpenBlank_ThenAddAndSaveLocation_Persists()
    {
        var dbPath = Path.Combine(_tempDir, "authored_world.db");

        using var svc = new WorldDatabaseService();
        svc.OpenBlank(dbPath);
        svc.InsertLocation(new LocationInfo("first_place", "First Place", "", 5.0, 6.0, 0.0));

        var locations = svc.GetLocations();
        Assert.AreEqual(1, locations.Count);
        Assert.AreEqual("first_place", locations[0].Id);
    }

    [TestMethod]
    public void InsertConnection_SurvivesCloseAndReopenOfTheSameFile()
    {
        var dbPath = Path.Combine(_tempDir, "connected_world.db");

        using (var svc = new WorldDatabaseService())
        {
            svc.OpenBlank(dbPath);
            // Connections has a foreign key on Locations — both endpoints must exist first.
            svc.InsertLocation(new LocationInfo("a", "A", "", 0, 0, 0));
            svc.InsertLocation(new LocationInfo("b", "B", "", 100, 0, 0));

            svc.InsertConnection("a", "b", 100.0);
            svc.InsertConnection("b", "a", 100.0);
        }

        using var reopened = new WorldDatabaseService();
        reopened.Open(dbPath);
        var connections = reopened.GetConnections();

        Assert.AreEqual(2, connections.Count, "Both directions of the connection must survive a close+reopen.");
        Assert.IsTrue(connections.Any(c => c.FromId == "a" && c.ToId == "b" && Math.Abs(c.DistanceMeters - 100.0) < 1e-9));
        Assert.IsTrue(connections.Any(c => c.FromId == "b" && c.ToId == "a" && Math.Abs(c.DistanceMeters - 100.0) < 1e-9));
    }

    [TestMethod]
    public void UpdateLocationDetails_ChangesNameAndSocialFields_PersistsAcrossReopen()
    {
        var dbPath = Path.Combine(_tempDir, "edited_world.db");

        using (var svc = new WorldDatabaseService())
        {
            svc.OpenBlank(dbPath);
            svc.InsertLocation(new LocationInfo("place", "Original Name", "", 0, 0, 0));

            var edited = new LocationInfo("place", "Renamed", "", 0, 0, 0,
                GameEngineTools.World.Location.LocationType.Work,
                GameEngineTools.World.Location.TerrainType.Forest,
                0.6, 0.1, 5, true);
            var updated = svc.UpdateLocationDetails(edited);
            Assert.IsTrue(updated, "UpdateLocationDetails must report a row was updated.");
        }

        using var reopened = new WorldDatabaseService();
        reopened.Open(dbPath);
        var found = reopened.GetLocations().Single(l => l.Id == "place");

        Assert.AreEqual("Renamed", found.DisplayName);
        Assert.AreEqual(GameEngineTools.World.Location.LocationType.Work, found.Type);
        Assert.AreEqual(GameEngineTools.World.Location.TerrainType.Forest, found.Terrain);
        Assert.AreEqual(0.6, found.BaseNoise, 1e-9);
        Assert.AreEqual(0.1, found.NoisePerPerson, 1e-9);
        Assert.AreEqual(5, found.Capacity);
        Assert.IsTrue(found.AllowsPrivacy);
    }

    [TestMethod]
    public void ListHeightmaps_ReturnsMetadataForEveryTileWithoutLoadingFullData()
    {
        var dbPath = Path.Combine(_tempDir, "tiles_world.db");

        using var svc = new WorldDatabaseService();
        svc.OpenBlank(dbPath);

        var tileA = new GameEngineTools.World.Data.TerrainHeightmap(
            "tile_1_2_3", OriginX: 100.0, OriginY: 200.0, CellSizeMeters: 2.5,
            Width: 4, Height: 4, Values: new float[16]);
        var tileB = new GameEngineTools.World.Data.TerrainHeightmap(
            "tile_1_2_4", OriginX: 1100.0, OriginY: 200.0, CellSizeMeters: 2.5,
            Width: 4, Height: 4, Values: new float[16]);
        svc.SaveHeightmap(tileA);
        svc.SaveHeightmap(tileB);

        var summaries = svc.ListHeightmaps();

        Assert.AreEqual(2, summaries.Count);
        var a = summaries.Single(s => s.Id == "tile_1_2_3");
        Assert.AreEqual(100.0, a.OriginX, 1e-9);
        Assert.AreEqual(200.0, a.OriginY, 1e-9);
        Assert.AreEqual(4, a.Width);
        Assert.AreEqual(4, a.Height);

        var loaded = svc.LoadHeightmap("tile_1_2_4");
        Assert.IsNotNull(loaded);
        Assert.AreEqual(1100.0, loaded!.OriginX, 1e-9);
        Assert.IsNull(svc.LoadHeightmap("tile_does_not_exist"));
    }

    [TestMethod]
    public void Open_LegacyFileWithTerrainHeightmapTable_MigratesIntoSiblingTerrainDb()
    {
        // Simulates a world.db saved before terrain moved to its own database: the file still
        // physically has a TerrainHeightmap table (schema.sql no longer creates one, but an
        // existing file on disk isn't retroactively altered).
        var dbPath = Path.Combine(_tempDir, "legacy_world.db");

        using (var db = new GameEngineTools.World.Data.SqliteWorldDatabase(dbPath))
        {
            GameEngineTools.World.Data.WorldDatabaseSeeder.InitializeSchemaOnly(db);
            db.ExecuteScript("""
                CREATE TABLE TerrainHeightmap (
                    Id TEXT PRIMARY KEY, OriginX REAL NOT NULL, OriginY REAL NOT NULL,
                    CellSizeMeters REAL NOT NULL, Width INTEGER NOT NULL, Height INTEGER NOT NULL,
                    Data BLOB NOT NULL, RiverMask BLOB
                );
                """);
            db.SaveHeightmap(new GameEngineTools.World.Data.TerrainHeightmap(
                "default", OriginX: 10.0, OriginY: 20.0, CellSizeMeters: 5.0,
                Width: 2, Height: 2, Values: [1f, 2f, 3f, 4f]));
        }

        using var svc = new WorldDatabaseService();
        svc.Open(dbPath);

        var migrated = svc.LoadHeightmap();
        Assert.IsNotNull(migrated, "The legacy heightmap must be migrated into the sibling terrain.db.");
        CollectionAssert.AreEqual(new float[] { 1f, 2f, 3f, 4f }, migrated!.Values);

        using (var db = new GameEngineTools.World.Data.SqliteWorldDatabase(dbPath))
        {
            Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => db.LoadHeightmap("default"),
                "TerrainHeightmap must be dropped from world.db after a successful migration.");
        }
    }
}
