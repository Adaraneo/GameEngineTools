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
        /// <summary>Channel-initiation criterion A·S² (not A·S). Source: Montgomery &amp; Dietrich (1992), Science 255(5046):826-830 — field range [500, 4000] m².</summary>
        double ChannelInitiationAreaSlopeSquaredThreshold = 2000.0);

    /// <summary>90° in radians — the angular span of one of Tarboton's 8 triangular facets.</summary>
    private const double QuarterPi = Math.PI / 4.0;

    /// <summary>√2 — grid distance to a diagonal neighbor.</summary>
    private const double Root2 = 1.4142135623730951;

    /// <summary>Tiny per-hop elevation bump <see cref="FillDepressions"/> adds while flooding a pit
    /// or a flat plateau — small enough to never visibly distort real terrain (real slopes differ
    /// by orders of magnitude more), but enough to keep the filled surface STRICTLY monotonic
    /// outward from any point to the tile's own boundary, so <see cref="SteepestDescentNeighbor"/>
    /// always has a well-defined single downhill direction even across what was originally a dead
    /// flat basin.</summary>
    private const float FillEpsilon = 1e-3f;

    /// <summary>Computes a river mask via single-flow-direction (D8) accumulation combined with the
    /// Montgomery &amp; Dietrich area-slope-squared channel-initiation criterion: every cell starts
    /// with 1 unit of "rainfall" and drains to its single steepest downhill 8-connected neighbor;
    /// accumulation sums along that path. A cell is marked river once its contributing area (that
    /// accumulation, in m²) times the SQUARE of its local downslope gradient reaches
    /// <see cref="Parameters.ChannelInitiationAreaSlopeSquaredThreshold"/> — see that property's
    /// remarks for why area alone (or even area × slope, unsquared) isn't the right criterion.
    /// Returns a 0/1 byte mask the same length/shape as
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
    ///
    /// The area×slope² test only decides where a channel STARTS. Once a cell qualifies, every cell
    /// downstream of it is marked too, regardless of whether THAT cell's own local slope clears the
    /// bar — confirmed live this has to work this way: real post-erosion elevation is noisy enough
    /// at the meter scale that two adjacent cells in the middle of an established, thousands-of-
    /// cells-wide channel can have a real drop that rounds to ~0, and evaluating the criterion
    /// pointwise at every cell (instead of just at the head) made the channel flicker on/off roughly
    /// every other cell instead of reading as one continuous river. A real channel doesn't do that —
    /// once water has cut a bed, it keeps flowing through a locally flat or noisy stretch rather than
    /// vanishing there and restarting a few cells later.
    /// </remarks>
    public static byte[] ComputeRiverMask(TerrainHeightmap grid, Parameters p) => ComputeDiagnostics(grid, p).Mask;

    /// <summary>Same computation as <see cref="ComputeRiverMask"/>, but also returns the
    /// intermediate per-cell accumulation, slope, Strahler-order and Shreve-magnitude arrays the
    /// mask is derived from — the order array specifically IS production-relevant (<see cref="TileGenerator"/>
    /// bakes it into the persisted <see cref="TerrainHeightmap.RiverMask"/> byte value instead of a
    /// flat 1, see that type's remarks), likewise Shreve magnitude (<see cref="TerrainHeightmap.ShreveMagnitude"/>);
    /// the rest exist so a test or a future investigation can see WHY a specific cell did or didn't
    /// make the cut, instead of only the pass/fail outcome.</summary>
    internal static (byte[] Mask, int[] Accumulation, double[] Slope, int[] Downstream, int[] Order, byte[] StrahlerOrder, int[] ShreveMagnitude) ComputeDiagnostics(TerrainHeightmap grid, Parameters p)
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

        var slope = new double[count];
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
                slope[i] = Math.Max(0.0, (grid.Values[i] - grid.Values[next]) / distance);

                var contributingAreaM2 = accumulation[i] * cellAreaM2;
                if (contributingAreaM2 * slope[i] * slope[i] >= p.ChannelInitiationAreaSlopeSquaredThreshold)
                    mask[i] = 1;
            }
        }

        // Montgomery & Dietrich's area×slope² criterion is for CHANNEL INITIATION — where does a
        // channel first cut in — not a per-point validity check repeated at every single cell
        // along its length. Evaluated pointwise like the loop above just did, it flickers: two
        // adjacent post-erosion cells can have a real elevation drop that rounds to ~0 (meter-scale
        // droplet-erosion granularity, not the river actually leveling out) even in the middle of a
        // channel whose accumulation is already in the thousands — confirmed live on production
        // terrain, where a single mainstem trunk (accumulation 6,600-11,800) marked barely half its
        // cells, flickering on/off roughly every other one. A real channel doesn't do that: once
        // water has cut a bed, it keeps flowing through a locally flat or noisy stretch instead of
        // vanishing there. So once a cell IS a channel, every cell downstream of it is too — this
        // single top-to-bottom pass (same order the accumulation sum above used, so a cell's own
        // upstream propagation has always already landed on it before it decides whether to pass
        // the mark further down) turns "was this exact point steep enough" into "has a channel
        // already reached this point," which is what actually determines whether a river is there.
        foreach (var idx in order)
        {
            if (mask[idx] == 0) continue;
            var next = downstream[idx];
            if (next >= 0) mask[next] = 1;
        }

        // Strahler stream order (Strahler 1952/1957): a headwater reach with no river tributary
        // feeding it is order 1; a reach's own order only increases — by exactly 1 — where two
        // tributaries of the SAME order merge. A big creek absorbing a much smaller trickle stays
        // the same order, same as real USGS/hydrology classification — order is meant to track
        // "how many times has a comparably-sized channel joined this one," not raw contributing
        // area. Computed in the same source-to-mouth topological pass `order` already gives every
        // other per-cell quantity here, incrementally: `runningMaxOrder`/`runningCountAtMax` track
        // the highest order seen among a cell's own upstream river contributors so far, and how
        // many of them tied for it, without needing to materialize a full per-cell upstream list.
        //
        // Shreve stream magnitude, computed in the same pass: additive (sum of contributors), unlike Strahler's max/conditional-increment. Source: Shreve, R.L. (1966), J. Geology 74:17-37.
        var strahlerOrder = new byte[count];
        var runningMaxOrder = new int[count];
        var runningCountAtMax = new int[count];
        Array.Fill(runningMaxOrder, -1);
        var shreveMagnitude = new int[count];

        foreach (var idx in order)
        {
            if (mask[idx] == 0) continue;

            var myOrder = runningMaxOrder[idx] < 0 ? 1
                : runningCountAtMax[idx] >= 2 ? runningMaxOrder[idx] + 1
                : runningMaxOrder[idx];
            strahlerOrder[idx] = (byte)Math.Min(255, myOrder);

            if (shreveMagnitude[idx] == 0) shreveMagnitude[idx] = 1; // no river contributor reached it yet -> headwater

            var next = downstream[idx];
            if (next < 0) continue;

            if (myOrder > runningMaxOrder[next]) { runningMaxOrder[next] = myOrder; runningCountAtMax[next] = 1; }
            else if (myOrder == runningMaxOrder[next]) { runningCountAtMax[next]++; }

            if (mask[next] != 0) shreveMagnitude[next] += shreveMagnitude[idx];
        }

        return (mask, accumulation, slope, downstream, order, strahlerOrder, shreveMagnitude);
    }

    /// <summary>Diagnostic-only D-infinity routing, run alongside D8, not wired into production. Source: Tarboton, D.G. (1997), WRR 33(2):309-319.</summary>
    internal static (double[] Angle, int[] NeighborA, int[] NeighborB, double[] WeightA, double[] Accumulation) ComputeDInfinityDiagnostics(TerrainHeightmap grid)
    {
        var width = grid.Width;
        var height = grid.Height;
        var count = width * height;

        // Same filled/flow-resolved surface D8 routes across — D∞ needs the same pit-free input.
        var filled = FillDepressions(grid);
        var routed = ResolveFlats(filled, width, height);

        var (angle, neighborA, neighborB, weightA) = ComputeDInfinityDirections(routed, width, height);

        // Elevation-sorted topological order, same construction as ComputeDiagnostics' own `order`.
        var order = new int[count];
        for (var i = 0; i < count; i++) order[i] = i;
        Array.Sort(order, (a, b) => routed[b].CompareTo(routed[a]));

        var accumulation = ComputeDInfinityAccumulation(order, neighborA, neighborB, weightA, width, height);

        return (angle, neighborA, neighborB, weightA, accumulation);
    }

    /// <summary>Tarboton's (1997) eight-triangular-facet D-infinity flow direction per cell, as two bounding neighbors plus a fractional weight.</summary>
    internal static (double[] Angle, int[] NeighborA, int[] NeighborB, double[] WeightA) ComputeDInfinityDirections(float[] routed, int width, int height)
    {
        var count = width * height;
        var angle = new double[count];
        var neighborA = new int[count];
        var neighborB = new int[count];
        var weightA = new double[count];
        Array.Fill(neighborA, -1);
        Array.Fill(neighborB, -1);

        // 8 directions in 45° steps around the cell: E, NE, N, NW, W, SW, S, SE.
        Span<int> dx = stackalloc int[] { 1, 1, 0, -1, -1, -1, 0, 1 };
        Span<int> dy = stackalloc int[] { 0, 1, 1, 1, 0, -1, -1, -1 };
        // Each facet pairs a cardinal (distance-1) e1 with an adjacent diagonal (distance-√2) e2 — naive consecutive-direction pairing gets this backwards on alternating facets.
        Span<int> cardinal = stackalloc int[] { 0, 2, 2, 4, 4, 6, 6, 0 };
        Span<int> diagonal = stackalloc int[] { 1, 1, 3, 3, 5, 5, 7, 7 };

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var e0 = (double)routed[i];

                var bestSlope = 0.0; // only strictly-downhill facets count, mirroring SteepestDescentNeighbor
                var bestFacet = -1;
                var bestR = 0.0;

                for (var m = 0; m < 8; m++)
                {
                    var c = cardinal[m];
                    var d = diagonal[m];
                    var x1 = x + dx[c]; var y1 = y + dy[c];
                    var x2 = x + dx[d]; var y2 = y + dy[d];
                    if (x1 < 0 || x1 >= width || y1 < 0 || y1 >= height) continue;
                    if (x2 < 0 || x2 >= width || y2 < 0 || y2 >= height) continue;

                    var e1 = (double)routed[y1 * width + x1]; // cardinal — real distance 1
                    var e2 = (double)routed[y2 * width + x2]; // diagonal — real distance √2, but exactly 1 more unit step away from e1

                    var s1 = e0 - e1;
                    var s2 = e1 - e2;
                    if (s1 == 0.0 && s2 == 0.0) continue;

                    var r = Math.Atan2(s2, s1);
                    double s;
                    if (double.IsNaN(r) || r < 0.0) { r = 0.0; s = s1; }
                    else if (r > QuarterPi) { r = QuarterPi; s = (e0 - e2) / Root2; }
                    else { s = Math.Sqrt(s1 * s1 + s2 * s2); }

                    if (s > bestSlope)
                    {
                        bestSlope = s;
                        bestFacet = m;
                        bestR = r;
                    }
                }

                if (bestFacet < 0) continue; // no downhill facet at all — same edge/pit case SteepestDescentNeighbor returns -1 for

                var idxA = (y + dy[cardinal[bestFacet]]) * width + (x + dx[cardinal[bestFacet]]);
                var idxB = (y + dy[diagonal[bestFacet]]) * width + (x + dx[diagonal[bestFacet]]);

                var fracB = bestR / QuarterPi; // 0 at the facet's cardinal edge, 1 at its diagonal edge
                angle[i] = bestFacet * QuarterPi + bestR;
                if (fracB <= 0.0) { neighborA[i] = idxA; weightA[i] = 1.0; }
                else if (fracB >= 1.0) { neighborA[i] = idxB; weightA[i] = 1.0; }
                else { neighborA[i] = idxA; neighborB[i] = idxB; weightA[i] = 1.0 - fracB; }
            }
        }

        return (angle, neighborA, neighborB, weightA);
    }

    /// <summary>Like D8 accumulation, but splits each cell's contribution fractionally between its two D∞ downslope neighbors. Source: Tarboton, D.G. (1997), WRR 33(2):309-319.</summary>
    internal static double[] ComputeDInfinityAccumulation(int[] order, int[] neighborA, int[] neighborB, double[] weightA, int width, int height)
    {
        var count = width * height;
        var accumulation = new double[count];
        for (var i = 0; i < count; i++) accumulation[i] = 1.0;

        foreach (var idx in order)
        {
            var a = neighborA[idx];
            if (a < 0) continue; // outlet — no downhill facet at all, nothing to forward

            var b = neighborB[idx];
            if (b < 0)
            {
                accumulation[a] += accumulation[idx];
            }
            else
            {
                var wa = weightA[idx];
                accumulation[a] += accumulation[idx] * wa;
                accumulation[b] += accumulation[idx] * (1.0 - wa);
            }
        }

        return accumulation;
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
    /// flow off the wall, rather than hugging it — a cell close to the wall gets pushed UP, away
    /// from being anyone's downhill target) and distance TOWARD the flat's lower outflow edge (a
    /// cell close to the true exit gets pulled DOWN, toward being everyone's downhill target, so
    /// every path funnels to the SAME exit instead of drifting toward the nearest wall) — weighted
    /// 1:2 exactly as Garbrecht &amp; Martz describe, scaled by <see cref="FillEpsilon"/> so the
    /// correction always nests strictly inside
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
                // +toLower (a cell far from the true exit sits HIGH, so it's never anyone's downhill
                // target) and -toHigher (a cell far from the wall sits LOW, pulling flow away from
                // hugging it) — NOT the other way around, which would bias flow toward the wall
                // instead of the exit. Found live via a propagation regression test: within an
                // isolated flat component this bias is the only thing deciding direction, and the
                // wrong sign here quietly sent water the wrong way inside every flat it touched.
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
