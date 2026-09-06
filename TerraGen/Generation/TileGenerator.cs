using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameEngineTools.World.Data;

namespace TerraGen.Generation;

/// <summary>
/// Orchestrates a batch run: generates a rectangular region of a planet tile-by-tile and persists
/// each tile via <see cref="SqliteWorldDatabase.SaveHeightmap"/>. Tiles are processed south-to-
/// north, west-to-east. Before eroding, each new tile checks all 4 neighbor positions (west,
/// east, south, north) for an ALREADY-SAVED tile — from earlier in this same run, or from an
/// entirely separate earlier invocation, it doesn't matter which — and overlays/locks (see
/// <see cref="TileErosion"/>) its margin against whichever neighbors already exist, so the shared
/// border agrees exactly after erosion too, not just in the raw pre-erosion noise. A direction
/// with no existing neighbor yet is left as this tile's own fresh result, to be picked up later
/// as context whenever that neighbor eventually gets generated.
///
/// River extraction (see <see cref="TileHydrology"/>) is the one step that does NOT follow this
/// same per-tile-independent shape: D8 flow accumulation genuinely needs to know how much water
/// already flowed in from upstream, which per-tile computation alone can't answer — a river that
/// built up a large catchment over many tiles would otherwise look, to each individual downstream
/// tile, like it's starting fresh with a tiny local catchment right at its own western/southern
/// edge, breaking the channel into disconnected fragments at every tile boundary (confirmed live:
/// only ~9% of river cells sitting exactly on a tile edge lined up with a river cell in the
/// neighboring tile at the same position). So hydrology runs once per <see cref="RunSettings.HydrologyChunkTilesPerSide"/>-sized
/// chunk (not per tile, and not for the whole region — see that setting's remarks for why), fully connecting rivers within a chunk but not across a chunk boundary.
/// </summary>
/// <remarks>
/// Every tile's mountain layer is computed as a flat offset from <see cref="RunSettings.MountainOriginLatDeg"/>/
/// <see cref="RunSettings.MountainOriginLonDeg"/> (default: the equator/prime meridian, 0,0) — a
/// FIXED point shared by every run against a given planet, not derived from that run's own
/// requested region. That's what makes tiles from SEPARATE invocations (today, next week, a
/// different lat/lon range) still agree at their shared edges: two tiles computed from the same
/// formula against the same reference point necessarily match, the same way the landmass layer
/// already does via true 3D-sphere sampling. The tradeoff, inherent to any single flat projection
/// of a sphere, is <see cref="PlanetNoise.OffsetToLatLon"/>'s own distortion far from the
/// reference latitude — mountain shapes stretch increasingly east-west approaching the poles.
/// That distortion is smooth and consistent, though, so it never breaks tile agreement — it only
/// ever changes how a far-away range looks, not whether it connects.
/// </remarks>
public static class TileGenerator
{
    public sealed record RunSettings(
        double LatMin, double LatMax, double LonMin, double LonMax,
        double TileSizeMeters, double CellSizeMeters,
        PlanetNoise.Parameters NoiseParams, TileErosion.Parameters ErosionParams,
        double PlanetRadiusMeters,
        /// <summary>Fixed planet-wide reference point every run's mountain layer is computed
        /// against — leave at the default (0,0) unless you specifically want tiles generated with
        /// a different reference to NOT connect to tiles generated with this one (they won't,
        /// since their flat mountain offsets would no longer agree).</summary>
        double MountainOriginLatDeg = 0.0,
        double MountainOriginLonDeg = 0.0,
        /// <summary><c>null</c> (default) skips river extraction entirely. Non-null derives <see cref="TerrainHeightmap.RiverMask"/> from each hydrology chunk's own stitched drainage pattern.</summary>
        TileHydrology.Parameters? HydrologyParams = null,
        /// <summary>Tiles per side of a square hydrology chunk, bounding the combined grid so it can't overflow a 32-bit array index on a large region. Default 20 ≈ 64M cells at TileKm=1/CellMeters=2.5.</summary>
        int HydrologyChunkTilesPerSide = 20,
        /// <summary>Off by default (always regenerate/overwrite). When true, a tile whose TileId already has a saved heightmap of the expected size skips noise+erosion and reuses the saved elevation; hydrology (if on) still reprocesses its whole chunk.</summary>
        bool SkipExisting = false,
        /// <summary>Null (default) keeps the existing ridged-noise mountain layer unchanged — see <see cref="StreamPowerErosion"/>. Non-null REPLACES it: each tile samples landmass only, then <see cref="StreamPowerErosion.Erode"/> builds relief from an uplift field derived from <see cref="TectonicPlates"/> (Task 1.2.4's confirmed integration mode), before droplet erosion runs as a fine-detail finishing pass. ⚠ Runs per padded tile with the same erosion-margin neighbor-locking as <see cref="TileErosion"/> — a local-drainage approximation, not a whole-region solve; a real basin wider than the margin is truncated at the tile's own padding.</summary>
        StreamPowerErosion.Parameters? SpimParams = null,
        /// <summary>Null (default) keeps SPIM's scalar Parameters.K everywhere, unchanged. Non-null (only meaningful together with <see cref="SpimParams"/>) couples erodibility to a per-cell <see cref="RockLayer"/> lithology assignment (Task 2.2.3) instead.</summary>
        RockLayer.Parameters? RockTypeParams = null,
        /// <summary>Null (default) leaves SPIM's uplift field untouched by erosion, unchanged. Non-null (only meaningful together with <see cref="SpimParams"/>) turns on the Stage 3.1 erosion→unloading→rebound feedback (<see cref="Isostasy"/>).</summary>
        Isostasy.Parameters? IsostasyParams = null,
        /// <summary>Null (default) keeps SPIM's bare D8 cell-count drainage area, unchanged. Non-null (only meaningful together with <see cref="SpimParams"/>) weights it by <see cref="OrographicPrecipitation"/>'s Smith &amp; Barstad field instead (Stage 4).</summary>
        OrographicPrecipitation.Parameters? OrographicParams = null,
        /// <summary>1 (default) keeps every tile/chunk fully sequential, byte-identical to before this setting existed. &gt;1 processes independent tiles/hydrology chunks concurrently — see <see cref="Run"/>'s remarks on the diagonal-wavefront schedule this relies on for correctness.</summary>
        int MaxDegreeOfParallelism = 1);

