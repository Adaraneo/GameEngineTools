using GameEngineTools.World.Data;

namespace TerraGen.Generation;

/// <summary>Stream-Power Incision Model (SPIM) — an emergent relief solver replacing the ridged-noise mountain layer when enabled via --spim. Source: Braun, J. &amp; Willett, S.D. (2013), Geomorphology 180:170-179, doi:10.1016/j.geomorph.2012.10.008.</summary>
/// <remarks>Governing equation dh/dt = U - K·A^m·S^n, solved per node in lowest-to-highest topological order (Braun &amp; Willett §2.1) against each node's already-updated receiver — n=1 (see Parameters.N) makes this a direct closed-form solve, no Newton-Raphson needed.</remarks>
public static class StreamPowerErosion
{
    public sealed record Parameters(
        /// <summary>Drainage-area exponent. Source: Whipple &amp; Tucker (1999), JGR 104(B8):17661-17674 — typical concavity m/n ≈ 0.5.</summary>
        double M = 0.5,
        /// <summary>⚠ Design simplification (not literature consensus): n=1 enables the closed-form implicit update; Harel, Mudd &amp; Attal (2016), Geomorphology 268:184-196 report n&gt;1 in many settings.</summary>
        double N = 1.0,
        /// <summary>Erodibility. Source: Cordonnier et al. (2016), Computer Graphics Forum 35(2):165-175 — representative mid-range value; real K spans ~5 orders of magnitude by lithology (Stock &amp; Montgomery 1999) — see Stage 2.</summary>
        double K = 5.61e-7,
        /// <summary>Source: Cordonnier et al. (2016) report 100-300 iterations to convergence.</summary>
        int Iterations = 200,
        /// <summary>Source: Cordonnier et al. (2016).</summary>
        double TimestepYears = 2.5e5);

    /// <summary>Empirical lower bound on tectonic uplift rate. Source: McGrath et al. (2025).</summary>
    public const double MinUpliftMmPerYear = 0.5;

    /// <summary>Empirical upper bound on tectonic uplift rate. Source: Vance et al. (2003).</summary>
    public const double MaxUpliftMmPerYear = 13.0;

    /// <summary>⚠ Design simplification: TectonicPlates' relative-velocity magnitude is a dimensionless sim unit, not m/yr — this is the approach-rate value at which uplift saturates at MaxUpliftMmPerYear (tuned so a typical strong convergent boundary sits near the top of the empirical range).</summary>
    private const double ApproachRateAtMaxUplift = 1.5;

