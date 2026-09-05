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

    /// <summary>Erodes <paramref name="grid"/> in place by running <see cref="Parameters.Iterations"/> implicit SPIM timesteps. <paramref name="locked"/> (same convention as <see cref="TileErosion.Erode"/>) marks cells read but never written.</summary>
    public static void Erode(TerrainHeightmap grid, Parameters p, double[] upliftMetersPerYear, bool[]? locked = null)
    {
        if (p.Iterations <= 0 || grid.Width < 2 || grid.Height < 2) return;

        var width = grid.Width;
        var cellSize = grid.CellSizeMeters;
        var cellAreaM2 = cellSize * cellSize;
        var dt = p.TimestepYears;

        for (var iter = 0; iter < p.Iterations; iter++)
        {
            var routing = FlowRouting.Compute(grid);
            var downstream = routing.Downstream;
            var stack = routing.Stack; // highest-to-lowest routed elevation
            var accumulation = routing.Accumulation;

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

                var areaM2 = accumulation[idx] * cellAreaM2;
                var coeff = dt * p.K * Math.Pow(areaM2, p.M) / distance;

                var upliftTerm = dt * upliftMetersPerYear[idx];
                var newHeight = (grid.Values[idx] + upliftTerm + coeff * grid.Values[next]) / (1.0 + coeff);
                grid.Values[idx] = (float)newHeight;
            }
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
