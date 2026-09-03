using GameEngineTools.World.Data;

namespace TerraGen.Generation;

/// <summary>
/// Displaces <see cref="TileHydrology"/>'s D8 river backbone sideways into a meandering path, using
/// the Langbein &amp; Leopold (1966) sine-generated-curve model: the channel's direction angle
/// varies sinusoidally along arc length, with wavelength ≈ 11× channel width (Leopold 1994) and
/// amplitude suppressed on steep terrain. Real meanders form via lateral bank erosion — the outer
/// bank of a bend erodes, the inner bank deposits a point bar, and the channel migrates sideways —
/// a process that only dominates when the channel doesn't have enough stream power to just plow
/// straight downhill (Leopold &amp; Wolman 1957's slope-discharge channel-pattern thresholds), which
/// is why mountain streams run straight and lowland rivers wander; sinuosity (channel length ÷
/// valley length) is itself defined as the ratio of valley slope to channel slope. D8 has no notion
/// of any of this — every cell picks its single steepest of 8 neighbors with no lateral or inertial
/// component, so a smooth, low-relief terrain naturally produces long dead-straight runs (confirmed
/// live on production terrain: 311 consecutive cells, 1.5km, in one exact direction) that don't
/// read as real rivers even though the underlying flow topology is entirely correct.
/// </summary>
public static class RiverMeander
{
    public sealed record Parameters(
        /// <summary>Real channel width has no simulated discharge to derive it from here, so this
        /// approximates it from contributing area via a simplified power law (width grows with the
        /// square root of catchment area — the same order-of-magnitude scaling real width-discharge
        /// -area relationships share, without claiming their exact regional-calibration constants).
        /// Purely a shape-driving parameter, not a claim about real channel width in meters.</summary>
        double WidthPerSqrtAreaM2 = 0.02,
        /// <summary>Meander wavelength ≈ 11× channel width (Leopold 1994, citing the Leopold-Wolman
        /// / Langbein-Leopold empirical surveys) — bigger rivers meander in bigger loops.</summary>
        double WavelengthPerWidth = 11.0,
        /// <summary>Meander amplitude relative to channel width, measured from the straight
        /// centerline — real meander BELT width runs roughly 6 channel widths across (Williams
        /// 1986: belt ≈ 3.7·W^1.12), so amplitude (half the belt) is set to about half that.</summary>
        double AmplitudePerWidth = 3.0,
        /// <summary>Local slope (dimensionless rise/run) below which meandering runs at full
        /// strength — a real lowland/floodplain-scale gradient.</summary>
        double SlopeFullMeanderBelow = 0.01,
        /// <summary>Local slope above which meandering is fully suppressed and the channel follows
        /// its straight D8 path — deliberately conservative (well past a typical lowland grade)
        /// rather than tuned to Leopold &amp; Wolman's own discharge-specific threshold line, which
        /// needs real discharge this generator doesn't simulate.</summary>
        double SlopeSuppressedAbove = 0.08);

    /// <summary>Takes the straight D8 mask <see cref="TileHydrology.ComputeDiagnostics"/> already
    /// computed (plus its accumulation/slope/downstream/Strahler-order arrays, which this reuses
    /// rather than recomputing) and returns a new mask where every marked cell has been laterally
    /// displaced along a sine-generated curve. Cell count and shape match the input — this only
    /// ever redistributes WHERE within the same grid the channel is drawn, it doesn't add or remove
    /// catchment area or change the underlying accumulation/routing at all. The returned byte value
    /// at a river cell is its Strahler order (see <see cref="GameEngineTools.World.Data.TerrainHeightmap.RiverMask"/>'s
    /// remarks), not a flat 1 — meandering only changes shape, never what a cell's own order was on
    /// the straight backbone.</summary>
    public static byte[] ApplyMeander(TerrainHeightmap grid, byte[] straightMask, int[] accumulation,
        double[] slope, int[] downstream, int[] order, byte[] strahlerOrder, Parameters p)
    {
        var (offsetX, offsetY) = ComputeOffsets(grid, straightMask, accumulation, slope, downstream, order, p);
        return Rasterize(grid, straightMask, downstream, strahlerOrder, offsetX, offsetY);
    }

