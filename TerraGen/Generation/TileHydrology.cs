using GameEngineTools.World.Data;

namespace TerraGen.Generation;

/// <summary>
/// D8 flow-direction + flow-accumulation river extraction — derives WHERE rivers actually form
/// from the terrain's own drainage pattern, instead of TerrainEditor's manual height-threshold
/// painting (which is what <see cref="TerrainHeightmap.RiverMask"/> has only ever held until
/// now). Runs per-tile on the SAME padded, already-eroded grid <see cref="TileErosion"/> just
/// finished with (see <see cref="TileGenerator"/>), so a river naturally continues across a tile
/// boundary the same way erosion's own locked margin already does. This is a LOCAL approximation
/// — drainage context only extends as far as the padding margin, not a true whole-planet
/// watershed model — the same already-accepted tradeoff <see cref="TileErosion"/> and the
/// mountain-ridge noise layer both make, not a new one.
/// </summary>
public static class TileHydrology
{
    public sealed record Parameters(
        /// <summary>Minimum flow accumulation (upstream cell count, including the cell itself)
        /// for a cell to be marked as river. Higher = fewer, larger rivers only; lower = a denser
        /// network that also picks up small streams.</summary>
        int FlowAccumulationThreshold = 50);

    /// <summary>Tiny per-hop elevation bump <see cref="FillDepressions"/> adds while flooding a pit
    /// or a flat plateau — small enough to never visibly distort real terrain (real slopes differ
    /// by orders of magnitude more), but enough to keep the filled surface STRICTLY monotonic
    /// outward from any point to the tile's own boundary, so <see cref="SteepestDescentNeighbor"/>
    /// always has a well-defined single downhill direction even across what was originally a dead
    /// flat basin.</summary>
    private const float FillEpsilon = 1e-3f;

    /// <summary>Computes a river mask via single-flow-direction (D8) accumulation: every cell
    /// starts with 1 unit of "rainfall" and drains to its single steepest downhill 8-connected
    /// neighbor; accumulation sums along that path. Cells at or above
    /// <see cref="Parameters.FlowAccumulationThreshold"/> are marked as river. Returns a 0/1 byte
    /// mask the same length/shape as <paramref name="grid"/>'s own <c>Values</c> — the exact
    /// convention <see cref="TerrainHeightmap.RiverMask"/> already uses.</summary>
    /// <remarks>
    /// Routes flow across a <see cref="FillDepressions"/>-filled copy of the elevation, not the raw
    /// values — ridged mountain noise is riddled with tiny local pits (every little bump creates
    /// one), and without filling them first, D8 flow dead-ends in the very first pit it meets: the
    /// visible symptom is a field of short, disconnected river dashes that never join into a longer
    /// channel, instead of real rivers threading continuously downhill to the tile's edge. Filling
    /// doesn't change the terrain itself (<paramref name="grid"/>'s own <c>Values</c> are untouched)
    /// — only the elevation surface used to DECIDE which way water flows.
    /// </remarks>
    public static byte[] ComputeRiverMask(TerrainHeightmap grid, Parameters p)
    {
        var width = grid.Width;
        var height = grid.Height;
        var count = width * height;

        var filled = FillDepressions(grid);

        var downstream = new int[count]; // -1 = no strictly-downhill neighbor (only possible at a grid edge)
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                downstream[y * width + x] = SteepestDescentNeighbor(filled, width, height, x, y);

        // Flow only ever moves to STRICTLY lower ground (see SteepestDescentNeighbor), so
        // processing cells from highest to lowest elevation guarantees every cell's upstream
        // contributors have already added their share to it before it, in turn, drains onward —
        // one O(n log n) sorted pass instead of an iterative graph solve.
        var order = new int[count];
        for (var i = 0; i < count; i++) order[i] = i;
        Array.Sort(order, (a, b) => filled[b].CompareTo(filled[a]));

        var accumulation = new int[count];
        for (var i = 0; i < count; i++) accumulation[i] = 1;

        foreach (var idx in order)
        {
            var next = downstream[idx];
            if (next >= 0)
                accumulation[next] += accumulation[idx];
        }

        var mask = new byte[count];
        for (var i = 0; i < count; i++)
            mask[i] = accumulation[i] >= p.FlowAccumulationThreshold ? (byte)1 : (byte)0;

        return mask;
    }

    /// <summary>
    /// Priority-Flood depression filling (Barnes, Lehman &amp; Mulla 2014) — returns a copy of
    /// <paramref name="grid"/>'s elevations where every local pit has been raised to its lowest
    /// pour point, so a strictly-downhill path exists from every cell to the grid's own boundary
    /// (treated as the drain: this is the padded tile's outer edge, already the local-approximation
    /// boundary every other part of the hydrology/erosion pipeline accepts — see the type-level
    /// remarks). <see cref="FillEpsilon"/> nudges each flooded cell a hair above the one that filled
    /// it, so even a perfectly flat filled basin still has a unique steepest-descent direction back
    /// toward its pour point instead of leaving every cell in it tied at 0 slope.
    /// </summary>
    private static float[] FillDepressions(TerrainHeightmap grid)
    {
        var width = grid.Width;
        var height = grid.Height;
        var filled = new float[grid.Values.Length];
        Array.Fill(filled, float.PositiveInfinity);

        var visited = new bool[filled.Length];
        var queue = new PriorityQueue<int, float>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x != 0 && x != width - 1 && y != 0 && y != height - 1) continue;
                var idx = y * width + x;
                filled[idx] = grid.Values[idx];
                visited[idx] = true;
                queue.Enqueue(idx, filled[idx]);
            }
        }

        while (queue.TryDequeue(out var idx, out var elevation))
        {
            var x = idx % width;
            var y = idx / width;

            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;

                    var nIdx = ny * width + nx;
                    if (visited[nIdx]) continue;

                    var candidate = Math.Max(grid.Values[nIdx], elevation + FillEpsilon);
                    filled[nIdx] = candidate;
                    visited[nIdx] = true;
                    queue.Enqueue(nIdx, candidate);
                }
            }
        }

        return filled;
    }

    /// <summary>Index of the 8-connected neighbor with the steepest downhill slope from (x,y) in
    /// <paramref name="values"/> — <c>-1</c> only at the grid's own boundary (nothing outside it to
    /// compare against), since <see cref="FillDepressions"/> guarantees every interior cell has a
    /// strictly lower neighbor somewhere.</summary>
    private static int SteepestDescentNeighbor(float[] values, int width, int height, int x, int y)
    {
        var here = values[y * width + x];

        var bestIdx = -1;
        var bestSlope = 0.0; // must be > 0 (strictly downhill) to ever replace the "no descent" default

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;

                var neighborHeight = values[ny * width + nx];
                var distance = dx != 0 && dy != 0 ? 1.4142135623730951 : 1.0; // diagonal vs orthogonal step
                var slope = (here - neighborHeight) / distance;
                if (slope > bestSlope)
                {
                    bestSlope = slope;
                    bestIdx = ny * width + nx;
                }
            }
        }

        return bestIdx;
    }
}
