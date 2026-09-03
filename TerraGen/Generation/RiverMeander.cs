using GameEngineTools.World.Data;

namespace TerraGen.Generation;

/// <summary>
/// Migrates <see cref="TileHydrology"/>'s D8 river backbone into a meandering path via an actual
/// iterative bank-erosion simulation — Ikeda, Parker &amp; Sawai's (1981) linear migration theory
/// combined with Howard &amp; Knutson's (1984) "start nearly straight, let instability grow the
/// bends" simulation approach — instead of imposing a sine-curve SHAPE on top of the backbone.
/// Real meanders form because a bend's own curvature increases the near-bank flow velocity there,
/// which erodes the outer bank and deposits a point bar on the inner one, migrating the channel
/// sideways in the SAME rotational sense it was already bending — a positive-feedback process that
/// starts from whatever tiny irregularity a real channel always has (even a "straight" one) and
/// grows it into full bends over time, rather than any shape being decided in advance. That
/// feedback is what this simulates: seed a small perpendicular perturbation onto every point of the
/// straight backbone (a real channel is never perfectly straight either), then repeatedly nudge
/// every point sideways in proportion to a curvature signal at that point — not just ITS OWN local
/// curvature, but an upstream-decaying blend of it (IPS's "near-bank velocity depends on a
/// convolution of curvature back along the channel", because the flow's momentum carries a bend's
/// influence some distance downstream before straightening back out) — for a fixed number of
/// iterations. Suppressed on steep terrain the same way as before (real channels don't get enough
/// time/opportunity to migrate before just cutting straight downhill there — Leopold &amp; Wolman
/// 1957), and channel width (hence how far a reach can wander and how long its curvature "memory"
/// reaches) still comes from contributing area the same way it did before.
///
/// KNOWN SIMPLIFICATION: real Howard-Knutson simulations also detect and splice out neck cutoffs
/// (where a bend curves back close enough to itself to pinch off into an oxbow lake) — without
/// that, letting the simulation run indefinitely eventually tangles a channel into itself. This
/// implementation skips explicit cutoffs and instead damps any step that would bring a point too
/// close to a non-adjacent part of the same network, and keeps the iteration count modest — bends
/// develop and vary in a stable-enough range without ever needing to resolve a real cutoff. A
/// future pass could add real cutoff splicing (and the resulting oxbow-lake cells) if that specific
/// detail turns out to matter.
/// </summary>
public static class RiverMeander
{
    public sealed record Parameters(
        /// <summary>Real channel width has no simulated discharge to derive it from here, so this
        /// approximates it from contributing area via a simplified power law (width grows with the
        /// square root of catchment area — the same order-of-magnitude scaling real width-discharge
        /// -area relationships share, without claiming their exact regional-calibration constants).
        /// Drives both how far a reach can migrate and how long its upstream curvature "memory"
        /// reaches, not just cosmetic scale.</summary>
        double WidthPerSqrtAreaM2 = 0.02,
        /// <summary>How many migration steps to simulate. Each one nudges every eligible point a
        /// little further in whatever direction its own feedback already favors — bends grow larger
        /// and more numerous with more iterations, up to where the collision damping below starts
        /// resisting further growth. Calibrated live, see the field's own value for the reasoning.</summary>
        int Iterations = 250,
        /// <summary>Migration distance per iteration (meters) per unit of dimensionless curvature
        /// (radians per meter) per meter of channel width — i.e. a bend curving at 1 radian per
        /// channel-width of travel migrates its outer bank by this many meters each iteration.
        /// Ikeda/Parker/Sawai express this as a physical erosion-rate coefficient calibrated against
        /// real discharge data this generator doesn't simulate, so it's tuned live here instead
        /// against how the resulting shapes actually look. This feedback loop has a genuine linear
        /// instability threshold (confirmed live via a synthetic isolated-trunk sweep): below a
        /// critical coefficient a seeded perturbation just decays back to straight; the critical
        /// value scales up sharply with channel width (~43 for a 23m-wide reach in the live sweep,
        /// far lower for narrow creeks), so a single default has to sit comfortably above the
        /// worst case (widest expected trunk) rather than being tuned near the edge — 150 cleared
        /// the threshold with room to spare for every width from 4m to 30m in that sweep, growing to
        /// (and saturating at, via <see cref="MaxBeltWidthPerWidth"/>) 80%+ of its belt cap within
        /// 200-300 iterations for all of them.</summary>
        double ErosionCoefficient = 150.0,
        /// <summary>IPS's upstream curvature "memory" — how far back along the channel (in multiples
        /// of local channel width) a bend's influence carries before decaying away. Real rivers
        /// resonate at a wavelength set by this decay length relative to channel width; too short
        /// and every point reacts only to itself (no coherent bends ever form), too long and the
        /// whole channel responds as one unit (one giant, unrealistic bend).</summary>
        double CurvatureMemoryLengthPerWidth = 6.0,
        /// <summary>Seed perturbation amplitude (fraction of local channel width) applied once,
        /// before any iteration — a real channel is never perfectly straight either, and the whole
        /// migration feedback below has nothing to amplify without SOME starting irregularity to
        /// work with. Deterministic (a smooth function of position, not RNG), so the same terrain
        /// always seeds and grows the same meander pattern.</summary>
        double InitialPerturbationPerWidth = 0.15,
        /// <summary>Safety clamp: no single iteration may move a point further than this fraction of
        /// its own channel width, however large the curvature feedback computes to — real bank
        /// erosion is rate-limited too, and without a cap a tight bend's positive feedback can blow
        /// up numerically within a few iterations instead of settling into a stable, evolving shape.</summary>
        double MaxStepPerIterationPerWidth = 0.35,
        /// <summary>Collision-avoidance safety clamp standing in for real neck-cutoff physics (see
        /// the type-level remarks): if a point's proposed step would bring it within this many
        /// channel widths of a non-adjacent point on the same network, the step is scaled down
        /// instead of applied in full — keeps the channel from tangling into itself instead of
        /// letting it pinch into a proper oxbow.</summary>
        double MinSeparationPerWidth = 1.5,
        /// <summary>Meander-belt cap: total drift from a point's original straight-backbone position
        /// is limited to this many multiples of local channel width — real rivers wander within a
        /// bounded floodplain belt, not indefinitely. This is the actual saturation mechanism for the
        /// migration feedback (see the clamp's own remarks): without it, a comfortably-supercritical
        /// erosion coefficient grows without bound wherever collision-avoidance never engages
        /// (confirmed live on an isolated synthetic trunk). Roughly matches observed real meander
        /// belt widths of a few channel widths.</summary>
        double MaxBeltWidthPerWidth = 4.0,
        /// <summary>Local slope (dimensionless rise/run) below which migration runs at full
        /// strength — a real lowland/floodplain-scale gradient.</summary>
        double SlopeFullMeanderBelow = 0.01,
        /// <summary>Local slope above which migration is fully suppressed and the channel stays on
        /// its straight D8 path — deliberately conservative (well past a typical lowland grade)
        /// rather than tuned to Leopold &amp; Wolman's own discharge-specific threshold line, which
        /// needs real discharge this generator doesn't simulate.</summary>
        double SlopeSuppressedAbove = 0.08);

