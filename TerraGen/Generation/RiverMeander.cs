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
/// Stage 2 adds real neck-cutoff splicing — see <see cref="ApplyMeanderWithCutoffs"/>.
/// </summary>
public static class RiverMeander
{
    /// <summary>Normalization reference for <see cref="Parameters.BankErosionCoefficientE"/>.</summary>
    private const double DefaultBankErosionCoefficientE = 3e-8;

    /// <summary>Normalization reference for <see cref="Parameters.ScourFactor"/>.</summary>
    private const double DefaultScourFactor = 3.0;

    /// <summary>Fresh water density, kg/m³ (SI/physical constant, not calibrated).</summary>
    private const double WaterDensityKgPerM3 = 1000.0;

    /// <summary>Standard gravity, m/s² (CGPM 1901, g_n = 9.80665, rounded).</summary>
    private const double GravityMPerS2 = 9.81;

    /// <summary>Specific stream power ω = ρ·g·Q·S/w (W/m²). Source: van den Berg, J.H. (1995), Geomorphology 12:259-279 (form only — see <see cref="Parameters.StreamPowerSuppressionThresholdWPerM2"/>).</summary>
    internal static double ComputeSpecificStreamPowerWPerM2(double dischargeM3PerS, double slope, double channelWidthMeters)
        => WaterDensityKgPerM3 * GravityMPerS2 * dischargeM3PerS * slope / channelWidthMeters;

    /// <summary>A neck-cutoff's severed loop: removed backbone indices and their frozen (x,y) at the moment of the cut.</summary>
    internal readonly record struct SeveredLoop(int[] BackboneIndices, int[] OffsetX, int[] OffsetY);

