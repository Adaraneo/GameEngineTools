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

    /// <summary>Computes a river mask via single-flow-direction (D8) accumulation: every cell
    /// starts with 1 unit of "rainfall" and drains to its single steepest downhill 8-connected
    /// neighbor; accumulation sums along that path. Cells at or above
    /// <see cref="Parameters.FlowAccumulationThreshold"/> are marked as river. Returns a 0/1 byte
    /// mask the same length/shape as <paramref name="grid"/>'s own <c>Values</c> — the exact
    /// convention <see cref="TerrainHeightmap.RiverMask"/> already uses.</summary>
    public static byte[] ComputeRiverMask(TerrainHeightmap grid, Parameters p)
    {
        var width = grid.Width;
        var height = grid.Height;
        var count = width * height;

        var downstream = new int[count]; // -1 = no strictly-downhill neighbor (a pit, or a grid edge)
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                downstream[y * width + x] = SteepestDescentNeighbor(grid, x, y);

        // Flow only ever moves to STRICTLY lower ground (see SteepestDescentNeighbor), so
        // processing cells from highest to lowest elevation guarantees every cell's upstream
        // contributors have already added their share to it before it, in turn, drains onward —
        // one O(n log n) sorted pass instead of an iterative graph solve.
        var order = new int[count];
        for (var i = 0; i < count; i++) order[i] = i;
        Array.Sort(order, (a, b) => grid.Values[b].CompareTo(grid.Values[a]));

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

    /// <summary>Index of the 8-connected neighbor with the steepest downhill slope from (x,y), or
    /// -1 if every in-bounds neighbor is at or above this cell's own height (a pit, or a grid
    /// edge with nothing lower) — flow simply stops there rather than needing pit-filling, an
    /// accepted simplification: a real depression would become a lake; this treats it as a dead
    /// end for the river network instead.</summary>
    private static int SteepestDescentNeighbor(TerrainHeightmap grid, int x, int y)
    {
        var width = grid.Width;
        var height = grid.Height;
        var here = grid.Values[y * width + x];

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

                var neighborHeight = grid.Values[ny * width + nx];
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
