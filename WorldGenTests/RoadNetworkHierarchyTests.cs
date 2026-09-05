// RoadNetworkHierarchyTests.cs
// Copyright (c) 50PSoftware

using GameEngineTools.World.Data;
using GameEngineTools.World.Location;
using WorldGen.Generation;

namespace WorldGenTests;

/// <summary>Tests for <see cref="WorldContentGenerator.ConnectNearestNeighbors"/>'s settlement-
/// hierarchy graph-grammar rules (Town MST backbone, Village-to-nearest-Town, Camp-to-nearest-
/// Village-or-Town, same-tier lateral links) — hand-builds <see cref="WorldContentGenerator.Placed"/>
/// lists directly (internal, see its own remarks) so tier composition is deterministic instead of
/// depending on a noise-driven <see cref="WorldContentGenerator.Generate"/> run to happen to
/// produce a particular mix.</summary>
[TestClass]
public class RoadNetworkHierarchyTests
{
    private static SqliteWorldDatabase NewWorldDb()
    {
        var db = new SqliteWorldDatabase(":memory:");
        WorldDatabaseSeeder.InitializeSchemaOnly(db);
        return db;
    }

    private static TerrainHeightmap FlatTile(int size = 50, double cellSize = 10.0) =>
        new("flat", -1000.0, -1000.0, cellSize, size, size, Enumerable.Repeat(50f, size * size).ToArray());

    private static WorldContentGenerator.Placed Place(string id, double x, double y, WorldContentGenerator.SettlementTier tier, TerrainHeightmap tile) =>
        new(id, x, y, tile, tier);

    /// <summary>InsertConnection has a foreign-key dependency on Locations already existing —
    /// tests that call <see cref="WorldContentGenerator.ConnectNearestNeighbors"/> directly (skipping
    /// the rest of <see cref="WorldContentGenerator.Generate"/>) must insert a minimal Location row
    /// for each <see cref="WorldContentGenerator.Placed"/> themselves first.</summary>
    private static void InsertMinimalLocations(SqliteWorldDatabase db, IEnumerable<WorldContentGenerator.Placed> placed)
    {
        foreach (var p in placed)
        {
            db.InsertLocation(new LocationDescriptor(
                Id: p.Id, DisplayName: p.Id, BaseNoise: 0.1, NoisePerPerson: 0.02, Capacity: 10,
                AllowsPrivacy: false, Type: LocationType.Public, Terrain: TerrainType.Forest,
                DangerLevel: 0.0, AllowsPickup: true, NormId: null, X: p.X, Y: p.Y, AltitudeMeters: 50.0),
                "Test");
        }
    }

    [TestMethod]
    public void ConnectNearestNeighbors_FiveColinearTowns_FormsExactSpanningTreeOfFourEdges()
    {
        using var db = NewWorldDb();
        var tile = FlatTile();
        // Evenly spaced on a line — MST is unambiguously the chain, and each town's own nearest
        // same-tier neighbor (the Rule 4 lateral-link pass) always coincides with a chain edge
        // already made, so no extra edges sneak in: exactly 4 = n-1 distinct connections.
        var placed = new List<WorldContentGenerator.Placed>
        {
            Place("town0", 0, 0, WorldContentGenerator.SettlementTier.Town, tile),
            Place("town1", 100, 0, WorldContentGenerator.SettlementTier.Town, tile),
            Place("town2", 200, 0, WorldContentGenerator.SettlementTier.Town, tile),
            Place("town3", 300, 0, WorldContentGenerator.SettlementTier.Town, tile),
            Place("town4", 400, 0, WorldContentGenerator.SettlementTier.Town, tile),
        };
        var options = new WorldContentGenerator.Options(Count: 5, ConnectionsPerLocation: 1);

        InsertMinimalLocations(db, placed);
        WorldContentGenerator.ConnectNearestNeighbors(db, placed, options);

        var connections = db.GetAllConnections();
        var distinctPairs = connections
            .Select(c => string.CompareOrdinal(c.FromId, c.ToId) <= 0 ? $"{c.FromId}|{c.ToId}" : $"{c.ToId}|{c.FromId}")
            .ToHashSet();
        Assert.AreEqual(4, distinctPairs.Count, $"Expected exactly 4 edges (a spanning tree over 5 towns), got: {string.Join(", ", distinctPairs)}");
    }

