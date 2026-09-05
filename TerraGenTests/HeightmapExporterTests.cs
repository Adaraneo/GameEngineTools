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
}
