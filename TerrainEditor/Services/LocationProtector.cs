using GameEngineTools.World.Data;

namespace TerrainEditor.Services;

/// <summary>
/// Keeps known locations on dry land after terrain generation — procedural noise has no idea
/// where the world's locations are, so without this a location can easily land underwater.
/// </summary>
public static class LocationProtector
{
    /// <summary>
    /// For every location whose sampled elevation is below <paramref name="minDryElevationMeters"/>,
    /// raises the terrain around it just enough to clear that floor, and provably so: the exact
    /// (worldX, worldY) point is guaranteed to sample at exactly <paramref name="minDryElevationMeters"/>
    /// afterward, not just "probably close enough".
    /// </summary>
    /// <remarks>
    /// <see cref="TerrainHeightmap.SampleAt"/> bilinearly interpolates the 4 grid cells
    /// surrounding the query point; raising only the single nearest (rounded) cell with a
    /// falloff — the previous approach — leaves the interpolation blending a fully-raised
    /// corner with barely-raised neighbors, which can undershoot the target at the exact query
    /// point even though the "center" cell looks fixed. Raising all 4 surrounding corners by
    /// the identical deficit first closes that gap exactly: since bilinear weights always sum
    /// to 1, a uniform +deficit to all 4 corners shifts the interpolated result by exactly
    /// +deficit, regardless of where within the cell the point falls. The radius-limited
    /// falloff beyond those 4 corners is then purely cosmetic (a soft surrounding mound), not
    /// load-bearing for correctness.
    /// </remarks>
    public static void KeepLocationsDry(TerrainHeightmap grid, IEnumerable<(double X, double Y)> locations,
        double minDryElevationMeters = 2.0, double radiusCells = 6.0)
    {
        foreach (var (worldX, worldY) in locations)
        {
            var currentHeight = grid.SampleAt(worldX, worldY);
            if (currentHeight >= minDryElevationMeters) continue;

            var deficit = minDryElevationMeters - currentHeight;

            var gxFrac = (worldX - grid.OriginX) / grid.CellSizeMeters;
            var gyFrac = (worldY - grid.OriginY) / grid.CellSizeMeters;
            var x0 = (int)Math.Floor(gxFrac);
            var y0 = (int)Math.Floor(gyFrac);

            // The exact-guarantee step: all 4 bilinear corners get the identical deficit.
            RaiseIfInBounds(grid, x0, y0, deficit);
            RaiseIfInBounds(grid, x0 + 1, y0, deficit);
            RaiseIfInBounds(grid, x0, y0 + 1, deficit);
            RaiseIfInBounds(grid, x0 + 1, y0 + 1, deficit);

            // Cosmetic falloff mound around those corners, blending into the surrounding terrain.
            var gx = (int)Math.Round(gxFrac);
            var gy = (int)Math.Round(gyFrac);
            var r = (int)Math.Ceiling(radiusCells);

            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    // The immediate 1-cell neighborhood was already handled exactly above.
                    if (dist > radiusCells || dist <= 1.5) continue;
                    var nx = gx + dx;
                    var ny = gy + dy;
                    if (nx < 0 || nx >= grid.Width || ny < 0 || ny >= grid.Height) continue;

                    var falloff = 1.0 - dist / radiusCells;
                    var idx = ny * grid.Width + nx;
                    grid.Values[idx] += (float)(deficit * falloff);
                }
            }
        }
    }

    private static void RaiseIfInBounds(TerrainHeightmap grid, int x, int y, double amount)
    {
        if (x < 0 || x >= grid.Width || y < 0 || y >= grid.Height) return;
        grid.Values[y * grid.Width + x] += (float)amount;
    }
}