    public sealed record TileResult(int Row, int Col, string Id, double CenterLatDeg, double CenterLonDeg);

    /// <summary>Deterministic tile id from the run's seed and the tile's true center position —
    /// stable across re-runs of the same region (idempotent overwrite) and collision-resistant
    /// against unrelated runs at different positions (unlike a bare row/col index, which is only
    /// meaningful relative to one run's own south-west corner).</summary>
    public static string TileId(int seed, double centerLatDeg, double centerLonDeg)
        => $"tile_{seed}_{(int)Math.Round(centerLatDeg * 1000)}_{(int)Math.Round(centerLonDeg * 1000)}";

    /// <summary>Runs the whole batch, saving every generated tile into <paramref name="db"/> and returning the tiles in row-major order regardless of <see cref="RunSettings.MaxDegreeOfParallelism"/>. <paramref name="onProgress"/> (optional) receives one human-readable line per completed tile.</summary>
    /// <param name="onTileProgress">Optional (done, total) callback after each tile, for a progress bar.</param>
    /// <remarks>MaxDegreeOfParallelism &gt; 1 batches tiles by anti-diagonal (row+col) instead of one row at a time: a tile's west/south neighbor always sits on the PREVIOUS diagonal, so no two same-diagonal tiles can ever be each other's neighbor — provably independent, safe to run concurrently within a diagonal, with a barrier between diagonals. Neighbor availability at generation time is therefore identical to fully sequential order, so output is byte-identical (see TileGeneratorTests' parallel-vs-sequential regression). Hydrology chunks below need no such scheme — they only start once every tile exists, and each owns a disjoint tile set and network id, so chunks are unconditionally independent.</remarks>
    public static IReadOnlyList<TileResult> Run(SqliteWorldDatabase db, RunSettings s,
        Action<string>? onProgress = null, Action<int, int>? onTileProgress = null)
    {
        var refLatDeg = s.MountainOriginLatDeg;
        var refLonDeg = s.MountainOriginLonDeg;

        // Built ONCE for the whole run (pure function of Seed+count — see TectonicPlates.Generate)
        // and reused for every grid cell below, rather than rebuilt per cell. Also what makes tiles
        // from separate runs/invocations agree on the same plates, the same way the fixed mountain
        // reference point above does for the non-tectonic layer.
        var plates = s.NoiseParams.TectonicPlateCount > 0
            ? TectonicPlates.Generate(s.NoiseParams.Seed, s.NoiseParams.TectonicPlateCount)
            : null;

        // The requested region's corners, converted into the FIXED reference frame — not derived
        // from the region's own center, so the same physical corner always lands at the same flat
        // offset regardless of what other region a future run happens to request. See the
        // type-level remarks for why this fixed-frame choice is what lets separate runs connect.
        var (rawSwX, rawSwY) = PlanetNoise.LatLonToOffset(s.LatMin, s.LonMin, refLatDeg, refLonDeg, s.PlanetRadiusMeters);
        var (neX, neY) = PlanetNoise.LatLonToOffset(s.LatMax, s.LonMax, refLatDeg, refLonDeg, s.PlanetRadiusMeters);

        // Snapped DOWN to the fixed global tile lattice (whole multiples of TileSizeMeters from the
        // reference point) — NOT left at the raw requested corner. Every tile below is placed at
        // swX + col*TileSizeMeters, so without this snap, two runs whose requested LatMin/LonMin
        // merely overlap (rather than landing on an identical value down to the last bit — e.g. a
        // re-scanned region typed back in, or a deliberate re-generate of "the same" area) would
        // anchor their whole tile grid at two different phases and never actually line up, even
        // though the underlying noise/elevation IS globally seamless. Symptom without this: re-
        // running over what looks like the same area visibly shifts every tile's position, while
        // anything that doesn't get regenerated (a river mask still cached from the old, differently
        // -phased grid) stays behind at the old positions — looking like it "moved" relative to the
        // new terrain. Snapping makes any tile physically inside two overlapping runs land at the
        // exact same OriginX/OriginY (hence the same TileId) in both, regardless of exactly where
        // each run's own corner was requested.
        var swX = Math.Floor(rawSwX / s.TileSizeMeters) * s.TileSizeMeters;
        var swY = Math.Floor(rawSwY / s.TileSizeMeters) * s.TileSizeMeters;
        var regionWidthMeters = neX - swX;
        var regionHeightMeters = neY - swY;

        var cols = Math.Max(1, (int)Math.Ceiling(regionWidthMeters / s.TileSizeMeters));
        var rows = Math.Max(1, (int)Math.Ceiling(regionHeightMeters / s.TileSizeMeters));
        var cellsPerTile = Math.Max(1, (int)Math.Round(s.TileSizeMeters / s.CellSizeMeters));
        var margin = Math.Max(1, s.ErosionParams.MaxDropletLifetime);

        (double lat, double lon) TileCenter(int row, int col)
        {
            var originX = swX + col * s.TileSizeMeters;
            var originY = swY + row * s.TileSizeMeters;
            var centerX = originX + cellsPerTile * s.CellSizeMeters / 2.0;
            var centerY = originY + cellsPerTile * s.CellSizeMeters / 2.0;
            return PlanetNoise.OffsetToLatLon(centerX, centerY, refLatDeg, refLonDeg, s.PlanetRadiusMeters);
        }

        // Filled by row*cols+col index from GenerateOneTile below (each slot written exactly once,
        // by whichever diagonal batch owns that (row,col)), then flattened to row-major order —
        // safe to write from multiple threads concurrently since every write targets a distinct index.
        var resultSlots = new TileResult?[rows * cols];
        var batchTileSlots = s.HydrologyParams is not null
            ? new (int Row, int Col, string Id, double CenterLat, double CenterLon, float[] Interior)?[rows * cols]
            : null;

        var totalTiles = rows * cols;
        var tilesDone = 0;

        // Every onProgress/onTileProgress invocation goes through this lock — callers (e.g.
        // Program.cs's console progress bar) keep their own non-thread-safe mutable state and must
        // never see two callbacks land at once, parallel tiles or not.
        var progressLock = new object();
        void ReportProgress(string message)
        {
            if (onProgress is null) return;
            lock (progressLock) onProgress(message);
        }
        void ReportTileProgress()
        {
            var done = Interlocked.Increment(ref tilesDone);
            if (onTileProgress is null) return;
            lock (progressLock) onTileProgress(done, totalTiles);
        }

        (TileResult Result, (int Row, int Col, string Id, double CenterLat, double CenterLon, float[] Interior)? BatchEntry) GenerateOneTile(int row, int col)
        {
                var tileOriginX = swX + col * s.TileSizeMeters;
                var tileOriginY = swY + row * s.TileSizeMeters;
                var (centerLat, centerLon) = TileCenter(row, col);
                var id = TileId(s.NoiseParams.Seed, centerLat, centerLon);

                if (s.SkipExisting && db.LoadHeightmap(id) is { } existing &&
                    existing.Width == cellsPerTile && existing.Height == cellsPerTile)
                {
                    ReportProgress($"[{row},{col}] {id}: přeskočeno, dlaždice už existuje");
                    ReportTileProgress();
                    return (new TileResult(row, col, id, centerLat, centerLon),
                        (row, col, id, centerLat, centerLon, existing.Values));
                }

                var paddedSize = cellsPerTile + 2 * margin;
                var paddedOriginX = tileOriginX - margin * s.CellSizeMeters;
                var paddedOriginY = tileOriginY - margin * s.CellSizeMeters;
                var paddedValues = new float[paddedSize * paddedSize];

                for (var gy = 0; gy < paddedSize; gy++)
                {
                    var worldY = paddedOriginY + gy * s.CellSizeMeters;
                    for (var gx = 0; gx < paddedSize; gx++)
                    {
                        var worldX = paddedOriginX + gx * s.CellSizeMeters;
                        // --spim samples landmass only here — StreamPowerErosion supplies the mountain relief below, replacing the ridged-noise term SampleCombined would otherwise add.
                        if (s.SpimParams is not null)
                        {
                            var (spimLat, spimLon) = PlanetNoise.OffsetToLatLon(worldX, worldY, refLatDeg, refLonDeg, s.PlanetRadiusMeters);
                            paddedValues[gy * paddedSize + gx] = (float)PlanetNoise.SampleLandmass(spimLat, spimLon, s.NoiseParams, s.PlanetRadiusMeters);
                        }
                        else
                        {
                            paddedValues[gy * paddedSize + gx] = (float)PlanetNoise.SampleCombined(
                                worldX, worldY, refLatDeg, refLonDeg, s.NoiseParams, s.PlanetRadiusMeters, plates);
                        }
                    }
                }

                var padded = new TerrainHeightmap("scratch", paddedOriginX, paddedOriginY,
                    s.CellSizeMeters, paddedSize, paddedSize, paddedValues);
                var locked = new bool[paddedSize * paddedSize];

                // Look up all 4 neighbors by their (seed, position)-derived id — NOT by "is this
                // run's own col/row > 0", since a neighbor may already exist from an EARLIER,
                // entirely separate Run() call (e.g. filling in the region next to one generated
                // last week). db.LoadHeightmap simply returns null for a neighbor that doesn't
                // exist yet in either case, so no run-relative guard is needed.
                void LockAgainst(TerrainHeightmap? neighbor, int gxStart, int gxEnd, int gyStart, int gyEnd)
                {
                    if (neighbor is null) return;

                    // Defensive: a neighbor saved by an earlier corrupted run can carry NaN/Infinity — invalidate it rather than smuggling that into this tile's own grid.
                    if (HasNonFiniteValues(neighbor.Values))
                    {
                        ReportProgress($"[{row},{col}] {id}: soused {neighbor.Id} obsahuje neplatná data (NaN/Infinity) — nejspíš pozůstatek staršího/rozbitého běhu; dlaždice byla z DB smazána, tento okraj se negeneruje zamčený proti ní.");
                        db.DeleteHeightmap(neighbor.Id);
                        return;
                    }

                    for (var gy = gyStart; gy < gyEnd; gy++)
                    {
                        var worldY = paddedOriginY + gy * s.CellSizeMeters;
                        for (var gx = gxStart; gx < gxEnd; gx++)
                        {
                            var worldX = paddedOriginX + gx * s.CellSizeMeters;
                            var idx = gy * paddedSize + gx;
                            padded.Values[idx] = (float)neighbor.SampleAt(worldX, worldY);
                            locked[idx] = true;
                        }
                    }
                }

                TerrainHeightmap? LoadNeighbor(int neighborRow, int neighborCol)
                {
                    var (lat, lon) = TileCenter(neighborRow, neighborCol);
                    return db.LoadHeightmap(TileId(s.NoiseParams.Seed, lat, lon));
                }

                LockAgainst(LoadNeighbor(row, col - 1), 0, margin, 0, paddedSize); // west
                LockAgainst(LoadNeighbor(row, col + 1), paddedSize - margin, paddedSize, 0, paddedSize); // east
                LockAgainst(LoadNeighbor(row - 1, col), 0, paddedSize, 0, margin); // south
                LockAgainst(LoadNeighbor(row + 1, col), 0, paddedSize, paddedSize - margin, paddedSize); // north

                if (s.SpimParams is { } spimParams)
                {
                    var uplift = StreamPowerErosion.UpliftFieldFromPlates(padded, plates, refLatDeg, refLonDeg, s.PlanetRadiusMeters);
                    double[]? erodibilityPerCell = null;
                    double[]? crustDensityPerCell = null;
                    if (s.RockTypeParams is { } rockTypeParams)
                    {
                        var rockTypes = RockLayer.ComputeRockTypeMap(padded, plates, refLatDeg, refLonDeg, s.PlanetRadiusMeters, rockTypeParams);
                        erodibilityPerCell = RockLayer.ErodibilityKPerCell(rockTypes);
                        crustDensityPerCell = RockLayer.DensityPerCell(rockTypes);
                    }
                    // Computed ONCE against the pre-SPIM (landmass-only) terrain — see Erode's remarks on precipitationWeightPerCell.
                    var precipitationWeight = s.OrographicParams is { } orographicParams
                        ? OrographicPrecipitation.ComputePrecipitationField(padded, orographicParams)
                        : null;
                    void LogSpimDiagnostic(string message) => ReportProgress($"[{row},{col}] {id}: {message}");
                    StreamPowerErosion.Erode(padded, spimParams, uplift, locked, erodibilityPerCell, s.IsostasyParams, crustDensityPerCell, precipitationWeight, LogSpimDiagnostic);
                }

                TileErosion.Erode(padded, s.ErosionParams, locked);

                var interior = new float[cellsPerTile * cellsPerTile];
                for (var iy = 0; iy < cellsPerTile; iy++)
                {
                    var py = iy + margin;
                    for (var ix = 0; ix < cellsPerTile; ix++)
                    {
                        var px = ix + margin;
                        interior[iy * cellsPerTile + ix] = padded.Values[py * paddedSize + px];
                    }
                }

                // River mask left null here even when hydrology is on — filled in by the batch
                // pass below, which needs every tile's elevation already saved (for the NEXT
                // tile's own neighbor-locking above) before it can compute anything.
                var tile = new TerrainHeightmap(id, tileOriginX, tileOriginY, s.CellSizeMeters,
                    cellsPerTile, cellsPerTile, interior);
                db.SaveHeightmap(tile);

                var result = new TileResult(row, col, id, centerLat, centerLon);
                ReportProgress($"[{row},{col}] {id}  lat={centerLat:0.000} lon={centerLon:0.000}");
                ReportTileProgress();
                return (result, (row, col, id, centerLat, centerLon, interior));
        }

        // Diagonal-wavefront schedule — see Run's remarks for the correctness proof. Diagonal d
        // holds every (row,col) with row+col==d; diagonals are processed strictly in order (a
        // barrier between them), but tiles WITHIN one diagonal are provably independent and run
        // concurrently when s.MaxDegreeOfParallelism > 1.
        var maxDegreeOfParallelism = Math.Max(1, s.MaxDegreeOfParallelism);
        for (var diagonal = 0; diagonal <= rows + cols - 2; diagonal++)
        {
            var minColInDiagonal = Math.Max(0, diagonal - (rows - 1));
            var maxColInDiagonal = Math.Min(cols - 1, diagonal);
            var tileCountInDiagonal = maxColInDiagonal - minColInDiagonal + 1;
            if (tileCountInDiagonal <= 0) continue;

            void ProcessDiagonalOffset(int offset)
            {
                var col = minColInDiagonal + offset;
                var row = diagonal - col;
                var (result, batchEntry) = GenerateOneTile(row, col);
                var slot = row * cols + col;
                resultSlots[slot] = result;
                if (batchTileSlots is not null) batchTileSlots[slot] = batchEntry;
            }

            if (maxDegreeOfParallelism <= 1 || tileCountInDiagonal <= 1)
            {
                for (var offset = 0; offset < tileCountInDiagonal; offset++) ProcessDiagonalOffset(offset);
            }
            else
            {
                try
                {
                    Parallel.For(0, tileCountInDiagonal,
                        new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                        ProcessDiagonalOffset);
                }
                catch (AggregateException ex)
                {
                    throw ex.Flatten().InnerExceptions[0];
                }
            }
        }

        var results = resultSlots.Select(r => r!).ToList();
        var batchTiles = batchTileSlots?.Select(t => t!.Value).ToList();

        if (batchTiles is { Count: > 0 } && s.HydrologyParams is { } hydrologyParams)
        {
            ReportProgress("Generuji hydrologii (řeky)...");
            var chunkTilesPerSide = Math.Max(1, s.HydrologyChunkTilesPerSide);
            // Tallied so a run finding zero channels anywhere can hint why (see PlanetNoise.SampleCombined).
            var totalRiverCellsInRun = 0L;
            // Group tiles by chunk coordinate (keeping each tile's batchTiles index, so its Interior
            // can be released there once the chunk is done), ordered like generation above.
            var chunks = batchTiles
                .Select((t, i) => (Tile: t, Index: i))
                .GroupBy(x => (ChunkRow: x.Tile.Row / chunkTilesPerSide, ChunkCol: x.Tile.Col / chunkTilesPerSide))
                .OrderBy(g => g.Key.ChunkRow).ThenBy(g => g.Key.ChunkCol)
                .ToList();

            // Chunks have no ordering dependency at all — each owns a disjoint tile set and a
            // unique river network id — so this is a plain parallel loop, no diagonal scheme needed.
            void ProcessChunk(IGrouping<(int ChunkRow, int ChunkCol), (
                (int Row, int Col, string Id, double CenterLat, double CenterLon, float[] Interior) Tile, int Index)> chunk)
            {
                var chunkEntries = chunk.ToList();
                var chunkTiles = chunkEntries.Select(x => x.Tile).ToList();
                var minRow = chunkTiles.Min(t => t.Row);
                var minCol = chunkTiles.Min(t => t.Col);
                var chunkRows = chunkTiles.Max(t => t.Row) - minRow + 1;
                var chunkCols = chunkTiles.Max(t => t.Col) - minCol + 1;

                // long first so an oversized chunk fails via ValidateHydrologyChunkSize, not a silent int overflow.
                var chunkBigWidthLong = (long)chunkCols * cellsPerTile;
                var chunkBigHeightLong = (long)chunkRows * cellsPerTile;
                ValidateHydrologyChunkSize(chunkBigWidthLong, chunkBigHeightLong, chunkTilesPerSide,
                    minRow, minRow + chunkRows - 1, minCol, minCol + chunkCols - 1);

                var chunkBigWidth = (int)chunkBigWidthLong;
                var chunkBigHeight = (int)chunkBigHeightLong;

                // Stitch this chunk's tiles into a combined grid, offset so it starts at local (0,0).
                var combined = new float[chunkBigWidth * chunkBigHeight];
                foreach (var t in chunkTiles)
                {
                    var baseX = (t.Col - minCol) * cellsPerTile;
                    var baseY = (t.Row - minRow) * cellsPerTile;
                    for (var iy = 0; iy < cellsPerTile; iy++)
                    {
                        var srcRow = iy * cellsPerTile;
                        var dstRow = (baseY + iy) * chunkBigWidth + baseX;
                        Array.Copy(t.Interior, srcRow, combined, dstRow, cellsPerTile);
                    }
                }

                var chunkOriginX = swX + minCol * s.TileSizeMeters;
                var chunkOriginY = swY + minRow * s.TileSizeMeters;
                var combinedGrid = new TerrainHeightmap("batch-combined-chunk", chunkOriginX, chunkOriginY,
                    s.CellSizeMeters, chunkBigWidth, chunkBigHeight, combined);
                var (straightRiverMask, riverAccumulation, riverSlope, riverDownstream, riverTopoOrder, riverStrahlerOrder, riverShreveMagnitude) =
                    TileHydrology.ComputeDiagnostics(combinedGrid, hydrologyParams);
                // Bends the straight D8 backbone into a meandering path, severs cutoffs into an OxbowMask, and
                // extracts the persisted graph — dual-write alongside the raster, see docs/plans/river-network-graph-model.md.
                var networkId = $"{s.NoiseParams.Seed}_chunk_{minRow}_{minCol}";
                var (combinedRiverMask, combinedShreveMagnitude, combinedOxbowMask, riverNetwork) = RiverMeander.ApplyMeanderWithGraph(combinedGrid, networkId, straightRiverMask,
                    riverAccumulation, riverSlope, riverDownstream, riverTopoOrder, riverStrahlerOrder, riverShreveMagnitude, new RiverMeander.Parameters());

                // Stage 4: the graph saved below is the only persisted river data now — RiverMask/etc stay null here.
                foreach (var t in chunkTiles)
                {
                    var tile = new TerrainHeightmap(t.Id, swX + t.Col * s.TileSizeMeters, swY + t.Row * s.TileSizeMeters,
                        s.CellSizeMeters, cellsPerTile, cellsPerTile, t.Interior);
                    db.SaveHeightmap(tile);
                }

                db.SaveRiverNetwork(riverNetwork);

                var chunkRiverCells = combinedRiverMask.Count(b => b != 0);
                Interlocked.Add(ref totalRiverCellsInRun, chunkRiverCells);

                // Saved per chunk above — a crash on a later chunk doesn't lose this one's data.
                ReportProgress($"hydrology chunk rows[{minRow}..{minRow + chunkRows - 1}] cols[{minCol}..{minCol + chunkCols - 1}]: {chunkTiles.Count} tiles saved with rivers ({chunkRiverCells} river cells)");

                // Release this chunk's Interior arrays from batchTiles now that they're persisted, so peak memory tracks one chunk, not the whole region.
                // Distinct chunks never share a tile, so distinct chunks' index writes never collide.
                foreach (var entry in chunkEntries)
                {
                    var t = entry.Tile;
                    batchTiles[entry.Index] = (t.Row, t.Col, t.Id, t.CenterLat, t.CenterLon, Array.Empty<float>());
                }
            }

            if (maxDegreeOfParallelism <= 1)
            {
                foreach (var chunk in chunks) ProcessChunk(chunk);
            }
            else
            {
                try
                {
                    Parallel.ForEach(chunks, new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism }, ProcessChunk);
                }
                catch (AggregateException ex)
                {
                    throw ex.Flatten().InnerExceptions[0];
                }
            }

            if (totalRiverCellsInRun == 0)
                ReportProgress("Pozn.: žádná buňka nesplnila --river-threshold — region má patrně zanedbatelný reliéf " +
                    "(mimo sbíhavou/rozbíhavou hranici desek reliéf pohoří vychází 0, viz PlanetNoise.SampleCombined); " +
                    "zkus oblast blíž tektonické hranici (--scan-detail), nebo --tectonic-plates 0.");
        }

