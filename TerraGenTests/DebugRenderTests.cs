using GameEngineTools.World.Data;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class DebugRenderTests
{
    private static string TempDbPath() => Path.Combine(Path.GetTempPath(), $"terragen_test_{Guid.NewGuid():N}.db");
    private static string TempDirPath() => Path.Combine(Path.GetTempPath(), $"terragen_debug_{Guid.NewGuid():N}");

    [TestMethod]
    public void Run_WithDebugRenderDirectory_WritesExpectedLayerPngsForEveryChunk()
    {
        var dbPath = TempDbPath();
        var debugDir = TempDirPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var noiseParams = new PlanetNoise.Parameters(Seed: 9, AmplitudeMeters: 300.0, TectonicPlateCount: 6);
            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.008, LonMin: 0.0, LonMax: 0.008,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: noiseParams,
                ErosionParams: new TileErosion.Parameters(Seed: 9, DropletCount: 0),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters,
                SpimParams: new StreamPowerErosion.Parameters(Iterations: 10),
                RockTypeParams: new RockLayer.Parameters(Seed: 9),
                OrographicParams: new OrographicPrecipitation.Parameters(),
                SpimChunkTilesPerSide: 2, // small chunks so the test region spans more than one
                DebugRenderDirectory: debugDir);

            TileGenerator.Run(db, settings);

            var files = Directory.GetFiles(debugDir, "*.png").Select(Path.GetFileName).ToList();
            Assert.IsTrue(files.Count > 0, "Expected at least one debug PNG to be written.");

            // At least one chunk must have all six layers (rock-types and orographic are both on).
            Assert.IsTrue(files.Any(f => f!.Contains("1_landmass")), "Missing landmass layer.");
            Assert.IsTrue(files.Any(f => f!.Contains("2_uplift")), "Missing uplift layer.");
            Assert.IsTrue(files.Any(f => f!.Contains("3_rocktype")), "Missing rock-type layer.");
            Assert.IsTrue(files.Any(f => f!.Contains("4_precipitation")), "Missing precipitation layer.");
            Assert.IsTrue(files.Any(f => f!.Contains("5_elevation")), "Missing elevation layer.");
            Assert.IsTrue(files.Any(f => f!.Contains("6_accumulation_log")), "Missing accumulation layer.");

            // A real 16-bit grayscale PNG, not an empty/corrupt file.
            var samplePath = Path.Combine(debugDir, files.First(f => f!.Contains("5_elevation"))!);
            var bytes = File.ReadAllBytes(samplePath);
            Assert.IsTrue(bytes.Length > 100, "PNG file suspiciously small.");
            CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8], "Missing PNG signature.");
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (Directory.Exists(debugDir)) Directory.Delete(debugDir, recursive: true);
        }
    }

    [TestMethod]
    public void Run_WithSpimChunks_ReportsChunkProgressToCompletion()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var noiseParams = new PlanetNoise.Parameters(Seed: 9, AmplitudeMeters: 300.0, TectonicPlateCount: 6);
            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.008, LonMin: 0.0, LonMax: 0.008,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: noiseParams,
                ErosionParams: new TileErosion.Parameters(Seed: 9, DropletCount: 0),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters,
                SpimParams: new StreamPowerErosion.Parameters(Iterations: 10),
                SpimChunkTilesPerSide: 2); // several chunks across the test region

            var reports = new List<(int Done, int Total)>();
            TileGenerator.Run(db, settings, onSpimChunkProgress: (done, total) => reports.Add((done, total)));

            Assert.IsTrue(reports.Count > 1, "Expected more than one chunk progress report for a multi-chunk region.");
            Assert.IsTrue(reports.All(r => r.Total == reports[0].Total), "Total chunk count should stay constant across reports.");
            Assert.AreEqual(reports[0].Total, reports[^1].Done, "The last report should reach the total.");
            for (var i = 1; i < reports.Count; i++)
                Assert.AreEqual(reports[i - 1].Done + 1, reports[i].Done, "Done count should increase by exactly 1 per chunk.");
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void Run_WithSpimIterations_ReportsIterationProgressPerChunk()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var noiseParams = new PlanetNoise.Parameters(Seed: 9, AmplitudeMeters: 300.0, TectonicPlateCount: 6);
            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.008, LonMin: 0.0, LonMax: 0.008,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: noiseParams,
                ErosionParams: new TileErosion.Parameters(Seed: 9, DropletCount: 0),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters,
                SpimParams: new StreamPowerErosion.Parameters(Iterations: 6),
                SpimChunkTilesPerSide: 2); // several chunks, each running its own 6 iterations

            var reports = new List<(int Done, int Total)>();
            TileGenerator.Run(db, settings, onSpimIterationProgress: (done, total) => reports.Add((done, total)));

            Assert.IsTrue(reports.Count > 6, "Expected iteration reports from more than one chunk (6 iterations each).");
            Assert.IsTrue(reports.All(r => r.Total == 6), "Every report should carry this run's Iterations=6.");
            Assert.IsTrue(reports.Count(r => r.Done == 6) > 1, "Expected more than one chunk to reach its own final iteration.");
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void Run_WithoutDebugRenderDirectory_WritesNoFiles()
    {
        var dbPath = TempDbPath();
        var debugDir = TempDirPath(); // deliberately never passed to RunSettings
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var noiseParams = new PlanetNoise.Parameters(Seed: 9, AmplitudeMeters: 300.0, TectonicPlateCount: 6);
            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.002, LonMin: 0.0, LonMax: 0.002,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: noiseParams,
                ErosionParams: new TileErosion.Parameters(Seed: 9, DropletCount: 0),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters,
                SpimParams: new StreamPowerErosion.Parameters(Iterations: 10));

            TileGenerator.Run(db, settings);

            Assert.IsFalse(Directory.Exists(debugDir), "DebugRenderDirectory left null must never create or write to any directory.");
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
