using GameEngineTools.World.Data;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class TileGeneratorRiversOnlyTests
{
    private static string TempDbPath() => Path.Combine(Path.GetTempPath(), $"terragen_test_{Guid.NewGuid():N}.db");

    private static TileGenerator.RunSettings Settings(bool riversOnly) => new(
        LatMin: -37.9, LatMax: -37.82, LonMin: 147.9, LonMax: 147.98,
        TileSizeMeters: 2000.0, CellSizeMeters: 50.0,
        NoiseParams: new PlanetNoise.Parameters(Seed: 5, AmplitudeMeters: 400.0),
        ErosionParams: new TileErosion.Parameters(Seed: 5, DropletCount: 2000),
        PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters,
        HydrologyParams: new TileHydrology.Parameters(2000.0),
        RiversOnly: riversOnly);

    [TestMethod]
    public void RiversOnly_KeepsTerrainByteIdentical_AndReplacesNetworksInTheRegion()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);
            var first = TileGenerator.Run(db, Settings(false));
            var before = first.ToDictionary(r => r.Id, r => (float[])db.LoadHeightmap(r.Id)!.Values.Clone());

            // A stale network from a differently-gridded earlier run, sitting inside the region.
            var someTile = db.LoadHeightmap(first[0].Id)!;
            var staleNode = new RiverNode("stale_n0", "stale_net", someTile.OriginX + 100, someTile.OriginY + 100, RiverNodeKind.Source);
            db.SaveRiverNetwork(new RiverNetwork("stale_net", new[] { staleNode }, new List<RiverReach>(), new List<OxbowLoop>()));

            TileGenerator.Run(db, Settings(true));

            foreach (var (id, values) in before)
                CollectionAssert.AreEqual(values, db.LoadHeightmap(id)!.Values, $"Tile {id} changed in --rivers-only.");
            Assert.AreEqual(0, db.LoadRiverNetwork("stale_net").Nodes.Count, "The stale network inside the region should have been replaced.");
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void RiversOnly_WithMissingTiles_FailsWithAClearMessage()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var ex = Assert.Throws<InvalidOperationException>(() => TileGenerator.Run(db, Settings(true)));

            StringAssert.Contains(ex.Message, "--rivers-only");
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