    /// <summary>Edwards &amp; Smith (2002) decay length D = H/(2·C_f) — see <see cref="Parameters.FrictionCoefficient"/>.</summary>
    internal static double CurvatureMemoryLengthMeters(double channelDepthMeters, double frictionCoefficient)
        => channelDepthMeters / (2.0 * frictionCoefficient);

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
        /// <summary>IPS bank-erosion coefficient E, normalized against its own default (see <see cref="ErosionCoefficient"/>).
        /// Source: Camporeale, C., Perona, P., Porporato, A. &amp; Ridolfi, L. (2005), WRR 41:W12403 — field range [1e-8, 1e-7].</summary>
        double BankErosionCoefficientE = 3e-8,
        /// <summary>IPS scour/transverse-bed-slope factor (their "A", renamed to avoid clashing with drainage area).
        /// Source: Ikeda, S., Parker, G. &amp; Sawai, K. (1981), J. Fluid Mech. 112:363-377 — range [2.5, 6].
        /// TODO(Stage 2): Johannesson &amp; Parker (1989) depth-averaged correction not yet applied.</summary>
        double ScourFactor = 3.0,
        /// <summary>Alluvial width-to-depth ratio, used only to estimate depth for <see cref="FrictionCoefficient"/>'s decay length.
        /// Source: Leopold, L.B. &amp; Maddock, T. (1953), USGS Professional Paper 252 — typical range [10, 20].</summary>
        double WidthToDepthRatio = 15.0,
        /// <summary>Bed-friction coefficient C_f in the curvature-memory decay length D = H/(2·C_f).
        /// Source: Edwards, B.F. &amp; Smith, N.D. (2002), Physical Review E — typical range [0.003, 0.03].</summary>
        double FrictionCoefficient = 0.0056,
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
        /// <summary>Neck-cutoff trigger distance (channel widths); must stay below <see cref="MinSeparationPerWidth"/> (see <see cref="Validate"/>).
        /// Source: Camporeale, C., Perona, P., Porporato, A. &amp; Ridolfi, L. (2008), JGR 113:F01001 — ~1 channel width.</summary>
        double CutoffTriggerPerWidth = 1.0,
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
        /// <summary>Local slope above which migration is fully suppressed — deliberately conservative, not tuned to a discharge-specific threshold this generator can't simulate.
        /// Additive to <see cref="StreamPowerSuppressionThresholdWPerM2"/> (Stage 2) — either check alone can suppress.</summary>
        double SlopeSuppressedAbove = 0.08,
        /// <summary>Specific stream power (W/m²) above which meandering is suppressed — additive to <see cref="SlopeSuppressedAbove"/>, via <see cref="ComputeSpecificStreamPowerWPerM2"/>.
        /// UNCITED PLACEHOLDER: van den Berg (1995)'s real threshold is grain-size-dependent (no D50 field exists here) — only the ω=ρ·g·Q·S/w FORM is cited, not this number.</summary>
        double StreamPowerSuppressionThresholdWPerM2 = 300.0,
        /// <summary>Discharge-per-contributing-area conversion (m/s) feeding the stream-power calculation above.
        /// UNCITED PLACEHOLDER, order-of-magnitude only — no rainfall/runoff model exists here to calibrate it against.</summary>
        double DischargePerContributingAreaM2 = 1e-8)
    {
        /// <summary>Throws unless <see cref="CutoffTriggerPerWidth"/> &lt; <see cref="MinSeparationPerWidth"/>.</summary>
        public void Validate()
        {
            if (CutoffTriggerPerWidth >= MinSeparationPerWidth)
                throw new ArgumentException(
                    $"{nameof(CutoffTriggerPerWidth)} ({CutoffTriggerPerWidth}) must be strictly smaller than " +
                    $"{nameof(MinSeparationPerWidth)} ({MinSeparationPerWidth}), or damping would saturate before a cutoff could ever fire.");
        }
    }

    /// <summary>Takes the straight D8 mask <see cref="TileHydrology.ComputeDiagnostics"/> already
    /// computed (plus its accumulation/slope/downstream/Strahler-order/Shreve-magnitude arrays,
    /// which this reuses rather than recomputing) and returns a new mask (plus a co-indexed
    /// migrated Shreve-magnitude array) where every marked cell has migrated to wherever the
    /// bank-erosion simulation moved it. Cell count and shape match the input — this only ever
    /// redistributes WHERE within the same grid the channel is drawn, it doesn't add or remove
    /// catchment area or change the underlying accumulation/routing at all. The returned byte
    /// value at a river cell is its Strahler order (see
    /// <see cref="GameEngineTools.World.Data.TerrainHeightmap.RiverMask"/>'s remarks), not a flat 1
    /// — migration only changes shape, never what a cell's own order (or magnitude) was on the
    /// straight backbone.</summary>
    public static (byte[] Mask, int[] ShreveMagnitude) ApplyMeander(TerrainHeightmap grid, byte[] straightMask,
        int[] accumulation, double[] slope, int[] downstream, int[] order, byte[] strahlerOrder,
        int[] shreveMagnitude, Parameters p)
    {
        var (offsetX, offsetY, effectiveDownstream, active, _) =
            ComputeOffsets(grid, straightMask, accumulation, slope, downstream, order, p);
        return Rasterize(grid, active, effectiveDownstream, strahlerOrder, shreveMagnitude, offsetX, offsetY);
    }

    /// <summary>Stage 2: same simulation as <see cref="ApplyMeander"/> — cutoffs happen there too,
    /// this is the only entry point that also SURFACES the resulting oxbow lakes and Shreve
    /// magnitude together with the river mask, rather than discarding them.</summary>
    public static (byte[] RiverMask, int[] ShreveMagnitude, byte[] OxbowMask) ApplyMeanderWithCutoffs(
        TerrainHeightmap grid, byte[] straightMask, int[] accumulation, double[] slope,
        int[] downstream, int[] order, byte[] strahlerOrder, int[] shreveMagnitude, Parameters p)
    {
        var (offsetX, offsetY, effectiveDownstream, active, severedLoops) =
            ComputeOffsets(grid, straightMask, accumulation, slope, downstream, order, p);
        var (mask, magnitude) = Rasterize(grid, active, effectiveDownstream, strahlerOrder, shreveMagnitude, offsetX, offsetY);
        var oxbow = RasterizeOxbowLakes(grid, severedLoops);
        return (mask, magnitude, oxbow);
    }

    /// <summary>Rasterizes severed loops into a standalone still-water mask, separate from the main river mask.
    /// Source: Schwenk, J. &amp; Foufoula-Georgiou, E. (2016), GRL 43:12437-12445.</summary>
    internal static byte[] RasterizeOxbowLakes(TerrainHeightmap grid, IReadOnlyList<SeveredLoop> severedLoops)
    {
        var width = grid.Width;
        var height = grid.Height;
        var oxbow = new byte[width * height];

        foreach (var loop in severedLoops)
        {
            for (var k = 0; k < loop.OffsetX.Length; k++)
            {
                var x = loop.OffsetX[k];
                var y = loop.OffsetY[k];
                if (x >= 0 && x < width && y >= 0 && y < height) oxbow[y * width + x] = 1;

                if (k + 1 >= loop.OffsetX.Length) continue;
                ForEachLinePoint(width, height, x, y, loop.OffsetX[k + 1], loop.OffsetY[k + 1], idx => oxbow[idx] = 1);
            }
        }

        return oxbow;
    }

    /// <summary>Pre-rasterization step: final migrated (x,y) per cell, plus post-splice topology.</summary>
    internal static (int[] OffsetX, int[] OffsetY, int[] EffectiveDownstream, bool[] Active, IReadOnlyList<SeveredLoop> SeveredLoops) ComputeOffsets(
        TerrainHeightmap grid, byte[] straightMask, int[] accumulation, double[] slope, int[] downstream, int[] order, Parameters p)
    {
        p.Validate();
        var width = grid.Width;
        var height = grid.Height;
        var count = width * height;
        var cellSize = grid.CellSizeMeters;
        var cellAreaM2 = cellSize * cellSize;

        // Active backbone flags — starts identical to straightMask's own nonzero cells; a cutoff
        // (Stage 2) removes a cell from active use for the REST of the simulation without deleting
        // it from any array (its final frozen position still matters for oxbow rasterization).
        var active = new bool[count];
        for (var i = 0; i < count; i++) active[i] = straightMask[i] != 0;

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

        // Estimated channel depth (see Parameters.WidthToDepthRatio's remarks) — feeds ONLY the
        // Edwards & Smith (2002) curvature-memory decay length D=H/(2·C_f) below, the same way
        // channelWidth already estimates a physical scale this generator has no simulated discharge
        // to derive directly.
        var channelDepth = new double[count];
        for (var i = 0; i < count; i++)
            channelDepth[i] = channelWidth[i] / p.WidthToDepthRatio;

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

        // Mutable copies a cutoff can locally splice, without touching the pre-cutoff originals above.
        var curPred = (int[])predecessor.Clone();
        var curDown = (int[])downstream.Clone();

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

        // Erodibility: zero on steep ground (Leopold & Wolman 1957), full strength on gentle ground.
        var physicalFactor = (p.BankErosionCoefficientE * p.ScourFactor) / (DefaultBankErosionCoefficientE * DefaultScourFactor);
        var erodibility = new double[count];
        for (var i = 0; i < count; i++)
        {
            if (straightMask[i] == 0) continue;
            var here = slope[i];
            var suppression = here <= p.SlopeFullMeanderBelow ? 1.0
                : here >= p.SlopeSuppressedAbove ? 0.0
                : 1.0 - (here - p.SlopeFullMeanderBelow) / (p.SlopeSuppressedAbove - p.SlopeFullMeanderBelow);

            // Stream-power gate, additive to the flat-slope check above (either alone can suppress).
            var dischargeM3PerS = accumulation[i] * cellAreaM2 * p.DischargePerContributingAreaM2;
            var streamPowerWPerM2 = ComputeSpecificStreamPowerWPerM2(dischargeM3PerS, here, channelWidth[i]);
            if (streamPowerWPerM2 >= p.StreamPowerSuppressionThresholdWPerM2) suppression = 0.0;

            erodibility[i] = p.ErosionCoefficient * suppression * physicalFactor;
        }

        // Seed phase, propagated incrementally along arc length like a running total (NOT
        // recomputed from raw (x,y) position) — its wavelength has to scale with each reach's OWN
        // channel width, not a fixed spatial frequency, or the seed oscillates faster than the
        // curvature convolution's own memory window can track. Confirmed live: with a
        // width-independent seed, a wide trunk river (23m width, ~27-cell memory window) barely
        // moved (0.085m average over the whole simulation) while narrow creeks moved freely (2m+) —
        // the trunk's memory window spanned 4+ full seed oscillation cycles, so the alternating
        // +/- signal averaged itself out to near-zero before the migration feedback ever got a
        // usable direction to amplify. Tying the seed's own wavelength to the SAME Edwards & Smith
        // decay length the migration feedback itself uses (see the CurvatureMemoryLengthMeters local
        // function below) keeps roughly one seed cycle per memory window at any channel size, so
        // there's always a coherent (not self-cancelling) direction to grow.
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
            var seedWavelength = Math.Max(cellSize, DecayLengthAt(idx));
            seedPhase[next] = seedPhase[idx] + 2.0 * Math.PI * stepDist / seedWavelength;
        }

        // Edwards & Smith (2002) upstream curvature-memory decay length D = H/(2·C_f) — see
        // Parameters.FrictionCoefficient's remarks and the standalone CurvatureMemoryLengthMeters
        // itself. Local function wrapper (not inlined) since it's needed identically in both the
        // seed-wavelength calculation above and the per-iteration convolution below.
        double DecayLengthAt(int cellIndex) => CurvatureMemoryLengthMeters(channelDepth[cellIndex], p.FrictionCoefficient);

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
        var severedLoops = new List<SeveredLoop>();

        // World-space-meters -> grid-cell, shared by the final offset pass and cutoff freezing.
        int GridX(double px) => Math.Clamp((int)Math.Round((px - grid.OriginX) / cellSize), 0, width - 1);
        int GridY(double py) => Math.Clamp((int)Math.Round((py - grid.OriginY) / cellSize), 0, height - 1);

        for (var iter = 0; iter < p.Iterations; iter++)
        {
            // Curvature at every point with both a predecessor and a valid successor — signed
            // turning angle from (predecessor->here) to (here->successor), per unit length. Sign
            // convention only has to be self-consistent with the normal direction below; whichever
            // way it comes out, the feedback amplifies whatever direction a point already bends.
            for (var i = 0; i < count; i++)
            {
                curvature[i] = 0.0;
                if (!active[i]) continue;
                var pred = curPred[i];
                var next = curDown[i];
                if (pred < 0 || next < 0 || !active[next]) continue;

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
                if (!active[i]) continue;
                if (curPred[i] < 0) { convolvedCurvature[i] = curvature[i]; continue; }
            }
            foreach (var idx in order)
            {
                if (!active[idx]) continue;
                var next = curDown[idx];
                if (next < 0 || !active[next]) continue;
                if (curPred[next] != idx) continue; // only the dominant edge carries memory forward

                var stepDist = Math.Sqrt(Math.Pow(posX[next] - posX[idx], 2) + Math.Pow(posY[next] - posY[idx], 2));
                var decayLength = Math.Max(cellSize, DecayLengthAt(idx));
                var decayWeight = Math.Exp(-stepDist / decayLength);
                convolvedCurvature[next] = curvature[next] * (1.0 - decayWeight) + convolvedCurvature[idx] * decayWeight;
            }

            // Proposed migration step for every eligible point (heads/mouths excluded — fixed
            // anchors, same as the seed perturbation above skipped them).
            Array.Copy(posX, newPosX, count);
            Array.Copy(posY, newPosY, count);

            for (var i = 0; i < count; i++)
            {
                if (!active[i]) continue;
                var pred = curPred[i];
                var next = curDown[i];
                if (pred < 0 || next < 0 || !active[next]) continue;

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

            // Collision-avoidance: bucket proposed positions; too close either cuts or damps below.
            var buckets = new Dictionary<(int, int), List<int>>();
            for (var i = 0; i < count; i++)
            {
                if (!active[i]) continue;
                var key = ((int)Math.Floor(newPosX[i] / bucketSize), (int)Math.Floor(newPosY[i] / bucketSize));
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
                list.Add(i);
            }

            for (var i = 0; i < count; i++)
            {
                if (!active[i]) continue;
                var pred = curPred[i];
                var next = curDown[i];
                if (pred < 0 || next < 0 || !active[next]) continue;

                var minAllowed = p.MinSeparationPerWidth * channelWidth[i];
                var cutoffThreshold = p.CutoffTriggerPerWidth * channelWidth[i];
                var bx = (int)Math.Floor(newPosX[i] / bucketSize);
                var by = (int)Math.Floor(newPosY[i] / bucketSize);
                var closest = double.PositiveInfinity;
                var closestJ = -1;

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
                            if (d < closest) { closest = d; closestJ = j; }
                        }
                    }
                }

                if (closest < cutoffThreshold && closestJ >= 0 &&
                    TryFindLoop(i, closestJ, curDown, active, count, out var loopIndices, out var upstreamEnd, out var downstreamEnd))
                {
                    // Genuine same-channel neck cutoff — freeze the severed loop's positions (its
                    // final, permanent shape as a stagnant oxbow lake), remove it from the active
                    // backbone, and splice the chain directly across the gap.
                    var loopOffsetX = new int[loopIndices.Count];
                    var loopOffsetY = new int[loopIndices.Count];
                    for (var k = 0; k < loopIndices.Count; k++)
                    {
                        var li = loopIndices[k];
                        loopOffsetX[k] = GridX(newPosX[li]);
                        loopOffsetY[k] = GridY(newPosY[li]);
                        active[li] = false;
                    }
                    severedLoops.Add(new SeveredLoop(loopIndices.ToArray(), loopOffsetX, loopOffsetY));
                    curDown[upstreamEnd] = downstreamEnd;
                    curPred[downstreamEnd] = upstreamEnd;
                    continue; // this iteration's positions for i/j stand — that closeness IS the pinched-neck geometry
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
            if (!active[i]) continue;
            offsetX[i] = GridX(posX[i]);
            offsetY[i] = GridY(posY[i]);
        }

        return (offsetX, offsetY, curDown, active, severedLoops);
    }

    /// <summary>Neck-cutoff support: determines whether <paramref name="start"/> and
    /// <paramref name="target"/> lie on the SAME channel — one reachable from the other by
    /// following <paramref name="curDown"/> — and if so, which is upstream/downstream and which
    /// cells lie strictly between them (the loop a cutoff would sever). Walks forward from each end
    /// in turn (bounded by <paramref name="maxHops"/>, a safe upper bound on any real chain length)
    /// rather than assuming a direction, since either point could be the upstream one. Returns
    /// <c>false</c> (no loop) when neither walk reaches the other — the two points merely drifted
    /// spatially close on DIFFERENT tributaries, which isn't a real neck cutoff (splicing across
    /// unrelated branches wouldn't be topologically meaningful).</summary>
    private static bool TryFindLoop(int start, int target, int[] curDown, bool[] active, int maxHops,
        out List<int> loopIndices, out int upstreamEnd, out int downstreamEnd)
    {
        var path = new List<int>();
        var cur = curDown[start];
        var hops = 0;
        while (cur >= 0 && active[cur] && cur != target && hops++ < maxHops)
        {
            path.Add(cur);
            cur = curDown[cur];
        }
        if (cur == target)
        {
            loopIndices = path;
            upstreamEnd = start;
            downstreamEnd = target;
            return true;
        }

        path.Clear();
        cur = curDown[target];
        hops = 0;
        while (cur >= 0 && active[cur] && cur != start && hops++ < maxHops)
        {
            path.Add(cur);
            cur = curDown[cur];
        }
        if (cur == start)
        {
            loopIndices = path;
            upstreamEnd = target;
            downstreamEnd = start;
            return true;
        }

        loopIndices = null!;
        upstreamEnd = -1;
        downstreamEnd = -1;
        return false;
    }

    /// <summary>Reconnect: every EFFECTIVE edge (a still-active cell -> its effective downstream
    /// cell, post-cutoff-splicing — see <see cref="ComputeOffsets"/>'s remarks) gets redrawn between
    /// the TWO cells' own final migrated positions, not just the migrated cells marked in isolation
    /// — a migration step can move a point several grid cells sideways over the course of the
    /// simulation, and without redrawing the connecting line the channel would fragment into
    /// disconnected dots exactly like the bug TileHydrology's own downstream-propagation fix already
    /// solved for the straight case. Shreve magnitude is stamped alongside the Strahler-order mask
    /// from the SAME source cell at every rasterized pixel — see <see cref="StampMax"/> — rather
    /// than in a separate pass, so a pixel's magnitude always corresponds to whichever reach's line
    /// actually "won" that pixel, not an unrelated reach that happened to draw over it too. Cells a
    /// cutoff removed from <paramref name="active"/> are skipped entirely — they belong only to
    /// <see cref="RasterizeOxbowLakes"/>'s output now, not the main channel.</summary>
    private static (byte[] Mask, int[] ShreveMagnitude) Rasterize(TerrainHeightmap grid, bool[] active,
        int[] effectiveDownstream, byte[] strahlerOrder, int[] shreveMagnitude, int[] offsetX, int[] offsetY)
    {
        var width = grid.Width;
        var height = grid.Height;
        var meandered = new byte[active.Length];
        var meanderedMagnitude = new int[active.Length];
        for (var idx = 0; idx < active.Length; idx++)
        {
            if (!active[idx]) continue;
            var value = strahlerOrder[idx];
            var magnitude = shreveMagnitude[idx];
            var next = effectiveDownstream[idx];
            if (next < 0 || !active[next])
            {
                StampMax(meandered, meanderedMagnitude, offsetY[idx] * width + offsetX[idx], value, magnitude);
                continue;
            }
            ForEachLinePoint(width, height, offsetX[idx], offsetY[idx], offsetX[next], offsetY[next],
                lineIdx => StampMax(meandered, meanderedMagnitude, lineIdx, value, magnitude));
        }

        return (meandered, meanderedMagnitude);
    }

    /// <summary>Bresenham line rasterization shared by <see cref="Rasterize"/> (the main river
    /// channel) and <see cref="RasterizeOxbowLakes"/> (severed-loop still water) — the exact same
    /// point-to-point line-plotting logic either way, only WHAT gets stamped at each point differs,
    /// so that decision is left entirely to <paramref name="plot"/>. Ensures two consecutive
    /// migrated points always end up 8-connected on the grid no matter how far apart the simulation
    /// put them.</summary>
    private static void ForEachLinePoint(int width, int height, int x0, int y0, int x1, int y1, Action<int> plot)
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
                plot(y * width + x);
            if (x == x1 && y == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
    }

    /// <summary>Two different reaches' migrated lines can rasterize over the same pixel — keep
    /// whichever order (and its co-indexed magnitude) is bigger rather than letting draw order
    /// arbitrarily decide, so a large river's line never gets accidentally overwritten by a small
    /// tributary passing near it.</summary>
    private static void StampMax(byte[] mask, int[] magnitudeMask, int idx, byte value, int magnitude)
    {
        if (value <= mask[idx]) return;
        mask[idx] = value;
        magnitudeMask[idx] = magnitude;
    }
}