    /// <summary>Takes the straight D8 mask <see cref="TileHydrology.ComputeDiagnostics"/> already
    /// computed (plus its accumulation/slope/downstream/Strahler-order arrays, which this reuses
    /// rather than recomputing) and returns a new mask where every marked cell has migrated to
    /// wherever the bank-erosion simulation moved it. Cell count and shape match the input — this
    /// only ever redistributes WHERE within the same grid the channel is drawn, it doesn't add or
    /// remove catchment area or change the underlying accumulation/routing at all. The returned byte
    /// value at a river cell is its Strahler order (see
    /// <see cref="GameEngineTools.World.Data.TerrainHeightmap.RiverMask"/>'s remarks), not a flat 1
    /// — migration only changes shape, never what a cell's own order was on the straight
    /// backbone.</summary>
    public static byte[] ApplyMeander(TerrainHeightmap grid, byte[] straightMask, int[] accumulation,
        double[] slope, int[] downstream, int[] order, byte[] strahlerOrder, Parameters p)
    {
        var (offsetX, offsetY) = ComputeOffsets(grid, straightMask, accumulation, slope, downstream, order, p);
        return Rasterize(grid, straightMask, downstream, strahlerOrder, offsetX, offsetY);
    }

    /// <summary>Same computation as <see cref="ApplyMeander"/>, but stops short of rasterizing —
    /// returns each backbone cell's own final migrated (x,y) instead. Not needed by any production
    /// caller (which only wants the final mask), but lets a test or investigation measure the
    /// simulation's actual output — path length, sinuosity, self-crossing — directly from where
    /// cells ended up, instead of only from how many raster pixels ended up lit.</summary>
    internal static (int[] OffsetX, int[] OffsetY) ComputeOffsets(TerrainHeightmap grid, byte[] straightMask,
        int[] accumulation, double[] slope, int[] downstream, int[] order, Parameters p)
    {
        var width = grid.Width;
        var height = grid.Height;
        var count = width * height;
        var cellSize = grid.CellSizeMeters;

        // Dominant predecessor per cell (the single upstream neighbor with the largest contributing
        // area) — the same selection TileHydrology's own connectivity relies on, giving every
        // branching network a well-defined single "previous point" and "next point" at each node
        // for curvature purposes, even though a confluence cell can have several real tributaries
        // feeding it.
        var predecessor = new int[count];
        var bestUpstreamAccum = new int[count];
        Array.Fill(predecessor, -1);

        var channelWidth = new double[count];
        for (var i = 0; i < count; i++)
            channelWidth[i] = Math.Max(cellSize * 0.1, p.WidthPerSqrtAreaM2 * Math.Sqrt(accumulation[i] * cellSize * cellSize));

        foreach (var idx in order)
        {
            if (straightMask[idx] == 0) continue;
            var next = downstream[idx];
            if (next < 0 || straightMask[next] == 0) continue;
            if (accumulation[idx] >= bestUpstreamAccum[next])
            {
                bestUpstreamAccum[next] = accumulation[idx];
                predecessor[next] = idx;
            }
        }

        // Points a handful of hops away along the SAME chain are always going to be physically
        // close together — that's just what a continuous polyline with ~cellSize point spacing
        // looks like, not a self-intersection. The collision-avoidance clamp further down must
        // never react to that (confirmed live: without this exclusion, it fires almost everywhere,
        // suppressing growth before any bend can develop at all — the simulation nudges every point
        // but the shape never becomes visibly wavy). Only genuinely distant-along-the-channel points
        // that happen to land close together are real near-misses worth damping.
        const int chainExclusionHops = 15;
        var nearbyInChain = new HashSet<int>[count];
        for (var i = 0; i < count; i++)
        {
            if (straightMask[i] == 0) continue;
            var set = new HashSet<int> { i };
            var cur = i;
            for (var h = 0; h < chainExclusionHops; h++)
            {
                cur = predecessor[cur];
                if (cur < 0) break;
                set.Add(cur);
            }
            cur = i;
            for (var h = 0; h < chainExclusionHops; h++)
            {
                cur = downstream[cur];
                if (cur < 0 || straightMask[cur] == 0) break;
                set.Add(cur);
            }
            nearbyInChain[i] = set;
        }

        // Erodibility: how strongly a cell's curvature signal actually translates into migration —
        // zero on steep ground (real channels there don't get the chance to wander before just
        // cutting straight downhill, Leopold & Wolman 1957), full strength on gentle ground.
        var erodibility = new double[count];
        for (var i = 0; i < count; i++)
        {
            if (straightMask[i] == 0) continue;
            var here = slope[i];
            var suppression = here <= p.SlopeFullMeanderBelow ? 1.0
                : here >= p.SlopeSuppressedAbove ? 0.0
                : 1.0 - (here - p.SlopeFullMeanderBelow) / (p.SlopeSuppressedAbove - p.SlopeFullMeanderBelow);
            erodibility[i] = p.ErosionCoefficient * suppression;
        }

        // Seed phase, propagated incrementally along arc length like a running total (NOT
        // recomputed from raw (x,y) position) — its wavelength has to scale with each reach's OWN
        // channel width, not a fixed spatial frequency, or the seed oscillates faster than the
        // curvature convolution's own memory window can track. Confirmed live: with a
        // width-independent seed, a wide trunk river (23m width, ~27-cell memory window) barely
        // moved (0.085m average over the whole simulation) while narrow creeks moved freely (2m+) —
        // the trunk's memory window spanned 4+ full seed oscillation cycles, so the alternating
        // +/- signal averaged itself out to near-zero before the migration feedback ever got a
        // usable direction to amplify. Tying the seed's own wavelength to
        // CurvatureMemoryLengthPerWidth keeps roughly one seed cycle per memory window at any
        // channel size, so there's always a coherent (not self-cancelling) direction to grow.
        var seedPhase = new double[count];
        foreach (var idx in order)
        {
            if (straightMask[idx] == 0) continue;
            var next = downstream[idx];
            if (next < 0 || straightMask[next] == 0) continue;
            if (predecessor[next] != idx) continue;

            var x = idx % width; var y = idx / width;
            var nx = next % width; var ny = next / width;
            var stepDist = (nx != x && ny != y ? 1.4142135623730951 : 1.0) * cellSize;
            var seedWavelength = Math.Max(cellSize, p.CurvatureMemoryLengthPerWidth * channelWidth[idx]);
            seedPhase[next] = seedPhase[idx] + 2.0 * Math.PI * stepDist / seedWavelength;
        }

        // World-space positions (meters), one per grid cell — starts exactly on the straight D8
        // backbone, then migrates in place over the iterations below. A tiny deterministic
        // perpendicular seed perturbation breaks perfect straightness up front: a dead-straight run
        // has exactly zero curvature at every point, and zero curvature can never grow into
        // anything under a purely proportional feedback — real channels always have SOME starting
        // irregularity for the same feedback to amplify, so this stands in for that.
        var posX = new double[count];
        var posY = new double[count];
        var anchorX = new double[count];
        var anchorY = new double[count];
        for (var i = 0; i < count; i++)
        {
            if (straightMask[i] == 0) continue;
            var gx = i % width;
            var gy = i / width;
            posX[i] = grid.OriginX + gx * cellSize;
            posY[i] = grid.OriginY + gy * cellSize;
            anchorX[i] = posX[i];
            anchorY[i] = posY[i];

            if (predecessor[i] < 0 || downstream[i] < 0 || straightMask[downstream[i]] == 0) continue; // heads/mouths stay fixed anchors
            var seedAmp = p.InitialPerturbationPerWidth * channelWidth[i];
            // Perpendicular to the (fixed, straight-backbone) predecessor->successor direction.
            var px = predecessor[i] % width; var py = predecessor[i] / width;
            var nxg = downstream[i] % width; var nyg = downstream[i] / width;
            var tdx = nxg - px; var tdy = nyg - py;
            var tlen = Math.Sqrt(tdx * tdx + tdy * tdy);
            if (tlen < 0.001) continue;
            posX[i] += -(tdy / tlen) * seedAmp * Math.Sin(seedPhase[i]);
            posY[i] += (tdx / tlen) * seedAmp * Math.Sin(seedPhase[i]);
        }

        // Spatial hash for the collision-avoidance safety clamp — rebuilt once per iteration from
        // that iteration's own positions, bucketed at roughly one typical channel width so a
        // neighbor lookup only has to check a small constant number of buckets.
        var bucketSize = Math.Max(cellSize, channelWidth.Where((_, i) => straightMask[i] != 0).DefaultIfEmpty(cellSize).Average()) * 1.5;

        var curvature = new double[count];
        var convolvedCurvature = new double[count];
        var newPosX = new double[count];
        var newPosY = new double[count];

        for (var iter = 0; iter < p.Iterations; iter++)
        {
            // Curvature at every point with both a predecessor and a valid successor — signed
            // turning angle from (predecessor->here) to (here->successor), per unit length. Sign
            // convention only has to be self-consistent with the normal direction below; whichever
            // way it comes out, the feedback amplifies whatever direction a point already bends.
            for (var i = 0; i < count; i++)
            {
                curvature[i] = 0.0;
                if (straightMask[i] == 0) continue;
                var pred = predecessor[i];
                var next = downstream[i];
                if (pred < 0 || next < 0 || straightMask[next] == 0) continue;

                var ax = posX[pred]; var ay = posY[pred];
                var bx = posX[i]; var by = posY[i];
                var cx = posX[next]; var cy = posY[next];

                var abx = bx - ax; var aby = by - ay;
                var bcx = cx - bx; var bcy = cy - by;
                var abLen = Math.Sqrt(abx * abx + aby * aby);
                var bcLen = Math.Sqrt(bcx * bcx + bcy * bcy);
                if (abLen < 1e-6 || bcLen < 1e-6) continue;

                var cross = abx * bcy - aby * bcx;
                var dot = abx * bcx + aby * bcy;
                var turnAngle = Math.Atan2(cross, dot);
                var segLen = 0.5 * (abLen + bcLen);
                curvature[i] = turnAngle / segLen;
            }

            // IPS-style upstream memory: propagated as a leaky integrator down the same dominant-
            // path topological order used throughout this file — the analytic steady-state solution
            // of "influence decays exponentially with distance since it was generated" is exactly
            // this kind of running blend, computed once per iteration since curvature itself just
            // changed above.
            for (var i = 0; i < count; i++)
            {
                if (straightMask[i] == 0) continue;
                if (predecessor[i] < 0) { convolvedCurvature[i] = curvature[i]; continue; }
            }
            foreach (var idx in order)
            {
                if (straightMask[idx] == 0) continue;
                var next = downstream[idx];
                if (next < 0 || straightMask[next] == 0) continue;
                if (predecessor[next] != idx) continue; // only the dominant edge carries memory forward

                var stepDist = Math.Sqrt(Math.Pow(posX[next] - posX[idx], 2) + Math.Pow(posY[next] - posY[idx], 2));
                var decayLength = Math.Max(cellSize, p.CurvatureMemoryLengthPerWidth * channelWidth[idx]);
                var decayWeight = Math.Exp(-stepDist / decayLength);
                convolvedCurvature[next] = curvature[next] * (1.0 - decayWeight) + convolvedCurvature[idx] * decayWeight;
            }

            // Proposed migration step for every eligible point (heads/mouths excluded — fixed
            // anchors, same as the seed perturbation above skipped them).
            Array.Copy(posX, newPosX, count);
            Array.Copy(posY, newPosY, count);

            for (var i = 0; i < count; i++)
            {
                if (straightMask[i] == 0) continue;
                var pred = predecessor[i];
                var next = downstream[i];
                if (pred < 0 || next < 0 || straightMask[next] == 0) continue;

                var tdx = posX[next] - posX[pred];
                var tdy = posY[next] - posY[pred];
                var tlen = Math.Sqrt(tdx * tdx + tdy * tdy);
                if (tlen < 1e-6) continue;
                var normalX = -tdy / tlen;
                var normalY = tdx / tlen;

                var stepMeters = erodibility[i] * convolvedCurvature[i] * channelWidth[i];
                var maxStep = p.MaxStepPerIterationPerWidth * channelWidth[i];
                stepMeters = Math.Clamp(stepMeters, -maxStep, maxStep);

                newPosX[i] = posX[i] + normalX * stepMeters;
                newPosY[i] = posY[i] + normalY * stepMeters;
            }

            // Collision-avoidance safety clamp (stands in for real cutoff splicing — see the
            // type-level remarks): bucket the PROPOSED positions, and for any point whose new
            // position landed too close to a non-adjacent point, shrink that step instead of
            // applying it in full.
            var buckets = new Dictionary<(int, int), List<int>>();
            for (var i = 0; i < count; i++)
            {
                if (straightMask[i] == 0) continue;
                var key = ((int)Math.Floor(newPosX[i] / bucketSize), (int)Math.Floor(newPosY[i] / bucketSize));
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
                list.Add(i);
            }

            for (var i = 0; i < count; i++)
            {
                if (straightMask[i] == 0) continue;
                var pred = predecessor[i];
                var next = downstream[i];
                if (pred < 0 || next < 0 || straightMask[next] == 0) continue;

                var minAllowed = p.MinSeparationPerWidth * channelWidth[i];
                var bx = (int)Math.Floor(newPosX[i] / bucketSize);
                var by = (int)Math.Floor(newPosY[i] / bucketSize);
                var closest = double.PositiveInfinity;

                for (var dby = -1; dby <= 1; dby++)
                {
                    for (var dbx = -1; dbx <= 1; dbx++)
                    {
                        if (!buckets.TryGetValue((bx + dbx, by + dby), out var list)) continue;
                        foreach (var j in list)
                        {
                            if (nearbyInChain[i].Contains(j)) continue;
                            var dx = newPosX[j] - newPosX[i];
                            var dy = newPosY[j] - newPosY[i];
                            var d = Math.Sqrt(dx * dx + dy * dy);
                            if (d < closest) closest = d;
                        }
                    }
                }

                if (closest < minAllowed)
                {
                    // Shrink this iteration's step proportionally to how badly it violated the
                    // minimum separation, down to a hard floor of "don't move at all" rather than
                    // letting it overshoot into (or past) whatever it nearly hit.
                    var shrink = Math.Clamp(closest / minAllowed, 0.0, 1.0);
                    newPosX[i] = posX[i] + (newPosX[i] - posX[i]) * shrink;
                    newPosY[i] = posY[i] + (newPosY[i] - posY[i]) * shrink;
                }

                // Meander-belt cap: the one real saturation mechanism the linear IPS feedback is
                // missing on its own. Confirmed live (synthetic straight-trunk sweep): below a
                // critical ErosionCoefficient a seeded perturbation just decays back to straight;
                // above it, the SAME positive feedback that should settle into stable bends instead
                // grows without bound (avgDisp scaling straight up with iteration count, no
                // saturation) whenever the point is far enough from any other reach that the
                // collision-avoidance clamp above never engages. Real rivers don't wander forever
                // either — floodplain confinement and, eventually, cutoffs bound how far a channel
                // strays from its valley's own course. Clamping total drift from the ORIGINAL
                // straight-backbone position to a fixed multiple of channel width reproduces that
                // bound directly, so a comfortably-supercritical (reliably growing, not
                // knife-edge-tuned) coefficient can be used for every reach regardless of width.
                var beltMax = p.MaxBeltWidthPerWidth * channelWidth[i];
                var fromAnchorX = newPosX[i] - anchorX[i];
                var fromAnchorY = newPosY[i] - anchorY[i];
                var fromAnchorDist = Math.Sqrt(fromAnchorX * fromAnchorX + fromAnchorY * fromAnchorY);
                if (fromAnchorDist > beltMax)
                {
                    var scale = beltMax / fromAnchorDist;
                    newPosX[i] = anchorX[i] + fromAnchorX * scale;
                    newPosY[i] = anchorY[i] + fromAnchorY * scale;
                }
            }

            (posX, newPosX) = (newPosX, posX);
            (posY, newPosY) = (newPosY, posY);
        }

        var offsetX = new int[count];
        var offsetY = new int[count];
        for (var i = 0; i < count; i++)
        {
            offsetX[i] = i % width;
            offsetY[i] = i / width;
            if (straightMask[i] == 0) continue;
            var gx = (int)Math.Round((posX[i] - grid.OriginX) / cellSize);
            var gy = (int)Math.Round((posY[i] - grid.OriginY) / cellSize);
            offsetX[i] = Math.Clamp(gx, 0, width - 1);
            offsetY[i] = Math.Clamp(gy, 0, height - 1);
        }

        return (offsetX, offsetY);
    }

