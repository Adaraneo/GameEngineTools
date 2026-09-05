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
    public void LoadHeightmap_CalledTwice_ReturnsSameCachedInstance()
    {
        var dbPath = Path.Combine(_tempDir, "cache_world.db");
        using var svc = new WorldDatabaseService();
        svc.OpenBlank(dbPath);
        svc.SaveHeightmap(new GameEngineTools.World.Data.TerrainHeightmap(
            "tile_a", 0.0, 0.0, 2.5, 4, 4, new float[16]));

        var first = svc.LoadHeightmap("tile_a");
        var second = svc.LoadHeightmap("tile_a");

        Assert.IsNotNull(first);
        Assert.AreSame(first, second, "Second LoadHeightmap should return the cached instance, not decode the BLOB again.");
    }

    [TestMethod]
    public void SaveHeightmap_UpdatesCache_SoSubsequentLoadReflectsNewValues()
    {
        var dbPath = Path.Combine(_tempDir, "cache_update_world.db");
        using var svc = new WorldDatabaseService();
        svc.OpenBlank(dbPath);

        var v1 = new GameEngineTools.World.Data.TerrainHeightmap("tile_a", 0.0, 0.0, 2.5, 2, 2, [1f, 1f, 1f, 1f]);
        svc.SaveHeightmap(v1);
        Assert.AreEqual(1f, svc.LoadHeightmap("tile_a")!.Values[0]);

        var v2 = v1 with { Values = [2f, 2f, 2f, 2f] };
        svc.SaveHeightmap(v2);

        var reloaded = svc.LoadHeightmap("tile_a");
        Assert.AreEqual(2f, reloaded!.Values[0], "Cache must reflect the newly saved values, not the stale first-loaded ones.");
    }

    [TestMethod]
    public void Close_ClearsCache_SoReopenedDatabaseReadsFreshFromDisk()
    {
        var dbPath = Path.Combine(_tempDir, "cache_close_world.db");

        using (var svc = new WorldDatabaseService())
        {
            svc.OpenBlank(dbPath);
            svc.SaveHeightmap(new GameEngineTools.World.Data.TerrainHeightmap("tile_a", 0.0, 0.0, 2.5, 2, 2, [1f, 1f, 1f, 1f]));
            svc.LoadHeightmap("tile_a"); // populate the cache
        } // Dispose -> Close() -> cache cleared

        // Modify the tile directly on disk, bypassing WorldDatabaseService entirely, to prove a
        // fresh Open() doesn't serve a stale cached instance from the previous session.
        var terrainDbPath = Path.Combine(Path.GetDirectoryName(dbPath)!, "terrain.db");
        using (var raw = new GameEngineTools.World.Data.SqliteWorldDatabase(terrainDbPath))
        {
            raw.SaveHeightmap(new GameEngineTools.World.Data.TerrainHeightmap("tile_a", 0.0, 0.0, 2.5, 2, 2, [9f, 9f, 9f, 9f]));
        }

        using var reopened = new WorldDatabaseService();
        reopened.OpenBlank(dbPath);
        var loaded = reopened.LoadHeightmap("tile_a");

        Assert.AreEqual(9f, loaded!.Values[0]);
    }

    [TestMethod]
    public void LoadHeightmap_ConcurrentCallsFromMultipleThreads_DoNotThrowOrCorruptTheCache()
    {
        // MainWindow's continuous-tile-panning feature loads/stitches tiles on a background thread
        // while the UI thread can still call LoadHeightmap/SaveHeightmap at the same time — this
        // exercises exactly that shape of concurrent access against the (now-locked) cache.
        var dbPath = Path.Combine(_tempDir, "concurrent_world.db");
        using var svc = new WorldDatabaseService();
        svc.OpenBlank(dbPath);

        var ids = Enumerable.Range(0, 8).Select(i => $"tile_{i}").ToList();
        foreach (var id in ids)
            svc.SaveHeightmap(new GameEngineTools.World.Data.TerrainHeightmap(id, 0.0, 0.0, 2.5, 2, 2, [1f, 1f, 1f, 1f]));

        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        Parallel.For(0, 200, i =>
        {
            try
            {
                var id = ids[i % ids.Count];
                svc.LoadHeightmap(id);
                if (i % 20 == 0)
                    svc.SaveHeightmap(new GameEngineTools.World.Data.TerrainHeightmap(id, 0.0, 0.0, 2.5, 2, 2, [2f, 2f, 2f, 2f]));
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.AreEqual(0, exceptions.Count, string.Join("; ", exceptions.Select(e => e.Message)));
    }

    [TestMethod]
    public void OpenTerrainOnly_NoWorldDbCreated_HeightmapStillWorks()
    {
        var terrainPath = Path.Combine(_tempDir, "standalone_terrain.db");

        using var svc = new WorldDatabaseService();
        svc.OpenTerrainOnly(terrainPath);

        Assert.IsTrue(svc.IsOpen);
        Assert.IsTrue(svc.IsTerrainOnly);
        Assert.IsFalse(File.Exists(Path.Combine(_tempDir, "world.db")),
            "OpenTerrainOnly must not create a paired world.db anywhere.");

        var grid = new GameEngineTools.World.Data.TerrainHeightmap(
            "default", OriginX: 0.0, OriginY: 0.0, CellSizeMeters: 5.0,
            Width: 2, Height: 2, Values: [1f, 2f, 3f, 4f]);
        svc.SaveHeightmap(grid);

        var loaded = svc.LoadHeightmap();
        Assert.IsNotNull(loaded);
        CollectionAssert.AreEqual(grid.Values, loaded!.Values);
    }

    [TestMethod]
    public void OpenTerrainOnly_LocationOperations_Throw()
    {
        var terrainPath = Path.Combine(_tempDir, "standalone_terrain2.db");

        using var svc = new WorldDatabaseService();
        svc.OpenTerrainOnly(terrainPath);

        Assert.Throws<InvalidOperationException>(() => svc.GetLocations());
        Assert.Throws<InvalidOperationException>(() =>
            svc.InsertLocation(new LocationInfo("x", "X", "", 0, 0, 0)));
        Assert.Throws<InvalidOperationException>(() => svc.ExportSeedSql());
    }
}