    /// <summary>Erodes <paramref name="grid"/> in place by running <see cref="Parameters.Iterations"/> implicit SPIM timesteps. <paramref name="locked"/> (same convention as <see cref="TileErosion.Erode"/>) marks cells read but never written. <paramref name="erodibilityPerCell"/> (Stage 2, <see cref="RockLayer"/>) overrides the scalar <see cref="Parameters.K"/> per cell when given — null keeps the single global K, byte-identical to Stage 1. <paramref name="isostasyParams"/> (Stage 3.1) turns on the erosion→unloading→rebound feedback loop — when set, accumulated erosional height loss is added DIRECTLY back into <paramref name="grid"/>'s own elevation every recompute interval (a one-time state correction, not a permanent addition to <paramref name="upliftMetersPerYear"/> — the latter compounded without bound across iterations in an earlier version of this method and produced runaway +Infinity terrain); null keeps Stage 1/2 behavior, byte-identical. <paramref name="precipitationWeightPerCell"/> (Stage 4, <see cref="OrographicPrecipitation"/>) replaces bare cell-count drainage area with a precipitation-weighted accumulation computed ONCE up front against the terrain <paramref name="grid"/> has at the moment <see cref="Erode"/> is called (a static-climate approximation — recomputing the FFT every iteration would multiply SPIM's own already-heavy cost by <see cref="Parameters.Iterations"/> for a second-order feedback); null keeps the unweighted D8 count, byte-identical to Stages 1-3. <paramref name="onDiagnostic"/> receives one line right before a fail-fast <see cref="InvalidOperationException"/> if any solved height ever comes out non-finite — the exact cell/iteration/parameter dump, instead of that NaN silently propagating into a cryptic crash somewhere downstream (e.g. TileErosion).</summary>
    public static void Erode(TerrainHeightmap grid, Parameters p, double[] upliftMetersPerYear, bool[]? locked = null,
        double[]? erodibilityPerCell = null, Isostasy.Parameters? isostasyParams = null, double[]? crustDensityPerCell = null,
        double[]? precipitationWeightPerCell = null, Action<string>? onDiagnostic = null)
    {
        if (p.Iterations <= 0 || grid.Width < 2 || grid.Height < 2) return;

        var width = grid.Width;
        var cellSize = grid.CellSizeMeters;
        var cellAreaM2 = cellSize * cellSize;
        var dt = p.TimestepYears;

        // Accumulates, per cell, how much LOWER each solved height came out than pure uplift alone
        // would have produced that iteration — i.e. the erosional height loss the implicit step just
        // applied — since the combined implicit update doesn't separate the two terms explicitly.
        var erodedHeightAccumulator = isostasyParams is not null ? new double[grid.Values.Length] : null;
        var iterationsSinceRebound = 0;

        for (var iter = 0; iter < p.Iterations; iter++)
        {
            var routing = FlowRouting.Compute(grid);
            var downstream = routing.Downstream;
            var stack = routing.Stack; // highest-to-lowest routed elevation
            var accumulation = routing.Accumulation;
            var weightedAccumulation = precipitationWeightPerCell is not null
                ? FlowRouting.ComputeWeightedAccumulation(downstream, stack, precipitationWeightPerCell)
                : null;

            // Reverse of the accumulation order: lowest elevation first, so a node's receiver
            // (strictly lower, hence visited earlier here) already holds its updated height.
            for (var i = stack.Length - 1; i >= 0; i--)
            {
                var idx = stack[i];
                if (locked is not null && locked[idx]) continue;

                var next = downstream[idx];
                if (next < 0) continue; // grid boundary treated as a fixed base level, same convention FlowRouting's own fill/drain logic uses

                var idxX = idx % width;
                var idxY = idx / width;
                var nextX = next % width;
                var nextY = next / width;
                var distance = (nextX != idxX && nextY != idxY ? 1.4142135623730951 : 1.0) * cellSize;

                var areaM2 = (weightedAccumulation?[idx] ?? accumulation[idx]) * cellAreaM2;
                var k = erodibilityPerCell?[idx] ?? p.K;
                var coeff = dt * k * Math.Pow(areaM2, p.M) / distance;

                var upliftTerm = dt * upliftMetersPerYear[idx];
                var upliftOnlyHeight = grid.Values[idx] + upliftTerm;
                var newHeight = (upliftOnlyHeight + coeff * grid.Values[next]) / (1.0 + coeff);
                // Check the FLOAT this actually gets stored as, not the double — a huge-but-finite
                // double (e.g. 3.6e38) silently overflows to float.PositiveInfinity on narrowing,
                // which double.IsFinite alone would miss for at least one extra iteration.
                var newHeightF = (float)newHeight;

                if (!float.IsFinite(newHeightF))
                {
                    var message = "SPIM diverged to a non-finite height — " +
                        $"iter={iter}/{p.Iterations} cell=({idxX},{idxY}) receiver=({nextX},{nextY}) " +
                        $"h_before={grid.Values[idx]:G9} h_receiver={grid.Values[next]:G9} " +
                        $"uplift_m_per_yr={upliftMetersPerYear[idx]:G9} k={k:G9} accumulation_cells={accumulation[idx]} " +
                        $"weighted_accumulation={(weightedAccumulation?[idx].ToString("G9") ?? "n/a")} areaM2={areaM2:G9} " +
                        $"coeff={coeff:G9} uplift_only_height={upliftOnlyHeight:G9} new_height={newHeight:G9}";
                    onDiagnostic?.Invoke(message);
                    throw new InvalidOperationException(message);
                }

                if (erodedHeightAccumulator is not null)
                    erodedHeightAccumulator[idx] += Math.Max(0.0, upliftOnlyHeight - newHeight);
                grid.Values[idx] = newHeightF;
            }

            if (erodedHeightAccumulator is null) continue;
            iterationsSinceRebound++;
            if (iterationsSinceRebound >= Math.Max(1, isostasyParams!.RecomputeIntervalIterations))
            {
                ApplyIsostaticRebound(erodedHeightAccumulator, grid, locked, crustDensityPerCell, isostasyParams, iter, p.Iterations, onDiagnostic);
                iterationsSinceRebound = 0;
            }
        }

        if (erodedHeightAccumulator is not null && iterationsSinceRebound > 0)
            ApplyIsostaticRebound(erodedHeightAccumulator, grid, locked, crustDensityPerCell, isostasyParams!, p.Iterations - 1, p.Iterations, onDiagnostic);
    }