    /// <summary>Reconnect: every original edge (cell -> its downstream cell) gets redrawn between
    /// the TWO cells' own final migrated positions, not just the migrated cells marked in isolation
    /// — a migration step can move a point several grid cells sideways over the course of the
    /// simulation, and without redrawing the connecting line the channel would fragment into
    /// disconnected dots exactly like the bug TileHydrology's own downstream-propagation fix already
    /// solved for the straight case.</summary>
    private static byte[] Rasterize(TerrainHeightmap grid, byte[] straightMask, int[] downstream,
        byte[] strahlerOrder, int[] offsetX, int[] offsetY)
    {
        var width = grid.Width;
        var height = grid.Height;
        var meandered = new byte[straightMask.Length];
        for (var idx = 0; idx < straightMask.Length; idx++)
        {
            if (straightMask[idx] == 0) continue;
            var value = strahlerOrder[idx];
            var next = downstream[idx];
            if (next < 0)
            {
                StampMax(meandered, offsetY[idx] * width + offsetX[idx], value);
                continue;
            }
            DrawLine(meandered, width, height, offsetX[idx], offsetY[idx], offsetX[next], offsetY[next], value);
        }

        return meandered;
    }

    /// <summary>Bresenham line rasterization, so two consecutive migrated points always end up
    /// 8-connected on the grid no matter how far apart the simulation put them. Stamps
    /// <paramref name="value"/> (the source cell's Strahler order) rather than a flat 1 — see
    /// <see cref="ApplyMeander"/>'s remarks.</summary>
    private static void DrawLine(byte[] mask, int width, int height, int x0, int y0, int x1, int y1, byte value)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = -Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;

        var x = x0;
        var y = y0;
        while (true)
        {
            if (x >= 0 && x < width && y >= 0 && y < height)
                StampMax(mask, y * width + x, value);
            if (x == x1 && y == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
    }

    /// <summary>Two different reaches' migrated lines can rasterize over the same pixel — keep
    /// whichever order is bigger rather than letting draw order arbitrarily decide, so a large
    /// river's line never gets accidentally overwritten by a small tributary passing near it.</summary>
    private static void StampMax(byte[] mask, int idx, byte value)
    {
        if (value > mask[idx]) mask[idx] = value;
    }
}
