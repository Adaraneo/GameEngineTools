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
/// neighboring tile at the same position). So hydrology runs ONCE, after every tile in THIS batch
/// has been generated and eroded, over the whole batch's stitched elevation — not once per tile —
/// then the combined river mask is sliced back out per tile for a second, river-mask-only save.
/// That fully connects rivers WITHIN one Run() call, at the cost of holding every tile's elevation
/// in memory simultaneously instead of one at a time (worse memory/CPU for large batches — an
/// accepted tradeoff for connected rivers) and a second SaveHeightmap round-trip per tile. It does
/// NOT connect across two SEPARATE Run() invocations (e.g. filling in a region next to one
/// generated last week) — that would require re-flowing accumulation through already-saved
/// neighbor tiles too, a much bigger, potentially whole-planet-cascading problem; existing
/// neighbor tiles' river masks are left exactly as they already were.
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
        /// <summary><c>null</c> (default) skips river extraction entirely — existing worlds/tiles
        /// generated before this option existed must keep regenerating identically. Non-null
        /// derives <see cref="TerrainHeightmap.RiverMask"/> from the WHOLE requested batch's own
        /// drainage pattern (see <see cref="TileHydrology"/> and the type-level remarks on why this
        /// is computed once per batch, not once per tile) right after erosion, instead of leaving it
        /// null for TerrainEditor's manual painting to be the only way it's ever set.</summary>
        TileHydrology.Parameters? HydrologyParams = null);

    public sealed record TileResult(int Row, int Col, string Id, double CenterLatDeg, double CenterLonDeg);

    /// <summary>Deterministic tile id from the run's seed and the tile's true center position —
    /// stable across re-runs of the same region (idempotent overwrite) and collision-resistant
    /// against unrelated runs at different positions (unlike a bare row/col index, which is only
    /// meaningful relative to one run's own south-west corner).</summary>
    public static string TileId(int seed, double centerLatDeg, double centerLonDeg)
        => $"tile_{seed}_{(int)Math.Round(centerLatDeg * 1000)}_{(int)Math.Round(centerLonDeg * 1000)}";

    /// <summary>Runs the whole batch, saving every generated tile into <paramref name="db"/> and
    /// returning the tiles produced in generation order. <paramref name="onProgress"/> (optional)
    /// receives one human-readable line per completed tile.</summary>
    public static IReadOnlyList<TileResult> Run(SqliteWorldDatabase db, RunSettings s, Action<string>? onProgress = null)
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

        var results = new List<TileResult>(rows * cols);

        // Only populated when s.HydrologyParams is set — holds every tile's post-erosion interior
        // elevation so the batch-wide hydrology pass below can stitch them into one combined grid.
        // Left empty (no extra memory) when rivers are off, matching this method's existing
        // "existing behavior must not change" backward-compatibility rule.
        var batchTiles = s.HydrologyParams is not null
            ? new List<(int Row, int Col, string Id, double CenterLat, double CenterLon, float[] Interior)>(rows * cols)
            : null;

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var tileOriginX = swX + col * s.TileSizeMeters;
                var tileOriginY = swY + row * s.TileSizeMeters;

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
                        paddedValues[gy * paddedSize + gx] = (float)PlanetNoise.SampleCombined(
                            worldX, worldY, refLatDeg, refLonDeg, s.NoiseParams, s.PlanetRadiusMeters, plates);
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

                var (centerLat, centerLon) = TileCenter(row, col);
                var id = TileId(s.NoiseParams.Seed, centerLat, centerLon);
                // River mask left null here even when hydrology is on — filled in by the batch
                // pass below, which needs every tile's elevation already saved (for the NEXT
                // tile's own neighbor-locking above) before it can compute anything.
                var tile = new TerrainHeightmap(id, tileOriginX, tileOriginY, s.CellSizeMeters,
                    cellsPerTile, cellsPerTile, interior);
                db.SaveHeightmap(tile);

                batchTiles?.Add((row, col, id, centerLat, centerLon, interior));

                var result = new TileResult(row, col, id, centerLat, centerLon);
                results.Add(result);
                onProgress?.Invoke($"[{row},{col}] {id}  lat={centerLat:0.000} lon={centerLon:0.000}");
            }
        }

        if (batchTiles is { Count: > 0 } && s.HydrologyParams is { } hydrologyParams)
        {
            // Stitch every tile's interior elevation into one combined grid, in the same row/col
            // layout the loop above already placed them at — see the type-level remarks for why
            // this has to happen once per batch instead of once per tile.
            var bigWidth = cols * cellsPerTile;
            var bigHeight = rows * cellsPerTile;
            var combined = new float[bigWidth * bigHeight];
            foreach (var t in batchTiles)
            {
                var baseX = t.Col * cellsPerTile;
                var baseY = t.Row * cellsPerTile;
                for (var iy = 0; iy < cellsPerTile; iy++)
                {
                    var srcRow = iy * cellsPerTile;
                    var dstRow = (baseY + iy) * bigWidth + baseX;
                    Array.Copy(t.Interior, srcRow, combined, dstRow, cellsPerTile);
                }
            }

            var combinedGrid = new TerrainHeightmap("batch-combined", swX, swY, s.CellSizeMeters,
                bigWidth, bigHeight, combined);
            var (straightRiverMask, riverAccumulation, riverSlope, riverDownstream, riverTopoOrder, riverStrahlerOrder, riverShreveMagnitude) =
                TileHydrology.ComputeDiagnostics(combinedGrid, hydrologyParams);
            // Bends the straight D8 backbone above into a meandering path (see RiverMeander's own
            // remarks for why D8 alone can't produce one) — a shape-only pass, so it never changes
            // which cells actually count as river-worthy, just where within the grid each one draws.
            // The Strahler order and Shreve magnitude threaded through here are what end up baked
            // into each tile's own saved RiverMask/ShreveMagnitude arrays below, instead of a flat 1
            // — see TerrainHeightmap's remarks.
            var (combinedRiverMask, combinedShreveMagnitude) = RiverMeander.ApplyMeander(combinedGrid, straightRiverMask,
                riverAccumulation, riverSlope, riverDownstream, riverTopoOrder, riverStrahlerOrder, riverShreveMagnitude, new RiverMeander.Parameters());

            foreach (var t in batchTiles)
            {
                var baseX = t.Col * cellsPerTile;
                var baseY = t.Row * cellsPerTile;
                var riverMaskInterior = new byte[cellsPerTile * cellsPerTile];
                var shreveMagnitudeInterior = new int[cellsPerTile * cellsPerTile];
                for (var iy = 0; iy < cellsPerTile; iy++)
                {
                    var srcRow = (baseY + iy) * bigWidth + baseX;
                    Array.Copy(combinedRiverMask, srcRow, riverMaskInterior, iy * cellsPerTile, cellsPerTile);
                    Array.Copy(combinedShreveMagnitude, srcRow, shreveMagnitudeInterior, iy * cellsPerTile, cellsPerTile);
                }

                var tile = new TerrainHeightmap(t.Id, swX + t.Col * s.TileSizeMeters, swY + t.Row * s.TileSizeMeters,
                    s.CellSizeMeters, cellsPerTile, cellsPerTile, t.Interior, riverMaskInterior, shreveMagnitudeInterior);
                db.SaveHeightmap(tile);
            }
        }

        return results;
    }
}
