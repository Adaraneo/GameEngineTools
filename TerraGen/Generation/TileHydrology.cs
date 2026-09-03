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
        /// <summary>Montgomery &amp; Dietrich (1992) channel-initiation criterion: a cell becomes a
        /// river when its contributing area (upstream cell count × cell area, in m²) TIMES the local
        /// downslope gradient (dimensionless rise/run along the actual flow path — see
        /// <see cref="ComputeRiverMask"/>'s remarks) reaches this value. Real channel heads form
        /// where surface flow's shear stress exceeds the substrate's erosion resistance, and that
        /// shear stress scales with area × slope, not area alone — a steep hillside needs only a
        /// small catchment to start eroding a channel, while a dead-flat plain needs an enormous one
        /// (in the limit, none at all, matching real drainage: water doesn't carve a channel across
        /// perfectly flat ground). The units work out to m² — read it as "the contributing area
        /// this would take on a 45° (slope = 1.0) hillside." Default 100 was calibrated live at 5m
        /// cells against <see cref="TileGenerator"/>'s batch-wide combined grid on real generated
        /// terrain (seed -1384538600): ~0.8% overall coverage, worst single tile 3.8% — inside the
        /// same realistic ~0.5-2% area-coverage range the old pure-area threshold (800) needed live
        /// tuning to reach, but without that threshold's own failure mode — a wide, genuinely flat
        /// catchment no longer needs an arbitrarily large cell-count bar to stay believable, since
        /// its near-zero real slope suppresses it directly, without per-scale recalibration.</summary>
        double AreaSlopeThreshold = 100.0);

    /// <summary>Tiny per-hop elevation bump <see cref="FillDepressions"/> adds while flooding a pit
    /// or a flat plateau — small enough to never visibly distort real terrain (real slopes differ
    /// by orders of magnitude more), but enough to keep the filled surface STRICTLY monotonic
    /// outward from any point to the tile's own boundary, so <see cref="SteepestDescentNeighbor"/>
    /// always has a well-defined single downhill direction even across what was originally a dead
    /// flat basin.</summary>
    private const float FillEpsilon = 1e-3f;

    /// <summary>Computes a river mask via single-flow-direction (D8) accumulation combined with the
    /// Montgomery &amp; Dietrich area-slope channel-initiation criterion: every cell starts with 1
    /// unit of "rainfall" and drains to its single steepest downhill 8-connected neighbor;
    /// accumulation sums along that path. A cell is marked river once its contributing area (that
    /// accumulation, in m²) times its local downslope gradient reaches
    /// <see cref="Parameters.AreaSlopeThreshold"/> — see that property's remarks for why area alone
    /// isn't the right criterion. Returns a 0/1 byte mask the same length/shape as
    /// <paramref name="grid"/>'s own <c>Values</c> — the exact convention
    /// <see cref="TerrainHeightmap.RiverMask"/> already uses.</summary>
    /// <remarks>
    /// Flow DIRECTION and accumulation are routed across a <see cref="FillDepressions"/>-filled, then
    /// <see cref="ResolveFlats"/>-corrected copy of the elevation, not the raw values — ridged
    /// mountain noise is riddled with tiny local pits (every little bump creates one), and without
    /// filling them first, D8 flow dead-ends in the very first pit it meets: the visible symptom is a
    /// field of short, disconnected river dashes that never join into a longer channel. Filling alone
    /// fixes that but introduces a second problem on genuinely low-relief ground — see
    /// <see cref="ResolveFlats"/> — which is why its output, not the raw filled surface, is what
    /// determines WHICH WAY flow goes. The channel-initiation SLOPE test, in contrast, is measured on
    /// the RAW (untouched) elevation along whichever direction that routing chose — using the
    /// filled/resolved surface there instead would measure the tiny synthetic epsilon gradient
    /// <see cref="ResolveFlats"/> invents to break ties, not the real ground's actual steepness, and
    /// every flat would then look artificially "sloped enough" to channelize no matter how large its
    /// area got. Neither step changes the terrain itself (<paramref name="grid"/>'s own <c>Values</c>
    /// are untouched) — only what's used to decide direction versus what's used to judge steepness.
    /// </remarks>
    public static byte[] ComputeRiverMask(TerrainHeightmap grid, Parameters p)
    {
        var width = grid.Width;
        var height = grid.Height;
        var count = width * height;
        var cellSize = grid.CellSizeMeters;
        var cellAreaM2 = cellSize * cellSize;

        var filled = FillDepressions(grid);
        var routed = ResolveFlats(filled, width, height);

        var downstream = new int[count]; // -1 = no strictly-downhill neighbor (only possible at a grid edge)
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                downstream[y * width + x] = SteepestDescentNeighbor(routed, width, height, x, y);

        // Flow only ever moves to STRICTLY lower ground (see SteepestDescentNeighbor), so
        // processing cells from highest to lowest elevation guarantees every cell's upstream
        // contributors have already added their share to it before it, in turn, drains onward —
        // one O(n log n) sorted pass instead of an iterative graph solve. This holds regardless of
        // exactly how `routed` was derived, since SteepestDescentNeighbor only ever returns a
        // neighbor strictly lower than `here` IN THAT SAME FIELD by construction.
        var order = new int[count];
        for (var i = 0; i < count; i++) order[i] = i;
        Array.Sort(order, (a, b) => routed[b].CompareTo(routed[a]));

        var accumulation = new int[count];
        for (var i = 0; i < count; i++) accumulation[i] = 1;

        foreach (var idx in order)
        {
            var next = downstream[idx];
            if (next >= 0)
                accumulation[next] += accumulation[idx];
        }

        var mask = new byte[count];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var next = downstream[i];
                if (next < 0) continue; // no known outflow slope to judge — never a channel head

                var nx = next % width;
                var ny = next / width;
                var distance = (nx != x && ny != y ? 1.4142135623730951 : 1.0) * cellSize;
                // Raw (pre-fill) elevation drop, clamped at 0: a genuinely flat or ResolveFlats-only
                // "downhill" step (real drop ~0 or slightly negative from epsilon tie-breaking) has
                // no real slope to speak of, whatever direction routing picked for it.
                var slope = Math.Max(0.0, (grid.Values[i] - grid.Values[next]) / distance);

                var contributingAreaM2 = accumulation[i] * cellAreaM2;
                if (contributingAreaM2 * slope >= p.AreaSlopeThreshold)
                    mask[i] = 1;
            }
        }

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

    /// <summary>Near-equal filled-elevation gap (in <see cref="FillEpsilon"/> steps) two 8-connected
    /// cells must fall within to be grouped into the same flat component by <see cref="ResolveFlats"/>
    /// — a few epsilon steps, since <see cref="FillDepressions"/> only ever separates truly distinct
    /// terrain by a real (much larger) slope; anything this close is the epsilon ring itself, not
    /// genuine relief.</summary>
    private const float FlatEqualityTolerance = FillEpsilon * 4f;

    /// <summary>
    /// Garbrecht &amp; Martz (1997) flat-surface resolution. <see cref="FillDepressions"/> alone
    /// guarantees every cell has SOME strictly-lower neighbor, but on a genuinely low-relief
    /// plateau the epsilon ring it adds radiates outward from the whole flooded boundary at once —
    /// like ripples on a pond — so <see cref="SteepestDescentNeighbor"/> sends neighboring cells off
    /// in whatever locally-nearest-ring direction they happen to face, with nothing pulling separate
    /// paths back together. The visible symptom: flow smears as a wide sheet across the entire flat
    /// instead of converging into a channel, instead of the dashed/fragmented rivers this whole file
    /// already fixed once. This adds a second synthetic gradient on top of the filled surface,
    /// computed per flat component (a maximal 8-connected run of cells within
    /// <see cref="FlatEqualityTolerance"/> of each other) from two BFS distance fields seeded at the
    /// component's own boundary: distance AWAY from the flat's higher inflow edge (nudges initial
    /// flow off the wall, rather than hugging it) and distance TOWARD the flat's lower outflow edge
    /// (pulls every path toward the SAME exit) — weighted 1:2 exactly as Garbrecht &amp; Martz
    /// describe, scaled by <see cref="FillEpsilon"/> so the correction always nests strictly inside
    /// the (much larger) real-terrain gap <see cref="FillDepressions"/> already put between this flat
    /// and its genuinely higher/lower neighbors, never overturning the macro direction those already
    /// fixed — only how the flat's own interior funnels toward it.
    /// </summary>
    private static float[] ResolveFlats(float[] filled, int width, int height)
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
                // The grid's own boundary is the local-approximation drain everywhere else in this
                // file already treats it as (see FillDepressions/SteepestDescentNeighbor) — a flat
                // cell sitting right on it is a guaranteed outflow edge regardless of its neighbors.
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
                // distToLower is always resolved: FillDepressions only ever raises a cell to reach a
                // strictly-lower-or-boundary drain, so every flat has at least one true outflow edge.
                // distToHigher can legitimately stay unresolved (a flat with no adjacent wall at
                // all, e.g. one that fills its whole padded tile) — treat that as "no wall bias".
                var toLower = distToLower[i] < 0 ? 0 : distToLower[i];
                var toHigher = distToHigher[i] < 0 ? 0 : distToHigher[i];
                routed[members[i]] = filled[members[i]] + FillEpsilon * (toHigher - 2f * toLower);
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
