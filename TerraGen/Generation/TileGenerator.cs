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
        double MountainOriginLonDeg = 0.0);

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

        // The requested region's corners, converted into the FIXED reference frame — not derived
        // from the region's own center, so the same physical corner always lands at the same flat
        // offset regardless of what other region a future run happens to request. See the
        // type-level remarks for why this fixed-frame choice is what lets separate runs connect.
        var (swX, swY) = PlanetNoise.LatLonToOffset(s.LatMin, s.LonMin, refLatDeg, refLonDeg, s.PlanetRadiusMeters);
        var (neX, neY) = PlanetNoise.LatLonToOffset(s.LatMax, s.LonMax, refLatDeg, refLonDeg, s.PlanetRadiusMeters);
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
                            worldX, worldY, refLatDeg, refLonDeg, s.NoiseParams, s.PlanetRadiusMeters);
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
                var tile = new TerrainHeightmap(id, tileOriginX, tileOriginY, s.CellSizeMeters,
                    cellsPerTile, cellsPerTile, interior);
                db.SaveHeightmap(tile);

                var result = new TileResult(row, col, id, centerLat, centerLon);
                results.Add(result);
                onProgress?.Invoke($"[{row},{col}] {id}  lat={centerLat:0.000} lon={centerLon:0.000}");
            }
        }

        return results;
    }
}