    /// <summary>Same computation as <see cref="ApplyMeander"/>, but stops short of rasterizing —
    /// returns each backbone cell's own displaced (x,y) instead, in the SAME arc-length/amplitude
    /// units the rasterized mask is built from. Not needed by any production caller (which only
    /// wants the final mask), but lets a test measure the meander's actual path length/sinuosity
    /// directly from where cells moved, instead of only from how many raster pixels ended up lit —
    /// Bresenham's own pixel count depends on rasterization angle in a way true arc length
    /// doesn't.</summary>
    internal static (int[] OffsetX, int[] OffsetY) ComputeOffsets(TerrainHeightmap grid, byte[] straightMask,
        int[] accumulation, double[] slope, int[] downstream, int[] order, Parameters p)
    {
        var width = grid.Width;
        var height = grid.Height;
        var count = width * height;
        var cellSize = grid.CellSizeMeters;

        // Arc length along the DOMINANT upstream path reaching each cell — propagated in the same
        // high-to-low elevation order TileHydrology's own accumulation pass uses, so every cell's
        // largest contributor has already resolved its own arc length before passing it onward.
        // This is what gives the sine curve a stable, connected phase along the whole channel
        // instead of restarting arbitrarily at every confluence.
        //
        // Two more quantities propagate the same way, both to fix a real self-intersection defect
        // (confirmed live: 44% of measured reaches crossed themselves, one 24 times over ~3km) that
        // a sine-generated curve should NOT have — Leopold's own belt-width ratios only stay
        // non-self-crossing when channel width (hence wavelength/amplitude) stays effectively
        // constant across one full meander cycle. Recomputing width from each cell's own raw,
        // locally noisy accumulation broke that assumption every time accumulation jittered even
        // slightly cycle-to-cycle. Exponentially smoothing both along arc length, with a decay
        // length of one wavelength, fixes it the same way a real channel's own width doesn't
        // fluctuate cell-to-cell either:
        //   - smoothedWidth: stabilizes wavelength/amplitude so successive cycles stay consistent
        //     instead of drifting into each other.
        //   - smoothedDir: the D8 backbone itself is jittery at cell resolution (that's the whole
        //     reason it needed meandering in the first place) — sine-overlaying a wobble on top of
        //     an already-wobbly base direction compounds the two curvatures and can loop back on
        //     itself even with a perfectly steady width, so the perpendicular axis needs to follow
        //     the backbone's smoothed TREND direction, not its raw single-cell direction.
        var arcLength = new double[count];
        // Accumulated PHASE of the sine wave (radians), not raw arc length — kept as its own
        // running total rather than recomputed as arcLength/wavelength at each cell. Wavelength
        // grows downstream as accumulated area grows, and sin(2π·s/λ(s)) — evaluating the WHOLE
        // arc length so far against only the CURRENT, already-much-larger wavelength — silently
        // assumes the entire path so far occurred at today's wavelength. It didn't: confirmed live,
        // this was the actual cause of the self-crossing loops earlier tuning couldn't fix by
        // adjusting amplitude or smoothing alone, because the bug wasn't in either of those — a
        // proper varying-wavelength wave needs its phase integrated incrementally, exactly like
        // arcLength itself already is.
        var phase = new double[count];
        var bestUpstreamAccum = new int[count];
        var smoothedWidth = new double[count];
        var smoothedDirX = new double[count];
        var smoothedDirY = new double[count];

        for (var i = 0; i < count; i++)
            smoothedWidth[i] = p.WidthPerSqrtAreaM2 * Math.Sqrt(accumulation[i] * cellSize * cellSize);

        foreach (var idx in order)
        {
            if (straightMask[idx] == 0) continue;
            var next = downstream[idx];
            if (next < 0) continue;

            var x = idx % width;
            var y = idx / width;
            var nx = next % width;
            var ny = next / width;
            var cellMag = nx != x && ny != y ? 1.4142135623730951 : 1.0;
            var stepDist = cellMag * cellSize;
            var candidateArc = arcLength[idx] + stepDist;

            // Unit direction vector (dimensionless), not the raw step — this is what gets smoothed.
            var stepDirX = (nx - x) / cellMag;
            var stepDirY = (ny - y) / cellMag;

            if (accumulation[idx] >= bestUpstreamAccum[next])
            {
                bestUpstreamAccum[next] = accumulation[idx];
                arcLength[next] = candidateArc;

                // Phase advances by this step's own length measured in units of idx's OWN (already
                // resolved) wavelength — an incremental integral, not a lump-sum recomputation.
                var wavelengthHere = Math.Max(cellSize, p.WavelengthPerWidth * smoothedWidth[idx]);
                phase[next] = phase[idx] + 2.0 * Math.PI * stepDist / wavelengthHere;

                // Exponential smoothing along the dominant path: blend this step's own raw value
                // toward the upstream neighbor's ALREADY-smoothed value, decaying over a distance
                // of one wavelength (estimated from the upstream neighbor's own smoothed width, so
                // it's self-consistent rather than circular).
                var smoothingLength = Math.Max(cellSize, p.WavelengthPerWidth * smoothedWidth[idx]);
                var w = 1.0 - Math.Exp(-stepDist / smoothingLength);
                var rawWidthHere = p.WidthPerSqrtAreaM2 * Math.Sqrt(accumulation[next] * cellSize * cellSize);
                smoothedWidth[next] = smoothedWidth[idx] + (rawWidthHere - smoothedWidth[idx]) * w;
                smoothedDirX[next] = smoothedDirX[idx] + (stepDirX - smoothedDirX[idx]) * w;
                smoothedDirY[next] = smoothedDirY[idx] + (stepDirY - smoothedDirY[idx]) * w;
            }
        }

        // Where each original backbone cell actually ends up after its own lateral offset.
        var offsetX = new int[count];
        var offsetY = new int[count];
        for (var i = 0; i < count; i++) { offsetX[i] = i % width; offsetY[i] = i / width; }

        for (var idx = 0; idx < count; idx++)
        {
            if (straightMask[idx] == 0) continue;
            var next = downstream[idx];
            if (next < 0) continue;

            var channelWidth = smoothedWidth[idx];
            var maxAmplitude = p.AmplitudePerWidth * channelWidth;

            var here = slope[idx];
            var suppression = here <= p.SlopeFullMeanderBelow ? 1.0
                : here >= p.SlopeSuppressedAbove ? 0.0
                : 1.0 - (here - p.SlopeFullMeanderBelow) / (p.SlopeSuppressedAbove - p.SlopeFullMeanderBelow);
            var amplitude = maxAmplitude * suppression;
            if (amplitude <= 0.0) continue;

            var x = idx % width;
            var y = idx / width;

            // The smoothed TREND direction, not the raw single-cell D8 step — see the remarks above
            // on why overlaying a wobble on an already-jittery base direction compounds curvature.
            // Falls back to the raw step for a cell whose smoothing never got a chance to run (e.g.
            // a head cell one step from its own start).
            var dirX = smoothedDirX[idx];
            var dirY = smoothedDirY[idx];
            var len = Math.Sqrt(dirX * dirX + dirY * dirY);
            if (len < 0.001)
            {
                var nx0 = next % width;
                var ny0 = next / width;
                dirX = nx0 - x;
                dirY = ny0 - y;
                len = Math.Sqrt(dirX * dirX + dirY * dirY);
                if (len < 0.001) continue;
            }

            // Perpendicular to the smoothed flow direction — this is the axis the sine wave
            // displaces the channel along, same as a real meander swings side-to-side across its
            // valley.
            var perpX = -dirY / len;
            var perpY = dirX / len;

            var offsetMeters = amplitude * Math.Sin(phase[idx]);
            var offsetCells = offsetMeters / cellSize;

            offsetX[idx] = Math.Clamp((int)Math.Round(x + perpX * offsetCells), 0, width - 1);
            offsetY[idx] = Math.Clamp((int)Math.Round(y + perpY * offsetCells), 0, height - 1);
        }

        return (offsetX, offsetY);
    }

    /// <summary>Reconnect: every original edge (cell -> its downstream cell) gets redrawn between
    /// the TWO cells' own offset positions, not just the offset cells marked in isolation — a
    /// meander swing can move a cell several grid cells sideways between one step and the next, and
    /// without redrawing the connecting line the channel would fragment into disconnected dots
    /// exactly like the bug TileHydrology's own downstream-propagation fix already solved for the
    /// straight case.</summary>
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

    /// <summary>Bresenham line rasterization, so two consecutive offset points always end up
    /// 8-connected on the grid no matter how far apart a meander swing put them. Stamps
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

    /// <summary>Two different reaches' meander swings can rasterize over the same pixel — keep
    /// whichever order is bigger rather than letting draw order arbitrarily decide, so a large
    /// river's line never gets accidentally overwritten by a small tributary passing near it.</summary>
    private static void StampMax(byte[] mask, int idx, byte value)
    {
        if (value > mask[idx]) mask[idx] = value;
    }
}
