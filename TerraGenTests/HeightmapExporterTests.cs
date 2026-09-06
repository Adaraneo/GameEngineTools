using GameEngineTools.World.Data;
using System.Text.Json;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class HeightmapExporterTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"heightmap_export_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static TerrainHeightmap SampleTile()
    {
        const int width = 5, height = 4;
        var values = new float[width * height];
        for (var i = 0; i < values.Length; i++) values[i] = i * 2.5f - 10f; // spans negative to positive
        return new TerrainHeightmap("tile_test_1_1", OriginX: 100.0, OriginY: 200.0, CellSizeMeters: 2.5, width, height, values);
    }

    [TestMethod]
    public void Export_RawFile_ExactlyMatchesTerrainHeightmapToBytes()
    {
        var dir = TempDir();
        try
        {
            var tile = SampleTile();
            var result = HeightmapExporter.Export(tile, dir, "Test Planet", 42);

            var rawBytes = File.ReadAllBytes(result.RawPath);
            CollectionAssert.AreEqual(tile.ToBytes(), rawBytes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Export_CreatesAllThreeFiles_NamedAfterTheTileId()
    {
        var dir = TempDir();
        try
        {
            var tile = SampleTile();
            var result = HeightmapExporter.Export(tile, dir, "Test Planet", 42);

            Assert.IsTrue(File.Exists(result.RawPath));
            Assert.IsTrue(File.Exists(result.PngPath));
            Assert.IsTrue(File.Exists(result.MetadataPath));
            Assert.AreEqual("tile_test_1_1.f32", Path.GetFileName(result.RawPath));
            Assert.AreEqual("tile_test_1_1.png", Path.GetFileName(result.PngPath));
            Assert.AreEqual("tile_test_1_1.json", Path.GetFileName(result.MetadataPath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Export_Metadata_MatchesTileGeometryAndElevationRange()
    {
        var dir = TempDir();
        try
        {
            var tile = SampleTile();
            var result = HeightmapExporter.Export(tile, dir, "Test Planet", 42);

            var metadata = JsonSerializer.Deserialize<HeightmapExporter.Metadata>(File.ReadAllText(result.MetadataPath))!;

            Assert.AreEqual(tile.Id, metadata.TileId);
            Assert.AreEqual("Test Planet", metadata.PlanetName);
            Assert.AreEqual(42, metadata.Seed);
            Assert.AreEqual(tile.OriginX, metadata.OriginXMeters);
            Assert.AreEqual(tile.OriginY, metadata.OriginYMeters);
            Assert.AreEqual(tile.CellSizeMeters, metadata.CellSizeMeters);
            Assert.AreEqual(tile.Width, metadata.Width);
            Assert.AreEqual(tile.Height, metadata.Height);
            Assert.AreEqual(tile.Values.Min(), metadata.ElevationMinMeters, 1e-6);
            Assert.AreEqual(tile.Values.Max(), metadata.ElevationMaxMeters, 1e-6);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Export_CreatesOutputDirectory_WhenItDoesNotExistYet()
    {
        var parent = TempDir();
        var nested = Path.Combine(parent, "nested", "deeper");
        try
        {
            var tile = SampleTile();
            HeightmapExporter.Export(tile, nested, "Test Planet", 42);

            Assert.IsTrue(Directory.Exists(nested));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    #region ExportRange (batch lat/lon export)

    private const double PlanetRadiusMeters = 6_378_100.0;
    private const double CellSize = 10.0;
    private const int TileSide = 4;

    private static TerrainHeightmap MakeTile(string id, double originX, double originY, float fill)
        => new(id, originX, originY, CellSize, TileSide, TileSide, Enumerable.Repeat(fill, TileSide * TileSide).ToArray());

    [TestMethod]
    public void ExportRange_OnlyStitchesTilesOverlappingTheWindow_NotDistantOnes()
    {
        var (inX, inY) = PlanetNoise.LatLonToOffset(0.005, 0.005, 0.0, 0.0, PlanetRadiusMeters);
        var (outX, outY) = PlanetNoise.LatLonToOffset(10.0, 10.0, 0.0, 0.0, PlanetRadiusMeters);
        var summaries = new List<TerrainHeightmapSummary>
        {
            new("tileIn", inX, inY, CellSize, TileSide, TileSide),
            new("tileOut", outX, outY, CellSize, TileSide, TileSide),
        };
        TerrainHeightmap? LoadTile(string id) => id switch
        {
            "tileIn" => MakeTile("tileIn", inX, inY, 1f),
            "tileOut" => MakeTile("tileOut", outX, outY, 2f),
            _ => null,
        };

        var dir = TempDir();
        try
        {
            var result = HeightmapExporter.ExportRange(summaries, LoadTile,
                latMin: 0.0, latMax: 0.01, lonMin: 0.0, lonMax: 0.01, PlanetRadiusMeters, dir, "Test Planet", 42);

            Assert.IsNotNull(result, "Expected tileIn to overlap the requested window.");
            Assert.AreEqual(1, Directory.GetFiles(dir, "*.f32").Length, "Expected exactly one combined batch file, not one per tile.");

            var rawBytes = File.ReadAllBytes(result!.RawPath);
            var floats = new float[rawBytes.Length / sizeof(float)];
            Buffer.BlockCopy(rawBytes, 0, floats, 0, rawBytes.Length);
            Assert.IsFalse(floats.Any(v => v == 2f), "The distant, non-overlapping tile's data must not appear in the combined export.");
            Assert.IsTrue(floats.All(v => v == 1f), "Every sample should come from the overlapping tile.");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void ExportRange_NoTileOverlapsTheWindow_ReturnsNull()
    {
        var (farX, farY) = PlanetNoise.LatLonToOffset(10.0, 10.0, 0.0, 0.0, PlanetRadiusMeters);
        var summaries = new List<TerrainHeightmapSummary> { new("tileFar", farX, farY, CellSize, TileSide, TileSide) };

        var dir = TempDir();
        try
        {
            var result = HeightmapExporter.ExportRange(summaries, id => MakeTile(id, farX, farY, 1f),
                latMin: 0.0, latMax: 0.01, lonMin: 0.0, lonMax: 0.01, PlanetRadiusMeters, dir, "Test Planet", 42);

            Assert.IsNull(result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void ComputeCoverage_ReturnsLatLonBoundingBoxOfAllTiles()
    {
        var (nearX, nearY) = PlanetNoise.LatLonToOffset(1.0, 1.0, 0.0, 0.0, PlanetRadiusMeters);
        var (farX, farY) = PlanetNoise.LatLonToOffset(-5.0, 8.0, 0.0, 0.0, PlanetRadiusMeters);
        var summaries = new List<TerrainHeightmapSummary>
        {
            new("tileNear", nearX, nearY, CellSize, TileSide, TileSide),
            new("tileFar", farX, farY, CellSize, TileSide, TileSide),
        };

        var (latMin, latMax, lonMin, lonMax) = HeightmapExporter.ComputeCoverage(summaries, PlanetRadiusMeters);

        Assert.IsTrue(latMin <= -5.0, $"Expected coverage to reach down to the far tile's latitude, got latMin={latMin}.");
        Assert.IsTrue(latMax >= 1.0, $"Expected coverage to reach up to the near tile's latitude, got latMax={latMax}.");
        Assert.IsTrue(lonMin <= 1.0, $"Expected coverage to reach the near tile's longitude, got lonMin={lonMin}.");
        Assert.IsTrue(lonMax >= 8.0, $"Expected coverage to reach the far tile's longitude, got lonMax={lonMax}.");
    }

    #endregion ExportRange (batch lat/lon export)
}
