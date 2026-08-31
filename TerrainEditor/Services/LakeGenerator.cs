using GameEngineTools.World.Data;

namespace TerrainEditor.Services;

/// <summary>
/// Finds basins (local minima above sea level) in a heightmap and floods them up to a bounded
/// depth/size, marking the result the same way the River brush does (<see cref="TerrainHeightmap.RiverMask"/>)
/// — a lake is just a filled-in area of the same freshwater layer, rendered identically.
/// A simplified, size/depth-bounded priority-flood rather than full watershed depression-filling.
/// </summary>
public static class LakeGenerator
{
    public sealed record Parameters(
        int MaxLakes = 8,
        int MaxCellsPerLake = 400,
        /// <summary>Never flood higher than this many meters above a basin's floor.</summary>
        double MaxDepthMeters = 15.0,
        /// <summary>Minimum grid-cell distance between two basins so lakes don't crowd each other.</summary>
        double MinBasinSeparationCells = 6.0,
        /// <summary>Basins smaller than this many cells once flooded are discarded as too small to bother with.</summary>
        int MinLakeCells = 3);

    /// <summary>
    /// Mutates <paramref name="grid"/>'s <c>RiverMask</c> in place. Caller must ensure it's
    /// already allocated.
    /// </summary>
    /// <param name="protectedLocations">
    /// World-space points (e.g. loaded location markers) whose exact cell is never flooded —
    /// a lake can still form right next to one (lakeside is fine), it just won't swallow it.
    /// </param>
    /// <returns>Number of lakes generated.</returns>
    public static int Generate(TerrainHeightmap grid, Parameters parameters,
        IReadOnlyCollection<(double X, double Y)>? protectedLocations = null)
    {
        if (grid.RiverMask is null)
            throw new InvalidOperationException("Grid.RiverMask must be allocated before generating lakes.");

        var basins = FindLocalMinima(grid, parameters.MinBasinSeparationCells)
            .Where(idx => grid.Values[idx] >= 0) // already-ocean cells aren't "basins" to fill
            .OrderBy(idx => grid.Values[idx])    // fill the deepest basins first
            .ToList();

        var claimed = new HashSet<int>();
        if (protectedLocations is not null)
        {
            foreach (var (worldX, worldY) in protectedLocations)
            {
                var gx = (int)Math.Round((worldX - grid.OriginX) / grid.CellSizeMeters);
                var gy = (int)Math.Round((worldY - grid.OriginY) / grid.CellSizeMeters);
                if (gx >= 0 && gx < grid.Width && gy >= 0 && gy < grid.Height)
                    claimed.Add(gy * grid.Width + gx);
            }
        }

        var lakesGenerated = 0;

        foreach (var seedIdx in basins)
        {
            if (lakesGenerated >= parameters.MaxLakes) break;
            if (claimed.Contains(seedIdx)) continue;

            var floorHeight = grid.Values[seedIdx];
            var lakeCells = FloodBasin(grid, seedIdx, floorHeight, parameters.MaxDepthMeters, parameters.MaxCellsPerLake, claimed);

            if (lakeCells.Count < parameters.MinLakeCells)
            {
                claimed.Add(seedIdx); // don't retry this dead-end seed
                continue;
            }

            foreach (var idx in lakeCells)
            {
                grid.RiverMask[idx] = 1;
                claimed.Add(idx);
            }
            lakesGenerated++;
        }

        return lakesGenerated;
    }

    /// <summary>Cells with no strictly-lower 8-connected neighbor, deduplicated so a cluster of
    /// tied minima (e.g. a flat basin floor) contributes only one seed.</summary>
    private static List<int> FindLocalMinima(TerrainHeightmap grid, double minSeparationCells)
    {
        var minima = new List<int>();
        for (var gy = 0; gy < grid.Height; gy++)
        {
            for (var gx = 0; gx < grid.Width; gx++)
            {
                var idx = gy * grid.Width + gx;
                var h = grid.Values[idx];
                var isMinimum = true;

                for (var dy = -1; dy <= 1 && isMinimum; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var nx = gx + dx;
                        var ny = gy + dy;
                        if (nx < 0 || nx >= grid.Width || ny < 0 || ny >= grid.Height) continue;
                        if (grid.Values[ny * grid.Width + nx] < h)
                        {
                            isMinimum = false;
                            break;
                        }
                    }
                }

                if (isMinimum) minima.Add(idx);
            }
        }

        var result = new List<int>();
        foreach (var idx in minima)
        {
            var gx = idx % grid.Width;
            var gy = idx / grid.Width;
            var tooClose = result.Any(otherIdx =>
            {
                var ox = otherIdx % grid.Width;
                var oy = otherIdx / grid.Width;
                var dist = Math.Sqrt((gx - ox) * (gx - ox) + (gy - oy) * (gy - oy));
                return dist < minSeparationCells;
            });
            if (!tooClose) result.Add(idx);
        }
        return result;
    }

    /// <summary>Priority-flood outward from a basin seed: always expand into the lowest
    /// available unvisited neighbor next, stopping at the depth/size caps.</summary>
    private static List<int> FloodBasin(TerrainHeightmap grid, int seedIdx, double floorHeight,
        double maxDepth, int maxCells, HashSet<int> alreadyClaimed)
    {
        var included = new List<int>();
        var frontier = new PriorityQueue<int, float>();
        var seen = new HashSet<int> { seedIdx };
        frontier.Enqueue(seedIdx, grid.Values[seedIdx]);

        while (frontier.Count > 0 && included.Count < maxCells)
        {
            var idx = frontier.Dequeue();
            if (alreadyClaimed.Contains(idx)) continue;

            var height = grid.Values[idx];
            if (height - floorHeight > maxDepth) continue; // would overflow the basin rim

            included.Add(idx);

            var gx = idx % grid.Width;
            var gy = idx / grid.Width;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = gx + dx;
                    var ny = gy + dy;
                    if (nx < 0 || nx >= grid.Width || ny < 0 || ny >= grid.Height) continue;
                    var nIdx = ny * grid.Width + nx;
                    if (!seen.Add(nIdx)) continue;
                    frontier.Enqueue(nIdx, grid.Values[nIdx]);
                }
            }
        }

        return included;
    }
}
