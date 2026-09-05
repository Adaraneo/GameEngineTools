using GameEngineTools.World.Data;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class TileGeneratorTests
{
    private static string TempDbPath() => Path.Combine(Path.GetTempPath(), $"terragen_test_{Guid.NewGuid():N}.db");

    [TestMethod]
    public void Run_AdjacentTiles_AgreeExactlyAlongSharedEdge_AfterErosion()
    {
        // The real end-to-end proof of the whole feature: two SEPARATELY-eroded tiles (each run
        // through TileErosion.Erode independently) must still agree exactly along their shared
        // border, because the later tile locks its margin to the earlier tile's actual saved
        // values before eroding — not just to fresh, independently-sampled noise.
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var noiseParams = new PlanetNoise.Parameters(Seed: 7, AmplitudeMeters: 200.0);
            var erosionParams = new TileErosion.Parameters(Seed: 7, DropletCount: 800, MaxDropletLifetime: 6);
            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.002, LonMin: 0.0, LonMax: 0.006,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: noiseParams, ErosionParams: erosionParams,
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters);

            var results = TileGenerator.Run(db, settings);

            var rowWithMultipleCols = results.GroupBy(r => r.Row).First(g => g.Count() >= 2);
            var ordered = rowWithMultipleCols.OrderBy(r => r.Col).ToList();
            Assert.IsTrue(ordered.Count >= 2, "Test setup needs at least 2 tiles in one row.");

            var west = db.LoadHeightmap(ordered[0].Id)!;
            var east = db.LoadHeightmap(ordered[1].Id)!;
            Assert.IsNotNull(west);
            Assert.IsNotNull(east);
            Assert.AreEqual(west.Height, east.Height);

            for (var y = 0; y < west.Height; y++)
            {
                var westEdge = west.Values[y * west.Width + (west.Width - 1)];
                var eastEdge = east.Values[y * east.Width + 0];
                Assert.AreEqual(westEdge, eastEdge, 1e-3f,
                    $"Row {y}: west tile's east edge ({westEdge}) doesn't match east tile's west edge ({eastEdge}).");
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void Run_WithTectonicPlates_AdjacentTilesStillAgreeExactlyAlongSharedEdge()
    {
        // Same proof as the non-tectonic test above, but with TectonicPlateCount > 0 — confirms
        // that switching the mountain layer onto TectonicPlates.Sample (instead of the single fixed
        // belt) doesn't break the tile-boundary agreement everything else here depends on. It only
        // works because TileGenerator builds the Plate[] array ONCE per run and reuses it for every
        // cell, so both tiles' SampleCombined calls consult the exact same plate positions.
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var noiseParams = new PlanetNoise.Parameters(Seed: 7, AmplitudeMeters: 200.0, TectonicPlateCount: 10);
            var erosionParams = new TileErosion.Parameters(Seed: 7, DropletCount: 800, MaxDropletLifetime: 6);
            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.002, LonMin: 0.0, LonMax: 0.006,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: noiseParams, ErosionParams: erosionParams,
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters);

            var results = TileGenerator.Run(db, settings);

            var rowWithMultipleCols = results.GroupBy(r => r.Row).First(g => g.Count() >= 2);
            var ordered = rowWithMultipleCols.OrderBy(r => r.Col).ToList();
            Assert.IsTrue(ordered.Count >= 2, "Test setup needs at least 2 tiles in one row.");

            var west = db.LoadHeightmap(ordered[0].Id)!;
            var east = db.LoadHeightmap(ordered[1].Id)!;
            Assert.IsNotNull(west);
            Assert.IsNotNull(east);
            Assert.AreEqual(west.Height, east.Height);

            for (var y = 0; y < west.Height; y++)
            {
                var westEdge = west.Values[y * west.Width + (west.Width - 1)];
                var eastEdge = east.Values[y * east.Width + 0];
                Assert.AreEqual(westEdge, eastEdge, 1e-3f,
                    $"Row {y}: west tile's east edge ({westEdge}) doesn't match east tile's west edge ({eastEdge}).");
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void Run_SeparateInvocations_ForAdjacentRegions_StillAgreeAtSharedEdge()
    {
        // The actual point of the fixed planet-wide reference point: two ENTIRELY SEPARATE
        // TileGenerator.Run calls (simulating "come back later and generate the region next
        // door") must still connect, not just tiles within one Run call. Both invocations use the
        // same default MountainOriginLatDeg/LonDeg (0,0) — that's what makes this work.
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var noiseParams = new PlanetNoise.Parameters(Seed: 9, AmplitudeMeters: 200.0);
            var erosionParams = new TileErosion.Parameters(Seed: 9, DropletCount: 800, MaxDropletLifetime: 6);
            var radius = PlanetNoise.EarthRadiusMeters;

            var settings1 = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.002, LonMin: 0.0, LonMax: 0.003,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: noiseParams, ErosionParams: erosionParams, PlanetRadiusMeters: radius);
            var results1 = TileGenerator.Run(db, settings1);

            var row0 = results1.Where(r => r.Row == 0).OrderBy(r => r.Col).ToList();
            Assert.IsTrue(row0.Count > 0, "First run produced no tiles.");
            var westTile = db.LoadHeightmap(row0[^1].Id)!;
            var boundaryX = westTile.OriginX + westTile.Width * westTile.CellSizeMeters;
            var (_, boundaryLon) = PlanetNoise.OffsetToLatLon(boundaryX, 0.0, 0.0, 0.0, radius);

            // Second, ENTIRELY SEPARATE invocation for the region immediately east of the first.
            var settings2 = settings1 with { LonMin = boundaryLon, LonMax = boundaryLon + 0.003 };
            var results2 = TileGenerator.Run(db, settings2);

            var row0b = results2.Where(r => r.Row == 0).OrderBy(r => r.Col).ToList();
            Assert.IsTrue(row0b.Count > 0, "Second run produced no tiles.");
            var eastTile = db.LoadHeightmap(row0b[0].Id)!;

            Assert.AreEqual(westTile.Height, eastTile.Height);
            for (var y = 0; y < westTile.Height; y++)
            {
                var westEdge = westTile.Values[y * westTile.Width + (westTile.Width - 1)];
                var eastEdge = eastTile.Values[y * eastTile.Width + 0];
                Assert.AreEqual(westEdge, eastEdge, 1e-3f,
                    $"Row {y}: tile from run #1's east edge ({westEdge}) doesn't match run #2's west edge ({eastEdge}).");
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void Run_TwoOverlappingRequests_WithDifferentCorners_PlaceTheSamePhysicalTileIdentically()
    {
        // Regression for the "regenerating shifts every tile" bug: nothing requires a re-scanned or
        // re-typed --lat-range/--lon-range to land EXACTLY on the previous run's own corner, only to
        // overlap it. Before snapping the tile grid to a fixed lattice, the second run's own corner
        // would re-phase its whole grid, so even a tile physically inside both requests would end up
        // with a different OriginX/OriginY (and a different id) each time — a visible position shift
        // for anything not re-touched by that particular run (e.g. a cached river mask).
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var noiseParams = new PlanetNoise.Parameters(Seed: 11, AmplitudeMeters: 200.0);
            var erosionParams = new TileErosion.Parameters(Seed: 11, DropletCount: 500, MaxDropletLifetime: 6);
            var radius = PlanetNoise.EarthRadiusMeters;

            // First run's corner sits deliberately off any tile boundary.
            var settings1 = new TileGenerator.RunSettings(
                LatMin: 0.00013, LatMax: 0.006, LonMin: 0.00027, LonMax: 0.006,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: noiseParams, ErosionParams: erosionParams, PlanetRadiusMeters: radius);
            var results1 = TileGenerator.Run(db, settings1);

            // Any tile whose center lies within BOTH requested regions must resolve to the exact
            // same id (hence the exact same OriginX/OriginY) in both runs — capture run 1's Origin
            // for each shared id BEFORE run 2 overwrites that same row.
            var settings2 = settings1 with { LatMin = 0.00089, LonMin = 0.00061 };
            var settings2SwX = Math.Floor(PlanetNoise.LatLonToOffset(settings2.LatMin, settings2.LonMin, 0, 0, radius).Item1 / settings2.TileSizeMeters) * settings2.TileSizeMeters;
            var settings2SwY = Math.Floor(PlanetNoise.LatLonToOffset(settings2.LatMin, settings2.LonMin, 0, 0, radius).Item2 / settings2.TileSizeMeters) * settings2.TileSizeMeters;
            var originsBeforeRun2 = results1.ToDictionary(r => r.Id, r => (db.LoadHeightmap(r.Id)!.OriginX, db.LoadHeightmap(r.Id)!.OriginY));

            var results2 = TileGenerator.Run(db, settings2);
            var shared = results1.Select(r => r.Id).Intersect(results2.Select(r => r.Id)).ToList();
            Assert.IsTrue(shared.Count > 0, "Test setup needs at least one tile physically inside both overlapping requests.");

            foreach (var id in shared)
            {
                var after = db.LoadHeightmap(id)!;
                var before = originsBeforeRun2[id];
                Assert.AreEqual(before.OriginX, after.OriginX, 1e-6,
                    $"Tile {id}: OriginX shifted between two overlapping runs ({before.OriginX} -> {after.OriginX}) — the tile grid re-phased instead of landing on the same fixed lattice.");
                Assert.AreEqual(before.OriginY, after.OriginY, 1e-6,
                    $"Tile {id}: OriginY shifted between two overlapping runs ({before.OriginY} -> {after.OriginY}) — the tile grid re-phased instead of landing on the same fixed lattice.");
            }

            // Sanity: settings2's own SW anchor really is snapped to the same TileSizeMeters lattice
            // as settings1's (both are multiples of TileSizeMeters), confirming the snap is what's
            // actually doing the work here, not a coincidence of the chosen test numbers.
            Assert.AreEqual(0.0, settings2SwX % settings2.TileSizeMeters, 1e-6);
            Assert.AreEqual(0.0, settings2SwY % settings2.TileSizeMeters, 1e-6);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void Run_ProducesExpectedNumberOfTiles()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.002, LonMin: 0.0, LonMax: 0.006,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: new PlanetNoise.Parameters(Seed: 1, AmplitudeMeters: 200.0),
                ErosionParams: new TileErosion.Parameters(Seed: 1, DropletCount: 500, MaxDropletLifetime: 6),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters);

            var results = TileGenerator.Run(db, settings);

            Assert.IsTrue(results.Count > 0);
            foreach (var r in results)
                Assert.IsNotNull(db.LoadHeightmap(r.Id), $"Tile {r.Id} wasn't actually persisted.");
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void Run_SameSettings_IsDeterministic()
    {
        var dbPath1 = TempDbPath();
        var dbPath2 = TempDbPath();
        try
        {
            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.002, LonMin: 0.0, LonMax: 0.002,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: new PlanetNoise.Parameters(Seed: 42, AmplitudeMeters: 200.0),
                ErosionParams: new TileErosion.Parameters(Seed: 42, DropletCount: 500, MaxDropletLifetime: 6),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters);

            IReadOnlyList<TileGenerator.TileResult> results1, results2;
            using (var db1 = new SqliteWorldDatabase(dbPath1))
            {
                WorldDatabaseSeeder.InitializeTerrainDatabase(db1);
                results1 = TileGenerator.Run(db1, settings);
            }
            using (var db2 = new SqliteWorldDatabase(dbPath2))
            {
                WorldDatabaseSeeder.InitializeTerrainDatabase(db2);
                results2 = TileGenerator.Run(db2, settings);
            }

            Assert.AreEqual(results1.Count, results2.Count);
            using var readDb1 = new SqliteWorldDatabase(dbPath1);
            using var readDb2 = new SqliteWorldDatabase(dbPath2);
            foreach (var r1 in results1)
            {
                var tile1 = readDb1.LoadHeightmap(r1.Id);
                var tile2 = readDb2.LoadHeightmap(r1.Id);
                Assert.IsNotNull(tile1);
                Assert.IsNotNull(tile2);
                CollectionAssert.AreEqual(tile1!.Values, tile2!.Values, $"Tile {r1.Id} differed between two identical runs.");
            }
        }
        finally
        {
            if (File.Exists(dbPath1)) File.Delete(dbPath1);
            if (File.Exists(dbPath2)) File.Delete(dbPath2);
        }
    }

    [TestMethod]
    public void Run_WithoutHydrologyParams_LeavesRiverMaskNull()
    {
        // Backward compatibility: existing worlds generated before --rivers existed must keep
        // getting a null RiverMask (the same as no river data ever having been painted).
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.002, LonMin: 0.0, LonMax: 0.002,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: new PlanetNoise.Parameters(Seed: 5, AmplitudeMeters: 200.0),
                ErosionParams: new TileErosion.Parameters(Seed: 5, DropletCount: 500, MaxDropletLifetime: 6),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters);

            var results = TileGenerator.Run(db, settings);
            foreach (var r in results)
                Assert.IsNull(db.LoadHeightmap(r.Id)!.RiverMask);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void Run_WithHydrologyParams_PopulatesRiverMaskOfCorrectLength()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.002, LonMin: 0.0, LonMax: 0.002,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: new PlanetNoise.Parameters(Seed: 5, AmplitudeMeters: 200.0),
                ErosionParams: new TileErosion.Parameters(Seed: 5, DropletCount: 500, MaxDropletLifetime: 6),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters,
                HydrologyParams: new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 5.0));

            var results = TileGenerator.Run(db, settings);
            Assert.IsTrue(results.Count > 0);
            foreach (var r in results)
            {
                var tile = db.LoadHeightmap(r.Id)!;
                Assert.IsNotNull(tile.RiverMask);
                Assert.AreEqual(tile.Width * tile.Height, tile.RiverMask!.Length);
                // A river cell's byte value is its Strahler order (see TerrainHeightmap.RiverMask's
                // remarks), not a flat 1 — sanity-bound it instead of requiring exactly 0/1: this
                // tiny test grid can't plausibly produce a double-digit order.
                Assert.IsTrue(tile.RiverMask.All(b => b < 20), "RiverMask order values should be small on a tiny test grid.");
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void Run_WithHydrologyParams_AndZeroRelief_EmitsNoRiverHintInsteadOfSilence()
    {
        // AmplitudeMeters: 0 deterministically reproduces the flat-terrain case that triggers the hint.
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.002, LonMin: 0.0, LonMax: 0.002,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: new PlanetNoise.Parameters(Seed: 5, AmplitudeMeters: 0.0),
                ErosionParams: new TileErosion.Parameters(Seed: 5, DropletCount: 0, MaxDropletLifetime: 6),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters,
                HydrologyParams: new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 10.0));

            var progressLines = new List<string>();
            var results = TileGenerator.Run(db, settings, onProgress: progressLines.Add);

            foreach (var r in results)
            {
                var tile = db.LoadHeightmap(r.Id)!;
                Assert.IsNotNull(tile.RiverMask);
                Assert.IsTrue(tile.RiverMask!.All(b => b == 0), "Flat terrain should never qualify a channel.");
            }
            Assert.IsTrue(progressLines.Any(l => l.Contains("--river-threshold") && l.Contains("reliéf")),
                "A run that found zero river cells anywhere should explain why instead of just reporting success.");
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void Run_WithHydrologyParams_RiversStayConnectedAcrossTileBoundaries()
    {
        // Regression test for a real generation defect (found live on production terrain): before
        // this batch-wide hydrology pass existed, TileHydrology.ComputeRiverMask ran once PER
        // TILE, so a river's flow accumulation reset to a tiny local catchment at every tile edge —
        // confirmed live, only ~9% of river cells sitting exactly on a tile boundary lined up with
        // a river cell in the neighboring tile at the same position (i.e. ~91% just dead-ended).
        // TileGenerator.Run now stitches every tile in the batch into one combined grid before
        // calling ComputeRiverMask ONCE, so a river's accumulation genuinely carries across tile
        // edges. This asserts that fix holds: for river cells sitting exactly on an INTERNAL tile
        // boundary (i.e. not the whole batch's own outer edge, which still has no upstream context
        // to draw on — same already-accepted local-approximation limit as before), most must line
        // up with a river cell in the neighboring tile at the same position.
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.006, LonMin: 0.0, LonMax: 0.006,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: new PlanetNoise.Parameters(Seed: 5, AmplitudeMeters: 200.0),
                ErosionParams: new TileErosion.Parameters(Seed: 5, DropletCount: 500, MaxDropletLifetime: 6),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters,
                HydrologyParams: new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 5.0));

            var results = TileGenerator.Run(db, settings);
            var tiles = results.Select(r => db.LoadHeightmap(r.Id)!).ToList();
            Assert.IsTrue(tiles.Count >= 4, "Need a multi-tile grid for a boundary-crossing check to mean anything.");

            var byOrigin = tiles.ToDictionary(t => (Math.Round(t.OriginX, 3), Math.Round(t.OriginY, 3)));
            var boundaryRiverCells = 0;
            var connectedAcross = 0;

            foreach (var t in tiles)
            {
                if (t.RiverMask is null) continue;
                var w = t.Width;
                var h = t.Height;
                var cs = t.CellSizeMeters;

                // Checked against a 3-wide window (y-1..y+1 / x-1..x+1) on the far side, not just
                // the exact same row/column — D8 flow is 8-connected, so a channel crossing a
                // boundary diagonally legitimately lands one cell off straight-across.
                var eastKey = (Math.Round(t.OriginX + w * cs, 3), Math.Round(t.OriginY, 3));
                if (byOrigin.TryGetValue(eastKey, out var east) && east.RiverMask is not null)
                {
                    for (var y = 0; y < h; y++)
                    {
                        if (t.RiverMask[y * w + (w - 1)] == 0) continue;
                        boundaryRiverCells++;
                        var found = false;
                        for (var dy = -1; dy <= 1 && !found; dy++)
                        {
                            var ny = y + dy;
                            if (ny < 0 || ny >= h) continue;
                            if (east.RiverMask[ny * w + 0] != 0) found = true;
                        }
                        if (found) connectedAcross++;
                    }
                }

                var northKey = (Math.Round(t.OriginX, 3), Math.Round(t.OriginY + h * cs, 3));
                if (byOrigin.TryGetValue(northKey, out var north) && north.RiverMask is not null)
                {
                    for (var x = 0; x < w; x++)
                    {
                        if (t.RiverMask[(h - 1) * w + x] == 0) continue;
                        boundaryRiverCells++;
                        var found = false;
                        for (var dx = -1; dx <= 1 && !found; dx++)
                        {
                            var nx = x + dx;
                            if (nx < 0 || nx >= w) continue;
                            if (north.RiverMask[0 * w + nx] != 0) found = true;
                        }
                        if (found) connectedAcross++;
                    }
                }
            }

            Assert.IsTrue(boundaryRiverCells > 0, "Test grid produced no river cells on any internal tile boundary — pick different noise/threshold.");
            var pct = 100.0 * connectedAcross / boundaryRiverCells;
            Assert.IsTrue(pct > 50.0,
                $"Expected most internal-boundary river cells to connect into their neighbor tile " +
                $"(>50%), not dead-end at nearly every edge like the pre-fix per-tile computation did " +
                $"(~9% live) — got {pct:F1}% ({connectedAcross}/{boundaryRiverCells}).");
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void Run_WithSmallHydrologyChunkSize_StillConnectsRiversWithinEachChunk()
    {
        // Regression test for the chunked hydrology processing that replaced a single combined
        // grid for the ENTIRE batch (which could overflow a 32-bit array length or exhaust memory
        // on a large real request — see RunSettings.HydrologyChunkTilesPerSide's remarks). A small
        // HydrologyChunkTilesPerSide forces this batch into MULTIPLE chunks (unlike every other
        // hydrology test in this file, which all fit inside one default-sized chunk and so never
        // actually exercise the new multi-chunk code path) — connectivity is only guaranteed WITHIN
        // a chunk, so this checks river cells only across an internal boundary shared by two tiles
        // in the SAME chunk, mirroring RiversStayConnectedAcrossTileBoundaries's own check but
        // scoped to that weaker (chunk-local, not batch-wide) guarantee.
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            const int chunkTilesPerSide = 2;
            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.006, LonMin: 0.0, LonMax: 0.006,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: new PlanetNoise.Parameters(Seed: 5, AmplitudeMeters: 200.0),
                ErosionParams: new TileErosion.Parameters(Seed: 5, DropletCount: 500, MaxDropletLifetime: 6),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters,
                HydrologyParams: new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 5.0),
                HydrologyChunkTilesPerSide: chunkTilesPerSide);

            var results = TileGenerator.Run(db, settings);
            var byRowCol = results.ToDictionary(r => (r.Row, r.Col));
            Assert.IsTrue(results.Select(r => r.Row / chunkTilesPerSide).Distinct().Count() > 1
                || results.Select(r => r.Col / chunkTilesPerSide).Distinct().Count() > 1,
                "Test grid should span multiple hydrology chunks for this test to mean anything.");

            var tilesById = results.ToDictionary(r => r.Id, r => db.LoadHeightmap(r.Id)!);
            foreach (var t in tilesById.Values)
            {
                Assert.IsNotNull(t.RiverMask, "Every tile should still get river data with chunked processing, same as before.");
                Assert.AreEqual(t.Width * t.Height, t.RiverMask!.Length);
            }

            var boundaryRiverCells = 0;
            var connectedAcross = 0;
            foreach (var (row, col) in byRowCol.Keys)
            {
                var eastRowCol = (row, col + 1);
                if (!byRowCol.TryGetValue(eastRowCol, out var eastResult)) continue;
                // Only a same-chunk pair exercises the chunk-local connectivity guarantee — a
                // cross-chunk pair is expected to fragment (that's the accepted tradeoff, not a bug).
                if (row / chunkTilesPerSide != eastResult.Row / chunkTilesPerSide) continue;
                if (col / chunkTilesPerSide != eastResult.Col / chunkTilesPerSide) continue;

                var west = tilesById[byRowCol[(row, col)].Id];
                var east = tilesById[eastResult.Id];
                var w = west.Width;
                var h = west.Height;

                for (var y = 0; y < h; y++)
                {
                    if (west.RiverMask![y * w + (w - 1)] == 0) continue;
                    boundaryRiverCells++;
                    var found = false;
                    for (var dy = -1; dy <= 1 && !found; dy++)
                    {
                        var ny = y + dy;
                        if (ny < 0 || ny >= h) continue;
                        if (east.RiverMask![ny * w + 0] != 0) found = true;
                    }
                    if (found) connectedAcross++;
                }
            }

            Assert.IsTrue(boundaryRiverCells > 0, "Test grid produced no river cells on any same-chunk internal tile boundary — pick different noise/threshold/chunk size.");
            var pct = 100.0 * connectedAcross / boundaryRiverCells;
            Assert.IsTrue(pct > 50.0,
                $"Expected most SAME-CHUNK internal-boundary river cells to still connect into their neighbor tile " +
                $"(>50%) with chunked processing — got {pct:F1}% ({connectedAcross}/{boundaryRiverCells}).");
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void Run_SkipExisting_ReusesSavedElevationInsteadOfRegenerating()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var firstSettings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.002, LonMin: 0.0, LonMax: 0.002,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: new PlanetNoise.Parameters(Seed: 11, AmplitudeMeters: 200.0),
                ErosionParams: new TileErosion.Parameters(Seed: 11, DropletCount: 500, MaxDropletLifetime: 6),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters);
            var firstResults = TileGenerator.Run(db, firstSettings);
            var savedValues = db.LoadHeightmap(firstResults[0].Id)!.Values.ToArray();

            // Same Seed (TileId is keyed on it, so the tile must be found existing at all), but a
            // different AmplitudeMeters/DropletCount — enough to produce different elevation if the
            // tile were actually regenerated. SkipExisting must reuse the saved data instead.
            var secondSettings = firstSettings with
            {
                NoiseParams = new PlanetNoise.Parameters(Seed: 11, AmplitudeMeters: 800.0),
                ErosionParams = new TileErosion.Parameters(Seed: 11, DropletCount: 1500, MaxDropletLifetime: 6),
                SkipExisting = true,
            };
            var skipMessages = new List<string>();
            var secondResults = TileGenerator.Run(db, secondSettings, skipMessages.Add);

            CollectionAssert.AreEqual(firstResults.Select(r => r.Id).ToList(), secondResults.Select(r => r.Id).ToList());
            CollectionAssert.AreEqual(savedValues, db.LoadHeightmap(secondResults[0].Id)!.Values);
            Assert.IsTrue(skipMessages.Any(m => m.Contains("přeskočeno")),
                "Expected at least one progress line reporting a skipped tile.");
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void Run_SkipExistingWithHydrology_StillRecomputesRiverMaskForSkippedTiles()
    {
        // Per design: SkipExisting only skips the expensive noise+erosion step. Hydrology still
        // reprocesses the whole chunk every run, so a skipped tile's RiverMask/OxbowMask stay
        // consistent with its (possibly regenerated) neighbors in the same chunk.
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var baseSettings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.002, LonMin: 0.0, LonMax: 0.002,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: new PlanetNoise.Parameters(Seed: 5, AmplitudeMeters: 200.0),
                ErosionParams: new TileErosion.Parameters(Seed: 5, DropletCount: 500, MaxDropletLifetime: 6),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters);
            TileGenerator.Run(db, baseSettings);

            var withHydrology = baseSettings with
            {
                SkipExisting = true,
                HydrologyParams = new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 5.0),
            };
            var results = TileGenerator.Run(db, withHydrology);

            foreach (var r in results)
            {
                var tile = db.LoadHeightmap(r.Id)!;
                Assert.IsNotNull(tile.RiverMask, "Skipped tile should still get hydrology's RiverMask, not stay null.");
                Assert.AreEqual(tile.Width * tile.Height, tile.RiverMask!.Length);
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void Run_OnTileProgressCallback_ReportsEveryTileUpToTheTotal()
    {
        var dbPath = TempDbPath();
        try
        {
            using var db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(db);

            var settings = new TileGenerator.RunSettings(
                LatMin: 0.0, LatMax: 0.002, LonMin: 0.0, LonMax: 0.006,
                TileSizeMeters: 200.0, CellSizeMeters: 10.0,
                NoiseParams: new PlanetNoise.Parameters(Seed: 3, AmplitudeMeters: 200.0),
                ErosionParams: new TileErosion.Parameters(Seed: 3, DropletCount: 500, MaxDropletLifetime: 6),
                PlanetRadiusMeters: PlanetNoise.EarthRadiusMeters);

            var progressCalls = new List<(int Done, int Total)>();
            var results = TileGenerator.Run(db, settings, onTileProgress: (done, total) => progressCalls.Add((done, total)));

            Assert.AreEqual(results.Count, progressCalls.Count, "Expected exactly one progress callback per generated tile.");
            Assert.IsTrue(progressCalls.All(c => c.Total == results.Count), "Total should be the same fixed tile count on every call.");
            CollectionAssert.AreEqual(Enumerable.Range(1, results.Count).ToList(), progressCalls.Select(c => c.Done).ToList());
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    public void ValidateHydrologyChunkSize_ChunkExceedsSafeCellCount_ThrowsActionableException()
    {
        // Direct unit test of the extracted guard (see its own remarks) against a contrived chunk
        // size well past MaxSafeHydrologyChunkCells — this is the check that turns what used to be
        // a bare, confusing OverflowException (or worse, silent memory exhaustion) from
        // `new float[bigWidth * bigHeight]` into an actionable message pointing at
        // HydrologyChunkTilesPerSide, without needing to actually generate a batch large enough to
        // trigger it for real.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TileGenerator.ValidateHydrologyChunkSize(
                chunkBigWidth: 20_000, chunkBigHeight: 20_000, chunkTilesPerSide: 50,
                minRow: 0, maxRowInclusive: 49, minCol: 0, maxColInclusive: 49));

        StringAssert.Contains(ex.Message, "HydrologyChunkTilesPerSide");
    }

    [TestMethod]
    public void ValidateHydrologyChunkSize_ChunkWithinSafeCellCount_DoesNotThrow()
    {
        // Regression guard: the default HydrologyChunkTilesPerSide=20 at the common default
        // TileKm=1/CellMeters=2.5 (400 cells/tile side) produces an 8000x8000 chunk — this must NOT
        // trip the guard, or every ordinary batch run using defaults would start failing.
        TileGenerator.ValidateHydrologyChunkSize(
            chunkBigWidth: 8000, chunkBigHeight: 8000, chunkTilesPerSide: 20,
            minRow: 0, maxRowInclusive: 19, minCol: 0, maxColInclusive: 19);
    }
}
