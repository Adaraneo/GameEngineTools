using GameEngineTools.World.Data;

namespace TerraGen.Generation;

/// <summary>Shared D8 flow-routing primitives, extracted out of <see cref="TileHydrology"/> (previously the only consumer) so <see cref="StreamPowerErosion"/> reuses the same routing instead of a second copy.</summary>
public static class FlowRouting
{
    /// <summary>Per-hop elevation bump <see cref="FillDepressions"/> adds while flooding a pit, keeping the filled surface strictly monotonic toward the tile boundary.</summary>
    internal const float FillEpsilon = 1e-3f;

    /// <summary>Near-equal filled-elevation gap (in <see cref="FillEpsilon"/> steps) for two cells to count as the same flat component in <see cref="ResolveFlats"/>.</summary>
    private const float FlatEqualityTolerance = FillEpsilon * 4f;

    /// <summary>Filled=pit-filled elevation; Routed=Filled+flat-gradient (what Downstream/Stack derive from); Downstream=steepest-descent neighbor index (-1 at grid boundary); Stack=cell indices highest-to-lowest by Routed (Braun &amp; Willett 2013 §2.1 topological order); Accumulation=D8 flow accumulation. All arrays are length grid.Width*grid.Height.</summary>
    public readonly record struct Result(float[] Filled, float[] Routed, int[] Downstream, int[] Stack, int[] Accumulation);

    /// <summary>Computes the full D8 routing package for <paramref name="grid"/>: fill, resolve, direction, topological stack, accumulation.</summary>
    public static Result Compute(TerrainHeightmap grid)
    {
        var width = grid.Width;
        var height = grid.Height;
        var count = width * height;

        var filled = FillDepressions(grid);
        var routed = ResolveFlats(filled, width, height);

        var downstream = new int[count]; // -1 = no strictly-downhill neighbor (only possible at a grid edge)
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                downstream[y * width + x] = SteepestDescentNeighbor(routed, width, height, x, y);

        // Highest-to-lowest order guarantees every cell's upstream contributors are processed first.
        var stack = new int[count];
        for (var i = 0; i < count; i++) stack[i] = i;
        Array.Sort(stack, (a, b) => routed[b].CompareTo(routed[a]));

        var accumulation = new int[count];
        for (var i = 0; i < count; i++) accumulation[i] = 1;

        foreach (var idx in stack)
        {
            var next = downstream[idx];
            if (next >= 0)
                accumulation[next] += accumulation[idx];
        }

        return new Result(filled, routed, downstream, stack, accumulation);
    }

    /// <summary>D8-weighted flow accumulation reusing an already-computed <see cref="Result.Downstream"/>/<see cref="Result.Stack"/> — each cell starts with <paramref name="weightPerCell"/> instead of a flat 1 unit, so the result is a precipitation- (or any other per-cell factor-) weighted drainage area, Stage 4's Task 4.1.3.</summary>
    public static double[] ComputeWeightedAccumulation(int[] downstream, int[] stack, double[] weightPerCell)
    {
        var accumulation = (double[])weightPerCell.Clone();
        foreach (var idx in stack)
        {
            var next = downstream[idx];
            if (next >= 0) accumulation[next] += accumulation[idx];
        }
        return accumulation;
    }

    /// <summary>Priority-Flood depression filling. Source: Barnes, Lehman &amp; Mulla (2014), Computers &amp; Geosciences 62:117-127, doi:10.1016/j.cageo.2013.04.024.</summary>
    /// <remarks>⚠ This already IS an O(n log n) priority-flood construction, not the "epsilon-increment" filler the SPIM plan's Task 1.1.2 assumed exists — flagged rather than adding a redundant second algorithm.</remarks>
    internal static float[] FillDepressions(TerrainHeightmap grid)
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