        return results;
    }

    /// <summary>True if any value in <paramref name="values"/> is NaN or +/-Infinity — see <c>LockAgainst</c>'s neighbor-invalidation check.</summary>
    private static bool HasNonFiniteValues(float[] values)
    {
        foreach (var v in values)
            if (!float.IsFinite(v)) return true;
        return false;
    }

    /// <summary>Soft ceiling on a chunk's combined-grid cell count, well under the int.MaxValue array-length limit.</summary>
    private const long MaxSafeHydrologyChunkCells = 200_000_000;

    /// <summary>Throws an actionable exception if a hydrology chunk's cell count exceeds <see cref="MaxSafeHydrologyChunkCells"/>.</summary>
    internal static void ValidateHydrologyChunkSize(long chunkBigWidth, long chunkBigHeight, int chunkTilesPerSide,
        int minRow, int maxRowInclusive, int minCol, int maxColInclusive)
    {
        if (chunkBigWidth * chunkBigHeight > MaxSafeHydrologyChunkCells)
            throw new InvalidOperationException(
                $"Hydrology chunk at tile rows [{minRow}..{maxRowInclusive}], cols [{minCol}..{maxColInclusive}] " +
                $"would need a {chunkBigWidth}x{chunkBigHeight} combined grid ({chunkBigWidth * chunkBigHeight:N0} cells) " +
                $"— reduce RunSettings.HydrologyChunkTilesPerSide (currently {chunkTilesPerSide}) or increase CellSizeMeters/decrease TileSizeMeters.");
    }
}
