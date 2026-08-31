using GameEngineTools.World.Data;

namespace TerrainEditor.Services;

/// <summary>
/// Traces a river downhill from a spring point via steepest descent: at each step, move to
/// whichever 8-connected neighbor has the lowest elevation, marking cells as river along the
/// way. Stops on reaching the sea (elevation &lt; 0, checked BEFORE carving — carving the spring
/// itself must never be mistaken for "reached the sea"), hitting a local minimum with no lower
/// or flat unvisited neighbor (a basin/lake — a reasonable place to stop without a full
/// depression-filling simulation), re-entering a cell already on this path, or a safety step cap.
/// </summary>
public static class HydrologyGenerator
{
    private const float CarveDepthMeters = 1.5f; // mild — this authors a plausible riverbed, not a canyon

    /// <summary>Search order for the 8 neighbors — fixed so a tie (flat terrain) still produces
    /// a consistent, visible direction instead of depending on iteration order.</summary>
    private static readonly (int Dx, int Dy)[] NeighborOffsets =
    [
        (1, 0), (0, 1), (-1, 0), (0, -1),
        (1, 1), (-1, 1), (1, -1), (-1, -1),
    ];

    /// <summary>
    /// Traces and marks a river from the given world-space spring point. Mutates
    /// <paramref name="grid"/>'s <see cref="TerrainHeightmap.RiverMask"/> and <see cref="TerrainHeightmap.Values"/>
    /// in place — the caller must ensure <c>RiverMask</c> is already allocated (non-null).
    /// </summary>
    /// <returns>Number of cells traced.</returns>
    public static int TraceFromSpring(TerrainHeightmap grid, double springWorldX, double springWorldY, int maxSteps = 0)
    {
        if (grid.RiverMask is null)
            throw new InvalidOperationException("Grid.RiverMask must be allocated before tracing — see MainWindow's lazy-allocate pattern.");

        var gx = Math.Clamp((int)Math.Round((springWorldX - grid.OriginX) / grid.CellSizeMeters), 0, grid.Width - 1);
        var gy = Math.Clamp((int)Math.Round((springWorldY - grid.OriginY) / grid.CellSizeMeters), 0, grid.Height - 1);

        var cap = maxSteps > 0 ? maxSteps : grid.Width * grid.Height;
        var traced = 0;
        var visited = new HashSet<int>();

        for (var step = 0; step < cap; step++)
        {
            var idx = gy * grid.Width + gx;
            if (!visited.Add(idx))
                break; // would re-enter a cell already on this path — stop rather than loop

            var currentHeight = grid.Values[idx];
            grid.RiverMask[idx] = 1;
            traced++;

            // Checked BEFORE carving: carving the spring itself must never look like "reached
            // the sea". Only a cell that was ALREADY at/below sea level counts.
            if (currentHeight < 0)
                break;

            grid.Values[idx] = currentHeight - CarveDepthMeters;

            // Prefer strictly-downhill; if none exists (flat/near-flat terrain — notably the
            // default freshly-created grid, which starts perfectly flat at 0m), fall back to
            // the first flat unvisited neighbor in a fixed direction order so the trace still
            // makes visible progress instead of stopping dead after one cell.
            var bestGx = -1;
            var bestGy = -1;
            var bestHeight = double.PositiveInfinity;
            var foundDownhill = false;

            foreach (var (dx, dy) in NeighborOffsets)
            {
                var nx = gx + dx;
                var ny = gy + dy;
                if (nx < 0 || nx >= grid.Width || ny < 0 || ny >= grid.Height) continue;
                var nIdx = ny * grid.Width + nx;
                if (visited.Contains(nIdx)) continue;

                var nHeight = grid.Values[nIdx];
                if (nHeight < currentHeight)
                {
                    if (!foundDownhill || nHeight < bestHeight)
                    {
                        bestHeight = nHeight;
                        bestGx = nx;
                        bestGy = ny;
                        foundDownhill = true;
                    }
                }
                else if (!foundDownhill && bestGx == -1 && Math.Abs(nHeight - currentHeight) < 1e-6)
                {
                    bestGx = nx;
                    bestGy = ny;
                }
            }

            if (bestGx == -1)
                break; // nowhere lower or flat-and-unvisited to flow to — a genuine basin

            gx = bestGx;
            gy = bestGy;
        }

        return traced;
    }
}