    [TestMethod]
    public void ConnectNearestNeighbors_TownMst_ConnectsEveryTownToEveryOtherTown_Transitively()
    {
        using var db = NewWorldDb();
        var tile = FlatTile();
        // An "L" shape, not a line — still must end up fully connected via the tree.
        var placed = new List<WorldContentGenerator.Placed>
        {
            Place("townA", 0, 0, WorldContentGenerator.SettlementTier.Town, tile),
            Place("townB", 100, 0, WorldContentGenerator.SettlementTier.Town, tile),
            Place("townC", 100, 100, WorldContentGenerator.SettlementTier.Town, tile),
            Place("townD", 100, 200, WorldContentGenerator.SettlementTier.Town, tile),
        };
        var options = new WorldContentGenerator.Options(Count: 4, ConnectionsPerLocation: 1);

        InsertMinimalLocations(db, placed);
        WorldContentGenerator.ConnectNearestNeighbors(db, placed, options);

        var adjacency = placed.ToDictionary(p => p.Id, _ => new HashSet<string>());
        foreach (var (from, to, _) in db.GetAllConnections())
            adjacency[from].Add(to);

        // BFS from townA — every other town must be reachable.
        var visited = new HashSet<string> { "townA" };
        var queue = new Queue<string>();
        queue.Enqueue("townA");
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in adjacency[current])
                if (visited.Add(next)) queue.Enqueue(next);
        }

        Assert.AreEqual(placed.Count, visited.Count, "Every town must be reachable from every other town through the backbone.");
    }

    [TestMethod]
    public void ConnectNearestNeighbors_EachVillage_ConnectsToTheOnlyTown()
    {
        using var db = NewWorldDb();
        var tile = FlatTile();
        var placed = new List<WorldContentGenerator.Placed>
        {
            Place("town", 0, 0, WorldContentGenerator.SettlementTier.Town, tile),
            Place("villageA", 500, 0, WorldContentGenerator.SettlementTier.Village, tile),
            Place("villageB", -500, 0, WorldContentGenerator.SettlementTier.Village, tile),
            Place("villageC", 0, 500, WorldContentGenerator.SettlementTier.Village, tile),
        };
        var options = new WorldContentGenerator.Options(Count: 4, ConnectionsPerLocation: 1);

        InsertMinimalLocations(db, placed);
        WorldContentGenerator.ConnectNearestNeighbors(db, placed, options);

        var connections = db.GetAllConnections();
        foreach (var villageId in new[] { "villageA", "villageB", "villageC" })
        {
            Assert.IsTrue(connections.Any(c => c.FromId == villageId && c.ToId == "town"),
                $"Expected {villageId} to have a connection to the (only) town.");
        }
    }

    [TestMethod]
    public void ConnectNearestNeighbors_Camp_ConnectsToNearerVillage_NotFartherTown()
    {
        using var db = NewWorldDb();
        var tile = FlatTile();
        var placed = new List<WorldContentGenerator.Placed>
        {
            Place("town", 10_000, 0, WorldContentGenerator.SettlementTier.Town, tile),
            Place("village", 100, 0, WorldContentGenerator.SettlementTier.Village, tile), // much closer to the camp
            Place("camp", 0, 0, WorldContentGenerator.SettlementTier.Camp, tile),
        };
        var options = new WorldContentGenerator.Options(Count: 3, ConnectionsPerLocation: 1);

        InsertMinimalLocations(db, placed);
        WorldContentGenerator.ConnectNearestNeighbors(db, placed, options);

        var connections = db.GetAllConnections();
        Assert.IsTrue(connections.Any(c => c.FromId == "camp" && c.ToId == "village"),
            "Expected the camp to connect to its nearer village hub.");
        Assert.IsFalse(connections.Any(c => c.FromId == "camp" && c.ToId == "town"),
            "The camp should not connect directly to the far-away town when a much closer village exists.");
    }

    [TestMethod]
    public void ConnectNearestNeighbors_CampWithNoVillages_ConnectsToTheTown()
    {
        using var db = NewWorldDb();
        var tile = FlatTile();
        var placed = new List<WorldContentGenerator.Placed>
        {
            Place("town", 1000, 0, WorldContentGenerator.SettlementTier.Town, tile),
            Place("camp", 0, 0, WorldContentGenerator.SettlementTier.Camp, tile),
        };
        var options = new WorldContentGenerator.Options(Count: 2, ConnectionsPerLocation: 1);

        InsertMinimalLocations(db, placed);
        WorldContentGenerator.ConnectNearestNeighbors(db, placed, options);

        var connections = db.GetAllConnections();
        Assert.IsTrue(connections.Any(c => c.FromId == "camp" && c.ToId == "town"),
            "With no villages available, the camp should fall back to connecting to the town.");
    }

    [TestMethod]
    public void ConnectNearestNeighbors_ZeroConnectionsPerLocation_CreatesNoHierarchyEdgesEither()
    {
        using var db = NewWorldDb();
        var tile = FlatTile();
        var placed = new List<WorldContentGenerator.Placed>
        {
            Place("town", 0, 0, WorldContentGenerator.SettlementTier.Town, tile),
            Place("village", 100, 0, WorldContentGenerator.SettlementTier.Village, tile),
            Place("camp", 200, 0, WorldContentGenerator.SettlementTier.Camp, tile),
        };
        var options = new WorldContentGenerator.Options(Count: 3, ConnectionsPerLocation: 0);

        InsertMinimalLocations(db, placed);
        var created = WorldContentGenerator.ConnectNearestNeighbors(db, placed, options);

        Assert.AreEqual(0, created);
        Assert.AreEqual(0, db.GetAllConnections().Count);
    }

    [TestMethod]
    public void ConnectNearestNeighbors_SingleTown_DoesNotThrow()
    {
        using var db = NewWorldDb();
        var tile = FlatTile();
        var placed = new List<WorldContentGenerator.Placed> { Place("onlyTown", 0, 0, WorldContentGenerator.SettlementTier.Town, tile) };
        var options = new WorldContentGenerator.Options(Count: 1, ConnectionsPerLocation: 2);

        InsertMinimalLocations(db, placed);
        var created = WorldContentGenerator.ConnectNearestNeighbors(db, placed, options);

        Assert.AreEqual(0, created);
    }
}