    /// <summary>Converts each cell's accumulated eroded-height loss into an isostatic rebound height (<see cref="Isostasy.ErosionalReboundHeight"/>, always &lt; the eroded amount itself) and adds it DIRECTLY to <paramref name="grid"/>'s current elevation — a one-time state correction proportional to that interval's own erosion, not a rate that persists or compounds into later iterations — then resets the accumulator to 0.</summary>
    private static void ApplyIsostaticRebound(double[] erodedHeightAccumulator, TerrainHeightmap grid,
        bool[]? locked, double[]? crustDensityPerCell, Isostasy.Parameters isostasyParams, int iter, int totalIterations, Action<string>? onDiagnostic)
    {
        for (var idx = 0; idx < erodedHeightAccumulator.Length; idx++)
        {
            if (locked is not null && locked[idx]) { erodedHeightAccumulator[idx] = 0.0; continue; }

            var crustDensity = crustDensityPerCell?[idx] ?? isostasyParams.DefaultCrustDensityKgM3;
            var rebound = Isostasy.ErosionalReboundHeight(erodedHeightAccumulator[idx], crustDensity, isostasyParams.MantleDensityKgM3);
            var newValue = grid.Values[idx] + rebound;
            var newValueF = (float)newValue; // see Erode's own float-cast-overflow comment

            if (!float.IsFinite(newValueF))
            {
                var message = "Isostatic rebound diverged to a non-finite height — " +
                    $"iter={iter}/{totalIterations} cellIndex={idx} h_before={grid.Values[idx]:G9} " +
                    $"eroded_height_accumulated={erodedHeightAccumulator[idx]:G9} crust_density={crustDensity:G9} " +
                    $"mantle_density={isostasyParams.MantleDensityKgM3:G9} rebound={rebound:G9}";
                onDiagnostic?.Invoke(message);
                throw new InvalidOperationException(message);
            }

            grid.Values[idx] = newValueF;
            erodedHeightAccumulator[idx] = 0.0;
        }
    }

    /// <summary>Task 1.2.3's kinematic-proxy uplift field: convergent plate boundaries get positive uplift scaled by boundary influence and relative-velocity magnitude, divergent get rifting subsidence at half that magnitude, clamped throughout to the empirical [<see cref="MinUpliftMmPerYear"/>, <see cref="MaxUpliftMmPerYear"/>] range. ⚠ Design simplification, not a mechanical orogeny model (Willett, Beaumont &amp; Fullsack 1993 describes the real mechanics).</summary>
    public static double[] UpliftFieldFromPlates(TerrainHeightmap grid, TectonicPlates.Plate[]? plates,
        double refLatDeg, double refLonDeg, double planetRadiusMeters)
    {
        var width = grid.Width;
        var height = grid.Height;
        var uplift = new double[width * height];
        if (plates is not { Length: > 0 }) return uplift; // no plates configured -> zero uplift everywhere, a legitimate degenerate case

        const double mmToM = 1.0 / 1000.0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var worldX = grid.OriginX + x * grid.CellSizeMeters;
                var worldY = grid.OriginY + y * grid.CellSizeMeters;
                var (lat, lon) = PlanetNoise.OffsetToLatLon(worldX, worldY, refLatDeg, refLonDeg, planetRadiusMeters);
                var (px, py, pz) = PlanetNoise.LatLonToUnitVector(lat, lon);
                var boundary = TectonicPlates.Sample(plates, px, py, pz);
                var belt = Math.Pow(boundary.BoundaryInfluence, 3.0); // same cubing PlanetNoise.SampleCombined uses so uplift reads as a band near the boundary line

                var upliftMmPerYear = boundary.Boundary switch
                {
                    TectonicPlates.BoundaryType.Convergent =>
                        belt * Lerp(MinUpliftMmPerYear, MaxUpliftMmPerYear, Math.Clamp(boundary.ApproachRate / ApproachRateAtMaxUplift, 0.0, 1.0)),
                    TectonicPlates.BoundaryType.Divergent =>
                        -0.5 * belt * Lerp(MinUpliftMmPerYear, MaxUpliftMmPerYear, Math.Clamp(-boundary.ApproachRate / ApproachRateAtMaxUplift, 0.0, 1.0)),
                    _ => 0.0,
                };

                uplift[y * width + x] = upliftMmPerYear * mmToM;
            }
        }

        return uplift;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
