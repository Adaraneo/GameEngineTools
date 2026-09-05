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
/// Stage 2 adds real neck-cutoff splicing (see <see cref="ApplyMeanderWithCutoffs"/>): once a bend's
/// proposed step would bring it within <see cref="Parameters.CutoffTriggerPerWidth"/> channel widths
/// of a non-adjacent point on the SAME channel, the intervening loop is severed from the active
/// backbone and rasterized separately as a still-water oxbow lake, rather than merely damped —
/// Howard &amp; Knutson (1984) report meandering channels typically migrate 5-7 channel widths
/// before this actually happens in nature. Below that tighter threshold, the original damping
/// still applies (see <see cref="Parameters.MinSeparationPerWidth"/>) — a bend slows down as it
/// approaches self-intersection before it actually cuts, it doesn't jump straight from "unaffected"
/// to "severed." <see cref="ApplyMeander"/> keeps its original 2-tuple signature for existing
/// callers (cutoffs still happen, only the oxbow output is discarded).
/// </summary>
public static class RiverMeander
{
    /// <summary>Reference point <see cref="Parameters.BankErosionCoefficientE"/>/<see cref="Parameters.ScourFactor"/>
    /// are normalized against — see <see cref="Parameters.BankErosionCoefficientE"/>'s remarks.</summary>
    private const double DefaultBankErosionCoefficientE = 3e-8;

    /// <summary>Reference point <see cref="Parameters.ScourFactor"/> is normalized against — see
    /// <see cref="Parameters.BankErosionCoefficientE"/>'s remarks.</summary>
    private const double DefaultScourFactor = 3.0;

    /// <summary>Fresh water density, kg/m³ — not literature-specific, but named per the project's
    /// own convention of never using bare magic numbers (see e.g. <see cref="FillEpsilon"/> in
    /// <see cref="TileHydrology"/>). Used only by <see cref="ComputeSpecificStreamPowerWPerM2"/>.
    /// Source: standard value at ~4°C / SI definition (a physical constant, not a calibrated one).</summary>
    private const double WaterDensityKgPerM3 = 1000.0;

    /// <summary>Standard gravity, m/s² — see <see cref="WaterDensityKgPerM3"/>'s remarks. Used only
    /// by <see cref="ComputeSpecificStreamPowerWPerM2"/>.
    /// Source: CGPM (1901) standard gravity g_n = 9.80665 m/s², rounded to 9.81 (a physical
    /// constant, not a calibrated one).</summary>
    private const double GravityMPerS2 = 9.81;

    /// <summary>Specific stream power <c>ω = ρ·g·Q·S/w</c> (W/m²) — van den Berg's (1995) alluvial
    /// channel-pattern discriminant. A standalone method (like <see cref="CurvatureMemoryLengthMeters"/>)
    /// specifically so this exact formula can be unit-tested in isolation against hand-calculated
    /// values.
    /// <para>Source: van den Berg, J.H. (1995). "Prediction of alluvial channel pattern of perennial
    /// rivers." <i>Geomorphology</i> 12:259-279 (form of the relationship only — see
    /// <see cref="Parameters.StreamPowerSuppressionThresholdWPerM2"/>'s remarks for why no specific
    /// threshold NUMBER from that source is cited here).</para></summary>
    internal static double ComputeSpecificStreamPowerWPerM2(double dischargeM3PerS, double slope, double channelWidthMeters)
        => WaterDensityKgPerM3 * GravityMPerS2 * dischargeM3PerS * slope / channelWidthMeters;

    /// <summary>A neck-cutoff's severed loop (Stage 2 — see the type-level remarks and
    /// <see cref="ApplyMeanderWithCutoffs"/>): the backbone cell indices removed from the active
    /// channel, and where each one's migrated position was at the moment of the cut — frozen there
    /// permanently, since a severed loop is stagnant water from that point on, not still
    /// migrating.</summary>
    internal readonly record struct SeveredLoop(int[] BackboneIndices, int[] OffsetX, int[] OffsetY);

