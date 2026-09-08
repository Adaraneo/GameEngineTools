using GameEngineTools.World.Data;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class TileGeneratorChunkedSpimTests
{
    private static string TempDbPath() => Path.Combine(Path.GetTempPath(), $"terragen_test_{Guid.NewGuid():N}.db");

    // Stitches every generated tile back into one big row-major elevation array, in world order,
    // so boundary-column jumps can be measured across the whole region regardless of how many
    // tiles/chunks it took to generate.
    private static (float[] Values, int Width, int Height, int CellsPerTile) StitchRegion(
        SqliteWorldDatabase db, IReadOnlyList<TileGenerator.TileResult> results, int cellsPerTile)
    {
        var maxRow = results.Max(r => r.Row);
        var maxCol = results.Max(r => r.Col);
        var width = (maxCol + 1) * cellsPerTile;
        var height = (maxRow + 1) * cellsPerTile;
        var combined = new float[width * height];

        foreach (var r in results)
        {
            var tile = db.LoadHeightmap(r.Id)!;
            var baseX = r.Col * cellsPerTile;
            var baseY = r.Row * cellsPerTile;
            for (var iy = 0; iy < cellsPerTile; iy++)
                Array.Copy(tile.Values, iy * cellsPerTile, combined, (baseY + iy) * width + baseX, cellsPerTile);
        }

        return (combined, width, height, cellsPerTile);
    }

    // For every internal tile-boundary column, compares the average |delta| RIGHT AT the boundary
    // against the average |delta| one column further in (ordinary local roughness) -- a truncated
    // per-tile drainage solve should show an excess jump at the boundary that a whole-chunk solve
    // should not.
    private static double MeanBoundaryExcessJump(float[] values, int width, int height, int cellsPerTile)
    {
        var boundaryJumps = new List<double>();
        var interiorJumps = new List<double>();

        for (var x = cellsPerTile; x < width; x += cellsPerTile)
        {
            for (var y = 0; y < height; y++)
            {
                boundaryJumps.Add(Math.Abs(values[y * width + x] - values[y * width + (x - 1)]));
                if (x + 1 < width)
                    interiorJumps.Add(Math.Abs(values[y * width + (x + 1)] - values[y * width + x]));
            }
        }

        return boundaryJumps.Average() - interiorJumps.Average();
    }

    [TestMethod]
    public void Run_ChunkedSpim_ReducesTileBoundaryDiscontinuityVsPerTileSpim()
    {
        var dbPathPerTile = TempDbPath();
        var dbPathChunked = TempDbPath();
        try
        {
            var noiseParams = new PlanetNoise.Parameters(Seed: 77, AmplitudeMeters: 400.0, TectonicPlateCount: 4);
            var baseSettings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.014, LonMin: 0.0, LonMax: 0.014,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: noiseParams,
                ErosionParams: new TileErosion.Parameters(Seed: 77, DropletCount: 0),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters,
                SpimParams: new StreamPowerErosion.Parameters(Iterations: 60));

            IReadOnlyList<TileGenerator.TileResult> perTileResults;
            using (var db = new SqliteWorldDatabase(dbPathPerTile))
            {
                WorldDatabaseSeeder.InitializeTerrainDatabase(db);
                perTileResults = TileGenerator.Run(db, baseSettings with { SpimChunkTilesPerSide = 1 }); // old, per-tile truncated behavior
            }

            IReadOnlyList<TileGenerator.TileResult> chunkedResults;
            using (var db = new SqliteWorldDatabase(dbPathChunked))
            {
                WorldDatabaseSeeder.InitializeTerrainDatabase(db);
                chunkedResults = TileGenerator.Run(db, baseSettings with { SpimChunkTilesPerSide = 8 }); // whole region in one chunk
            }

            Assert.IsTrue(perTileResults.Count >= 4, "Test region should span multiple tiles for a boundary to even exist.");

            var cellsPerTile = (int)Math.Round(baseSettings.TileSizeMeters / baseSettings.CellSizeMeters);

            using var readPerTile = new SqliteWorldDatabase(dbPathPerTile);
            using var readChunked = new SqliteWorldDatabase(dbPathChunked);
            var perTileRegion = StitchRegion(readPerTile, perTileResults, cellsPerTile);
            var chunkedRegion = StitchRegion(readChunked, chunkedResults, cellsPerTile);

            var perTileExcess = MeanBoundaryExcessJump(perTileRegion.Values, perTileRegion.Width, perTileRegion.Height, cellsPerTile);
            var chunkedExcess = MeanBoundaryExcessJump(chunkedRegion.Values, chunkedRegion.Width, chunkedRegion.Height, cellsPerTile);

            Assert.IsTrue(chunkedExcess < perTileExcess,
                $"Expected chunked SPIM to reduce the tile-boundary elevation discontinuity — per-tile excess={perTileExcess:F4}, chunked excess={chunkedExcess:F4}.");
        }
        finally
        {
            if (File.Exists(dbPathPerTile)) File.Delete(dbPathPerTile);
            if (File.Exists(dbPathChunked)) File.Delete(dbPathChunked);
        }
    }

    [TestMethod]
    public void Run_ChunkedSpim_ProducesFiniteValues()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var noiseParams = new PlanetNoise.Parameters(Seed: 5, AmplitudeMeters: 300.0, TectonicPlateCount: 6);
            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.014, LonMin: 0.0, LonMax: 0.014,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: noiseParams,
                ErosionParams: new TileErosion.Parameters(Seed: 5, DropletCount: 0),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters,
                SpimParams: new StreamPowerErosion.Parameters(Iterations: 40),
                RockTypeParams: new RockLayer.Parameters(Seed: 5),
                IsostasyParams: new Isostasy.Parameters(RecomputeIntervalIterations: 10),
                SpimChunkTilesPerSide: 3);

            var results = TileGenerator.Run(db, settings);

            foreach (var r in results)
            {
                var tile = db.LoadHeightmap(r.Id)!;
                foreach (var v in tile.Values)
                {
                    Assert.IsFalse(float.IsNaN(v), $"Tile {r.Id} has a NaN cell.");
                    Assert.IsFalse(float.IsInfinity(v), $"Tile {r.Id} has an infinite cell.");
                }
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void Run_ChunkedSpimSequentialVsParallel_ProducesByteIdenticalOutput()
    {
        var dbPathSeq = TempDbPath();
        var dbPathPar = TempDbPath();
        try
        {
            var noiseParams = new PlanetNoise.Parameters(Seed: 33, AmplitudeMeters: 300.0, TectonicPlateCount: 5);
            var baseSettings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.02, LonMin: 0.0, LonMax: 0.02,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: noiseParams,
                ErosionParams: new TileErosion.Parameters(Seed: 33, DropletCount: 0),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters,
                SpimParams: new StreamPowerErosion.Parameters(Iterations: 20),
                SpimChunkTilesPerSide: 2);

            using (var db = new SqliteWorldDatabase(dbPathSeq))
            {
                WorldDatabaseSeeder.InitializeTerrainDatabase(db);
                TileGenerator.Run(db, baseSettings with { SpimMaxDegreeOfParallelism = 1 });
            }
            using (var db = new SqliteWorldDatabase(dbPathPar))
            {
                WorldDatabaseSeeder.InitializeTerrainDatabase(db);
                TileGenerator.Run(db, baseSettings with { SpimMaxDegreeOfParallelism = 4 });
            }

            using var readSeq = new SqliteWorldDatabase(dbPathSeq);
            using var readPar = new SqliteWorldDatabase(dbPathPar);
            var seqSummaries = readSeq.ListHeightmaps();
            Assert.IsTrue(seqSummaries.Count >= 9, "Test grid should span multiple chunk diagonals for this test to mean anything.");

            foreach (var summary in seqSummaries)
            {
                var seqTile = readSeq.LoadHeightmap(summary.Id)!;
                var parTile = readPar.LoadHeightmap(summary.Id);
                Assert.IsNotNull(parTile, $"Tile {summary.Id} missing from the SPIM-chunk-parallel run.");
                CollectionAssert.AreEqual(seqTile.Values, parTile!.Values, $"Tile {summary.Id} differed between sequential and SPIM-chunk-parallel runs.");
            }
        }
        finally
        {
            if (File.Exists(dbPathSeq)) File.Delete(dbPathSeq);
            if (File.Exists(dbPathPar)) File.Delete(dbPathPar);
        }
    }
}
