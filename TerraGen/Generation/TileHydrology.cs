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
    /// <remarks>Direction/accumulation route across <see cref="FlowRouting.FillDepressions"/>+<see cref="FlowRouting.ResolveFlats"/> output (raw elevation dead-ends D8 at the first pit); the slope test itself reads raw (untouched) elevation, since the routed surface's synthetic tie-break gradient isn't real steepness. Once a cell qualifies as a channel head, every cell downstream is marked too regardless of its own pointwise slope — evaluated purely pointwise, meter-scale post-erosion noise made real channels flicker on/off.</remarks>
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

        var routing = FlowRouting.Compute(grid);
        var downstream = routing.Downstream;
        var order = routing.Stack;
        var accumulation = routing.Accumulation;

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
        var filled = FlowRouting.FillDepressions(grid);
        var routed = FlowRouting.ResolveFlats(filled, width, height);

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

}
