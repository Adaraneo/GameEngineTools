using GameEngineTools.World.Data;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class TileGeneratorParallelTests
{
    private static string TempDbPath() => Path.Combine(Path.GetTempPath(), $"terragen_test_{Guid.NewGuid():N}.db");

    [TestMethod]
    public void ComputeAutoHydrologyDegree_NeverGoesBelowOne()
    {
        // A tiny memory budget (less than one chunk's estimated cost) must still return 1, not 0/negative.
        var degree = TileGenerator.ComputeAutoHydrologyDegree(chunkTilesPerSide: 20, cellsPerTile: 400, availableMemoryBytes: 1024);
        Assert.AreEqual(1, degree);
    }

    [TestMethod]
    public void ComputeAutoHydrologyDegree_NeverExceedsProcessorCount()
    {
        // An enormous memory budget must cap at Environment.ProcessorCount, not grow unbounded.
        var degree = TileGenerator.ComputeAutoHydrologyDegree(chunkTilesPerSide: 1, cellsPerTile: 1, availableMemoryBytes: long.MaxValue / 2);
        Assert.AreEqual(Environment.ProcessorCount, degree);
    }

    [TestMethod]
    public void ComputeAutoHydrologyDegree_ScalesWithAvailableMemory()
    {
        var chunkCells = 20L * 20 * 400 * 400;
        var oneChunkBytes = chunkCells * TileGenerator.EstimatedBytesPerHydrologyChunkCell;

        // Budget for roughly 3 chunks worth of memory (before the 0.5 safety fraction) -> expect ~1 chunk safely.
        var degreeForOne = TileGenerator.ComputeAutoHydrologyDegree(20, 400, availableMemoryBytes: (long)(oneChunkBytes * 1.5));
        Assert.AreEqual(1, degreeForOne);

        // Budget for roughly 20 chunks worth (before the safety fraction) -> expect more than 1, capped by cores.
        var degreeForMany = TileGenerator.ComputeAutoHydrologyDegree(20, 400, availableMemoryBytes: oneChunkBytes * 20);
        Assert.IsTrue(degreeForMany > degreeForOne, $"Expected more available memory to allow a higher degree (got {degreeForMany} vs {degreeForOne}).");
        Assert.IsTrue(degreeForMany <= Environment.ProcessorCount);
    }

    [TestMethod]
    public void Run_WithAutoHydrologyParallelism_ProducesByteIdenticalOutputToSequential()
    {
        var dbPathSeq = TempDbPath();
        var dbPathAuto = TempDbPath();
        try
        {
            var baseSettings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.008, LonMin: 0.0, LonMax: 0.008,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: new PlanetNoise.Parameters(Seed: 32, AmplitudeMeters: 200.0),
                ErosionParams: new TileErosion.Parameters(Seed: 32, DropletCount: 200, MaxDropletLifetime: 6),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters,
                HydrologyParams: new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 5.0),
                HydrologyChunkTilesPerSide: 2);

            using (var dbSeq = new SqliteWorldDatabase(dbPathSeq))
            {
                WorldDatabaseSeeder.InitializeTerrainDatabase(dbSeq);
                TileGenerator.Run(dbSeq, baseSettings);
            }
            using (var dbAuto = new SqliteWorldDatabase(dbPathAuto))
            {
                WorldDatabaseSeeder.InitializeTerrainDatabase(dbAuto);
                TileGenerator.Run(dbAuto, baseSettings with { AutoHydrologyParallelism = true });
            }

            using var readSeq = new SqliteWorldDatabase(dbPathSeq);
            using var readAuto = new SqliteWorldDatabase(dbPathAuto);
            Assert.AreEqual(readSeq.LoadAllReaches().Count, readAuto.LoadAllReaches().Count);

            foreach (var summary in readSeq.ListHeightmaps())
            {
                var seqTile = readSeq.LoadHeightmap(summary.Id)!;
                var autoTile = readAuto.LoadHeightmap(summary.Id);
                Assert.IsNotNull(autoTile, $"Tile {summary.Id} missing from the auto-parallel run.");
                CollectionAssert.AreEqual(seqTile.Values, autoTile!.Values, $"Tile {summary.Id} differed with AutoHydrologyParallelism on.");
            }
        }
        finally
        {
            if (File.Exists(dbPathSeq)) File.Delete(dbPathSeq);
            if (File.Exists(dbPathAuto)) File.Delete(dbPathAuto);
        }
    }

    [TestMethod]
    public void Run_WithParallelTiles_HydrologyDefaultsToSequential()
    {
        // Regression for a reported memory blowup: --parallel used to also parallelize hydrology
        // chunks (each ~(HydrologyChunkTilesPerSide)^2 times a single tile's memory cost) at the
        // SAME degree as tiles, so running with many cores could spawn many multi-GB chunks at
        // once. HydrologyMaxDegreeOfParallelism must default to 1 regardless of MaxDegreeOfParallelism.
        var settings = new TileGenerator.RunSettings(
            LatMin: 0.0, LatMax: 0.002, LonMin: 0.0, LonMax: 0.002,
            TileSizeMeters: 200.0, CellSizeMeters: 10.0,
            NoiseParams: new PlanetNoise.Parameters(Seed: 30, AmplitudeMeters: 200.0),
            ErosionParams: new TileErosion.Parameters(Seed: 30, DropletCount: 200, MaxDropletLifetime: 6),
            PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters,
            MaxDegreeOfParallelism: Environment.ProcessorCount);

        Assert.AreEqual(1, settings.HydrologyMaxDegreeOfParallelism);
    }

    [TestMethod]
    public void Run_HydrologyParallelVsSequential_ProducesByteIdenticalOutput()
    {
        var dbPathSeq = TempDbPath();
        var dbPathPar = TempDbPath();
        try
        {
            var baseSettings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.008, LonMin: 0.0, LonMax: 0.008,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: new PlanetNoise.Parameters(Seed: 31, AmplitudeMeters: 200.0),
                ErosionParams: new TileErosion.Parameters(Seed: 31, DropletCount: 200, MaxDropletLifetime: 6),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters,
                HydrologyParams: new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 5.0),
                HydrologyChunkTilesPerSide: 2);

            using (var dbSeq = new SqliteWorldDatabase(dbPathSeq))
            {
                WorldDatabaseSeeder.InitializeTerrainDatabase(dbSeq);
                TileGenerator.Run(dbSeq, baseSettings with { HydrologyMaxDegreeOfParallelism = 1 });
            }
            using (var dbPar = new SqliteWorldDatabase(dbPathPar))
            {
                WorldDatabaseSeeder.InitializeTerrainDatabase(dbPar);
                TileGenerator.Run(dbPar, baseSettings with { HydrologyMaxDegreeOfParallelism = 4 });
            }

            using var readSeq = new SqliteWorldDatabase(dbPathSeq);
            using var readPar = new SqliteWorldDatabase(dbPathPar);
            var seqReaches = readSeq.LoadAllReaches();
            var parReaches = readPar.LoadAllReaches();
            Assert.AreEqual(seqReaches.Count, parReaches.Count, "River reach count differed between sequential and parallel hydrology.");

            foreach (var summary in readSeq.ListHeightmaps())
            {
                var seqTile = readSeq.LoadHeightmap(summary.Id)!;
                var parTile = readPar.LoadHeightmap(summary.Id);
                Assert.IsNotNull(parTile, $"Tile {summary.Id} missing from the hydrology-parallel run.");
                CollectionAssert.AreEqual(seqTile.Values, parTile!.Values, $"Tile {summary.Id} differed between sequential and hydrology-parallel runs.");
            }
        }
        finally
        {
            if (File.Exists(dbPathSeq)) File.Delete(dbPathSeq);
            if (File.Exists(dbPathPar)) File.Delete(dbPathPar);
        }
    }

    [TestMethod]
    public void Run_ParallelVsSequential_ProducesByteIdenticalOutput()
    {
        // Load-bearing regression: TileGenerator.Run's remarks prove neighbor availability is identical between row-major and diagonal-batched order, so output must be byte-identical regardless of MaxDegreeOfParallelism.
        var dbPathSeq = TempDbPath();
        var dbPathPar = TempDbPath();
        try
        {
            var noiseParams = new PlanetNoise.Parameters(Seed: 21, AmplitudeMeters: 200.0, TectonicPlateCount: 6);
            var erosionParams = new TileErosion.Parameters(Seed: 21, DropletCount: 200, MaxDropletLifetime: 6);
            var radius = PlanetNoise.EarthRadiusMeters;

            var baseSettings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.008, LonMin: 0.0, LonMax: 0.008,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: noiseParams, ErosionParams: erosionParams, PlanetRadiusMeters: radius,
                HydrologyParams: new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 5.0),
                HydrologyChunkTilesPerSide: 2,
                SpimParams: new StreamPowerErosion.Parameters(Iterations: 15),
                RockTypeParams: new RockLayer.Parameters(Seed: 21),
                IsostasyParams: new Isostasy.Parameters(RecomputeIntervalIterations: 5));

            using (var dbSeq = new SqliteWorldDatabase(dbPathSeq))
            {
                WorldDatabaseSeeder.InitializeTerrainDatabase(dbSeq);
                TileGenerator.Run(dbSeq, baseSettings with { MaxDegreeOfParallelism = 1 });
            }
            using (var dbPar = new SqliteWorldDatabase(dbPathPar))
            {
                WorldDatabaseSeeder.InitializeTerrainDatabase(dbPar);
                TileGenerator.Run(dbPar, baseSettings with { MaxDegreeOfParallelism = 4 });
            }

            using var readSeq = new SqliteWorldDatabase(dbPathSeq);
            using var readPar = new SqliteWorldDatabase(dbPathPar);
            var seqSummaries = readSeq.ListHeightmaps();
            var parSummaries = readPar.ListHeightmaps();
            Assert.AreEqual(seqSummaries.Count, parSummaries.Count);
            Assert.IsTrue(seqSummaries.Count >= 9, "Test grid should span multiple diagonals/chunks for this test to mean anything.");

            foreach (var summary in seqSummaries)
            {
                var seqTile = readSeq.LoadHeightmap(summary.Id)!;
                var parTile = readPar.LoadHeightmap(summary.Id);
                Assert.IsNotNull(parTile, $"Tile {summary.Id} missing from the parallel run.");
                CollectionAssert.AreEqual(seqTile.Values, parTile!.Values, $"Tile {summary.Id} differed between sequential and parallel runs.");
            }

            var seqReaches = readSeq.LoadAllReaches();
            var parReaches = readPar.LoadAllReaches();
            Assert.AreEqual(seqReaches.Count, parReaches.Count, "River reach count differed between sequential and parallel runs.");
        }
        finally
        {
            if (File.Exists(dbPathSeq)) File.Delete(dbPathSeq);
            if (File.Exists(dbPathPar)) File.Delete(dbPathPar);
        }
    }

    [TestMethod]
    public void Run_ParallelVsSequential_ReturnsResultsInSameRowMajorOrder()
    {
        var dbPath1 = TempDbPath();
        var dbPath2 = TempDbPath();
        try
        {
            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.006, LonMin: 0.0, LonMax: 0.006,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: new PlanetNoise.Parameters(Seed: 22, AmplitudeMeters: 200.0),
                ErosionParams: new TileErosion.Parameters(Seed: 22, DropletCount: 200, MaxDropletLifetime: 6),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters);

            IReadOnlyList<TileGenerator.TileResult> sequential, parallel;
            using (var db1 = new SqliteWorldDatabase(dbPath1))
            {
                WorldDatabaseSeeder.InitializeTerrainDatabase(db1);
                sequential = TileGenerator.Run(db1, settings with { MaxDegreeOfParallelism = 1 });
            }
            using (var db2 = new SqliteWorldDatabase(dbPath2))
            {
                WorldDatabaseSeeder.InitializeTerrainDatabase(db2);
                parallel = TileGenerator.Run(db2, settings with { MaxDegreeOfParallelism = 4 });
            }

            Assert.AreEqual(sequential.Count, parallel.Count);
            Assert.IsTrue(sequential.Count > 4, "Test grid should span multiple rows/cols for row-major order to mean anything.");

            for (var i = 0; i < sequential.Count; i++)
            {
                Assert.AreEqual(sequential[i].Row, parallel[i].Row, $"Result order differed at index {i}.");
                Assert.AreEqual(sequential[i].Col, parallel[i].Col, $"Result order differed at index {i}.");
            }

            for (var i = 1; i < sequential.Count; i++)
            {
                var inOrder = sequential[i].Row > sequential[i - 1].Row ||
                    (sequential[i].Row == sequential[i - 1].Row && sequential[i].Col > sequential[i - 1].Col);
                Assert.IsTrue(inOrder, $"Results are not in row-major order at index {i}.");
            }
        }
        finally
        {
            if (File.Exists(dbPath1)) File.Delete(dbPath1);
            if (File.Exists(dbPath2)) File.Delete(dbPath2);
        }
    }

    [TestMethod]
    public void Run_WithParallelism_AllTilesGetGeneratedExactlyOnce()
    {
        // Guards the resultSlots/batchTileSlots index-write scheme: every (row,col) slot must be
        // written by exactly one diagonal-batch worker, never zero (a missing tile) or more than
        // once (a race/double-processed slot silently overwriting itself).
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.01, LonMin: 0.0, LonMax: 0.01,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: new PlanetNoise.Parameters(Seed: 23, AmplitudeMeters: 200.0),
                ErosionParams: new TileErosion.Parameters(Seed: 23, DropletCount: 200, MaxDropletLifetime: 6),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters,
                MaxDegreeOfParallelism: Environment.ProcessorCount);

            var results = TileGenerator.Run(db, settings);

            var distinctPositions = results.Select(r => (r.Row, r.Col)).Distinct().Count();
            Assert.AreEqual(results.Count, distinctPositions, "Every (row,col) must appear exactly once.");
            foreach (var r in results)
                Assert.IsNotNull(db.LoadHeightmap(r.Id), $"Tile {r.Id} wasn't actually persisted.");
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