    /// <summary>Edwards &amp; Smith (2002) upstream curvature-memory decay length
    /// <c>D = H / (2·C_f)</c> — see <see cref="Parameters.FrictionCoefficient"/>'s remarks. A
    /// standalone method (not inlined into <see cref="ComputeOffsets"/>) specifically so this exact
    /// formula can be unit-tested in isolation against hand-calculated values.</summary>
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
        /// <summary>Ikeda, Parker &amp; Sawai's (1981) bank-erosion coefficient E (dimensionless) —
        /// how strongly the near-bank excess velocity a bend's curvature creates actually erodes the
        /// outer bank, in the linearized IPS bend theory this whole simulation is built on.
        /// <para>Source: field-calibrated range <c>E ∈ [1e-8, 1e-7]</c>; reference value
        /// <c>E = 3×10⁻⁸</c> used in Camporeale, C., Perona, P., Porporato, A. &amp; Ridolfi, L.
        /// (2005). "On the long-term behavior of meandering rivers." <i>Water Resources Research</i>
        /// 41:W12403.</para>
        /// E primarily rescales the SIMULATION'S OWN time axis (larger E migrates a bend further per
        /// iteration), not the resulting planform SHAPE — <see cref="ErosionCoefficient"/> above is
        /// what this generator actually live-tunes for visual/gameplay pacing (it has no simulated
        /// discharge to plug a literal E into and get a meaningful meters-per-iteration answer from),
        /// so E and <see cref="ScourFactor"/> enter the migration formula NORMALIZED against their
        /// own defaults — moving either within its cited range scales migration up/down from
        /// whatever <see cref="ErosionCoefficient"/> already produces, in the SAME proportion IPS's
        /// linear theory says it should (E doubled -> migration doubled), without silently changing
        /// the already-calibrated default look the moment this field was introduced.</summary>
        double BankErosionCoefficientE = 3e-8,
        /// <summary>Ikeda, Parker &amp; Sawai's (1981) scour/transverse-bed-slope factor — called
        /// "A" in the source's own near-bank-velocity equation, renamed here to avoid confusion with
        /// drainage AREA elsewhere in this codebase (see e.g. <see cref="TileHydrology"/>).
        /// <para>Source: range <c>2.5-6</c>, commonly ≈3. Ikeda, S., Parker, G. &amp; Sawai, K.
        /// (1981). "Bend theory of river meanders. Part 1. Linear development." <i>Journal of Fluid
        /// Mechanics</i> 112:363-377. doi:10.1017/S0022112081000451. Also see Odgaard, A.J. (1981)
        /// for a consistent field range.</para>
        /// TODO(Stage 2): Johannesson &amp; Parker (1989) correct this coefficient for a
        /// depth-averaged (rather than IPS's original near-surface) velocity profile — deliberately
        /// NOT implemented in this pass, which only relabels/re-ranges the existing IPS constant
        /// correctly; the JP correction needs its own research-verified equation form. See
        /// <see cref="BankErosionCoefficientE"/>'s remarks for how this enters the migration formula
        /// alongside <see cref="ErosionCoefficient"/>.</summary>
        double ScourFactor = 3.0,
        /// <summary>Typical alluvial-channel width-to-depth ratio, used ONLY to estimate channel
        /// depth H (= width / this ratio) for <see cref="FrictionCoefficient"/>'s D=H/(2·Cf) decay
        /// length below — this generator has no simulated discharge to derive a real depth from, the
        /// same reason <see cref="WidthPerSqrtAreaM2"/> approximates width from area instead of a
        /// real width-discharge relationship.
        /// Source: typical range 10-20 for alluvial (sand/gravel-bed) channels — Leopold, L.B. &amp;
        /// Maddock, T. (1953). "The hydraulic geometry of stream channels and some physiographic
        /// implications." USGS Professional Paper 252.</summary>
        double WidthToDepthRatio = 15.0,
        /// <summary>Dimensionless bed-friction coefficient C_f in Edwards &amp; Smith's (2002)
        /// upstream curvature-memory decay length <c>D = H / (2·C_f)</c> (H = local channel depth,
        /// estimated via <see cref="WidthToDepthRatio"/>) — this REPLACES what used to be a flat
        /// "N channel widths" decay-length constant with one actually derived from the channel's own
        /// depth and bed roughness, so the memory length scales per-reach instead of being globally
        /// fixed. Source: typical range 0.003-0.03 depending on bed roughness. Edwards, B.F. &amp;
        /// Smith, N.D. (2002). "River meandering dynamics." <i>Physical Review E</i>.
        /// Default 0.0056 was picked, within that cited range, specifically to reproduce (via
        /// D=H/(2·C_f) with the <see cref="WidthToDepthRatio"/> default) almost exactly the same
        /// decay length the old flat "6× channel width" constant it replaces already had live-tuned
        /// — this formula change is a re-derivation of an already-working number, not a re-tune of
        /// the resulting shapes.</summary>
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
        /// <summary>Neck-cutoff trigger distance, in multiples of local channel width. When a
        /// point's proposed position would fall within this distance of a non-adjacent point on the
        /// SAME channel network (i.e. reachable from it by following <c>predecessor</c>/
        /// <c>downstream</c> links, not a different tributary — see <see cref="ComputeOffsets"/>'s
        /// remarks), the intervening loop is severed and rasterized separately as an oxbow lake
        /// (<see cref="RasterizeOxbowLakes"/>) instead of being damped further. Must be strictly
        /// smaller than <see cref="MinSeparationPerWidth"/> — enforced by <see cref="Validate"/> —
        /// so damping still has a chance to act as a bend approaches self-intersection before an
        /// actual cutoff fires; a cutoff threshold that never engages before damping saturates would
        /// silently disable this feature.
        /// <para>Source: Camporeale, C., Perona, P., Porporato, A. &amp; Ridolfi, L. (2008).
        /// "Significance of cutoff in meandering river dynamics." <i>Journal of Geophysical
        /// Research</i> 113:F01001. doi:10.1029/2006JF000600. Cutoff trigger at approximately 1
        /// channel width of neck separation.</para></summary>
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
        /// <summary>Local slope above which migration is fully suppressed and the channel stays on
        /// its straight D8 path — deliberately conservative (well past a typical lowland grade)
        /// rather than tuned to Leopold &amp; Wolman's own discharge-specific threshold line, which
        /// needs real discharge this generator doesn't simulate. Stage 2's
        /// <see cref="StreamPowerSuppressionThresholdWPerM2"/> now partially addresses that
        /// disclaimer with an actual discharge-aware criterion — but this flat-slope check remains,
        /// additively, as a cheap independent filter for genuinely steep terrain regardless of
        /// discharge (a suppression from EITHER check applies; neither replaces the other).</summary>
        double SlopeSuppressedAbove = 0.08,
        /// <summary>Specific stream power (W/m²) above which the channel pattern switches from
        /// single-thread meandering to a suppressed/braided-tendency regime — an ADDITIONAL,
        /// independent suppression gate alongside (not replacing) <see cref="SlopeSuppressedAbove"/>,
        /// computed via <see cref="ComputeSpecificStreamPowerWPerM2"/>.
        /// <para>The real van den Berg (1995) threshold is grain-size-dependent (<c>ω ∝ D50^0.42</c>,
        /// calibrated across 126 streams/rivers — see "Prediction of alluvial channel pattern of
        /// perennial rivers," <i>Geomorphology</i> 12:259-279) and this codebase has no D50/grain-
        /// size field to plug into it. Rather than invent an unstated D50 assumption, this default
        /// is an UNCITED PLACEHOLDER — only the FORM of the relationship (ω = ρ·g·Q·S/w) is cited,
        /// not a specific literature number. Tune per biome/climate once a grain-size field exists,
        /// or treat it as a pure gameplay/visual dial until then.</para></summary>
        double StreamPowerSuppressionThresholdWPerM2 = 300.0,
        /// <summary>Rough discharge-per-unit-contributing-area conversion (m/s — a specific-runoff-
        /// yield-style rate) used to turn contributing area into an estimated bankfull discharge
        /// for <see cref="StreamPowerSuppressionThresholdWPerM2"/>'s stream-power calculation, since
        /// this generator has no simulated rainfall/runoff model.
        /// <para>UNCITED PLACEHOLDER, not a literature-calibrated regional value (order-of-magnitude
        /// only, loosely comparable to a temperate-climate specific runoff yield of roughly
        /// 10 L/s/km²) — tune per biome/climate rather than treating it as a cited constant.</para></summary>
        double DischargePerContributingAreaM2 = 1e-8)
    {
        /// <summary>Throws if this instance is internally inconsistent. Currently only checks that
        /// <see cref="CutoffTriggerPerWidth"/> is strictly smaller than
        /// <see cref="MinSeparationPerWidth"/> — see that field's own remarks for why. Called
        /// automatically by <see cref="ComputeOffsets"/> (hence by <see cref="ApplyMeander"/> and
        /// <see cref="ApplyMeanderWithCutoffs"/>); call directly only to validate a Parameters
        /// instance before it's actually used.</summary>
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

    /// <summary>Rasterizes every <see cref="SeveredLoop"/> a meander simulation cut off into a
    /// standalone still-water mask — the oxbow lake left behind after a neck cutoff. Separate from
    /// the main river mask so a caller can render/tag it distinctly (no flow, no Strahler order,
    /// eventually stagnant).
    /// <para>Source: Schwenk, J. &amp; Foufoula-Georgiou, E. (2016). "Meander cutoffs nonlocally
    /// accelerate upstream and downstream migration and channel widening." <i>Geophysical Research
    /// Letters</i> 43:12437-12445. doi:10.1002/2016GL071670 — cutoffs sever a loop which becomes an
    /// oxbow lake.</para></summary>
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

    /// <summary>Same computation as <see cref="ApplyMeander"/>/<see cref="ApplyMeanderWithCutoffs"/>,
    /// but stops short of rasterizing — returns each still-active backbone cell's own final migrated
    /// (x,y), the EFFECTIVE downstream connectivity (identical to <paramref name="downstream"/>
    /// except where a cutoff spliced across a severed loop), which cells are still active (identical
    /// to <paramref name="straightMask"/>'s own nonzero cells except wherever a cutoff removed one),
    /// and the list of cutoffs that actually fired. Not needed by any production caller directly
    /// (which only wants the final masks), but lets a test or investigation measure the simulation's
    /// actual output — path length, sinuosity, self-crossing, cutoffs — directly from where cells
    /// ended up, instead of only from how many raster pixels ended up lit.</summary>
    /// <remarks>
    /// Neck-cutoff detection (Stage 2) reuses the SAME per-iteration proximity check that already
    /// existed for collision-avoidance damping, just with a second, tighter comparison against
    /// <see cref="Parameters.CutoffTriggerPerWidth"/> (validated smaller than
    /// <see cref="Parameters.MinSeparationPerWidth"/> — see <see cref="Parameters.Validate"/>):
    /// when a point's proposed step lands within that tighter distance of a non-adjacent point, this
    /// only actually severs a loop if the two points are genuinely on the SAME channel (one
    /// reachable from the other by following the current downstream chain, not two different
    /// tributaries that merely drifted close together spatially — splicing across unrelated
    /// branches wouldn't be topologically meaningful). When they are, the run of backbone cells
    /// strictly between them is frozen at its current position (recorded as a
    /// <see cref="SeveredLoop"/>), marked inactive, and the chain is spliced directly across the gap
    /// (a local, in-place update — only the two endpoints' own predecessor/downstream pointers
    /// change); every later iteration then sees the shortened chain. When the two points are NOT on
    /// the same channel, the proximity is handled exactly as before: plain damping against
    /// <see cref="Parameters.MinSeparationPerWidth"/>, unaffected by this Stage.
    /// </remarks>
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

        // Mutable working copies used ONLY inside the per-iteration simulation loop below — a
        // cutoff (Stage 2) locally rewrites exactly two entries (the splice endpoints) in these,
        // never in the original `predecessor`/`downstream` arrays, which everything computed ABOVE
        // this point (seed phase, initial perturbation, nearbyInChain) already correctly used the
        // pre-cutoff topology for and never needs to see a splice.
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

        // Erodibility: how strongly a cell's curvature signal actually translates into migration —
        // zero on steep ground (real channels there don't get the chance to wander before just
        // cutting straight downhill, Leopold & Wolman 1957), full strength on gentle ground.
        // physicalFactor folds in BankErosionCoefficientE and ScourFactor (IPS's own E and A)
        // NORMALIZED against their own defaults — see BankErosionCoefficientE's remarks for why: it
        // keeps ErosionCoefficient's already-live-tuned default look exactly unchanged (factor=1.0
        // at defaults) while still making E/A individually meaningful, cited, in-range dials that
        // scale migration in the same linear proportion IPS's theory says they should.
        var physicalFactor = (p.BankErosionCoefficientE * p.ScourFactor) / (DefaultBankErosionCoefficientE * DefaultScourFactor);
        var erodibility = new double[count];
        for (var i = 0; i < count; i++)
        {
            if (straightMask[i] == 0) continue;
            var here = slope[i];
            var suppression = here <= p.SlopeFullMeanderBelow ? 1.0
                : here >= p.SlopeSuppressedAbove ? 0.0
                : 1.0 - (here - p.SlopeFullMeanderBelow) / (p.SlopeSuppressedAbove - p.SlopeFullMeanderBelow);

            // Stream-power suppression gate (Stage 2 — see Parameters.StreamPowerSuppressionThresholdWPerM2's
            // remarks): ADDITIVE to the flat-slope check above, not a replacement — either one alone
            // can suppress migration, since the flat-slope check stays a legitimate cheap pre-filter
            // for genuinely steep terrain regardless of discharge.
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

        // Same world-space-meters -> grid-cell conversion the final offset pass below already did
        // inline — factored out here too since a cutoff needs to freeze a severed loop's grid
        // position at the moment of the cut, not just at the very end of the simulation.
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

            // Collision-avoidance: bucket the PROPOSED positions of still-active points, and for any
            // point whose new position landed too close to a non-adjacent point, either (Stage 2)
            // sever the intervening loop as a genuine neck cutoff, or (unchanged from before) shrink
            // the step instead of applying it in full.
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