    /// <summary>Garbrecht &amp; Martz (1997) flat-surface resolution — adds a synthetic gradient per flat component so flow converges to one exit instead of smearing as a sheet.</summary>
    internal static float[] ResolveFlats(float[] filled, int width, int height)
    {
        var count = width * height;
        var componentId = new int[count];
        Array.Fill(componentId, -1);
        var components = new List<List<int>>();

        for (var start = 0; start < count; start++)
        {
            if (componentId[start] != -1) continue;

            var members = new List<int> { start };
            componentId[start] = -2; // provisional: "visited, component not finalized yet"
            var stack = new Stack<int>();
            stack.Push(start);

            while (stack.Count > 0)
            {
                var idx = stack.Pop();
                var x = idx % width;
                var y = idx / width;
                var here = filled[idx];

                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        var nIdx = ny * width + nx;
                        if (componentId[nIdx] != -1) continue;
                        if (Math.Abs(filled[nIdx] - here) > FlatEqualityTolerance) continue;

                        componentId[nIdx] = -2;
                        members.Add(nIdx);
                        stack.Push(nIdx);
                    }
                }
            }

            if (members.Count <= 1)
            {
                componentId[start] = -1; // single cell — no internal routing ambiguity to resolve
                continue;
            }

            var id = components.Count;
            foreach (var m in members) componentId[m] = id;
            components.Add(members);
        }

        var routed = (float[])filled.Clone();

        foreach (var members in components)
        {
            var memberSet = new HashSet<int>(members);
            var indexInMembers = new Dictionary<int, int>(members.Count);
            for (var i = 0; i < members.Count; i++) indexInMembers[members[i]] = i;

            var distToLower = new int[members.Count];
            var distToHigher = new int[members.Count];
            Array.Fill(distToLower, -1);
            Array.Fill(distToHigher, -1);
            var lowerSeeds = new Queue<int>();
            var higherSeeds = new Queue<int>();

            foreach (var idx in members)
            {
                var x = idx % width;
                var y = idx / width;
                var here = filled[idx];
                // Grid boundary is the drain — a flat cell right on it is a guaranteed outflow edge.
                var touchesLower = x == 0 || x == width - 1 || y == 0 || y == height - 1;
                var touchesHigher = false;

                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        var nIdx = ny * width + nx;
                        if (memberSet.Contains(nIdx)) continue;

                        if (filled[nIdx] < here) touchesLower = true;
                        else if (filled[nIdx] > here) touchesHigher = true;
                    }
                }

                var mi = indexInMembers[idx];
                if (touchesLower) { distToLower[mi] = 0; lowerSeeds.Enqueue(idx); }
                if (touchesHigher) { distToHigher[mi] = 0; higherSeeds.Enqueue(idx); }
            }

            BfsFillDistances(lowerSeeds, distToLower, indexInMembers, memberSet, width, height);
            BfsFillDistances(higherSeeds, distToHigher, indexInMembers, memberSet, width, height);

            for (var i = 0; i < members.Count; i++)
            {
                // distToLower always resolves (every flat has a true outflow edge); distToHigher can
                // legitimately stay unresolved (no adjacent wall) — treated as "no wall bias".
                var toLower = distToLower[i] < 0 ? 0 : distToLower[i];
                var toHigher = distToHigher[i] < 0 ? 0 : distToHigher[i];
                // +toLower pushes cells far from the exit up; -toHigher pulls cells far from the wall down — sign found live via a propagation regression test.
                routed[members[i]] = filled[members[i]] + FillEpsilon * (2f * toLower - toHigher);
            }
        }

        return routed;
    }

    private static void BfsFillDistances(Queue<int> queue, int[] dist, Dictionary<int, int> indexInMembers,
        HashSet<int> memberSet, int width, int height)
    {
        while (queue.Count > 0)
        {
            var idx = queue.Dequeue();
            var d = dist[indexInMembers[idx]];
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
                    if (!memberSet.Contains(nIdx)) continue;
                    var nmi = indexInMembers[nIdx];
                    if (dist[nmi] >= 0) continue;
                    dist[nmi] = d + 1;
                    queue.Enqueue(nIdx);
                }
            }
        }
    }

    /// <summary>Index of the steepest-downhill 8-connected neighbor from (x,y) in <paramref name="values"/>, or -1 only at the grid boundary.</summary>
    internal static int SteepestDescentNeighbor(float[] values, int width, int height, int x, int y)
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
