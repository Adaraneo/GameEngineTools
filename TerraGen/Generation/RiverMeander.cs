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
    /// computed (plus its accumulation/slope/downstream arrays, which this reuses rather than
    /// recomputing) and returns a new mask where every marked cell has been laterally displaced
    /// along a sine-generated curve. Cell count and shape match the input — this only ever
    /// redistributes WHERE within the same grid the channel is drawn, it doesn't add or remove
    /// catchment area or change the underlying accumulation/routing at all.</summary>
    public static byte[] ApplyMeander(TerrainHeightmap grid, byte[] straightMask, int[] accumulation,
        double[] slope, int[] downstream, int[] order, Parameters p)
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
        var arcLength = new double[count];
        var bestUpstreamAccum = new int[count];

        foreach (var idx in order)
        {
            if (straightMask[idx] == 0) continue;
            var next = downstream[idx];
            if (next < 0) continue;

            var x = idx % width;
            var y = idx / width;
            var nx = next % width;
            var ny = next / width;
            var stepDist = (nx != x && ny != y ? 1.4142135623730951 : 1.0) * cellSize;
            var candidateArc = arcLength[idx] + stepDist;

            if (accumulation[idx] >= bestUpstreamAccum[next])
            {
                bestUpstreamAccum[next] = accumulation[idx];
                arcLength[next] = candidateArc;
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

            var areaM2 = accumulation[idx] * cellSize * cellSize;
            var channelWidth = p.WidthPerSqrtAreaM2 * Math.Sqrt(areaM2);
            var wavelength = Math.Max(cellSize, p.WavelengthPerWidth * channelWidth);
            var maxAmplitude = p.AmplitudePerWidth * channelWidth;

            var here = slope[idx];
            var suppression = here <= p.SlopeFullMeanderBelow ? 1.0
                : here >= p.SlopeSuppressedAbove ? 0.0
                : 1.0 - (here - p.SlopeFullMeanderBelow) / (p.SlopeSuppressedAbove - p.SlopeFullMeanderBelow);
            var amplitude = maxAmplitude * suppression;
            if (amplitude <= 0.0) continue;

            var x = idx % width;
            var y = idx / width;
            var nx = next % width;
            var ny = next / width;
            var dirX = nx - x;
            var dirY = ny - y;
            var len = Math.Sqrt(dirX * dirX + dirY * dirY);
            if (len < 0.001) continue;

            // Perpendicular to the local flow direction — this is the axis the sine wave displaces
            // the channel along, same as a real meander swings side-to-side across its valley.
            var perpX = -dirY / len;
            var perpY = dirX / len;

            var offsetMeters = amplitude * Math.Sin(2.0 * Math.PI * arcLength[idx] / wavelength);
            var offsetCells = offsetMeters / cellSize;

            offsetX[idx] = Math.Clamp((int)Math.Round(x + perpX * offsetCells), 0, width - 1);
            offsetY[idx] = Math.Clamp((int)Math.Round(y + perpY * offsetCells), 0, height - 1);
        }

        // Reconnect: every original edge (cell -> its downstream cell) gets redrawn between the
        // TWO cells' own offset positions, not just the offset cells marked in isolation — a
        // meander swing can move a cell several grid cells sideways between one step and the next,
        // and without redrawing the connecting line the channel would fragment into disconnected
        // dots exactly like the bug TileHydrology's own downstream-propagation fix already solved
        // for the straight case.
        var meandered = new byte[count];
        for (var idx = 0; idx < count; idx++)
        {
            if (straightMask[idx] == 0) continue;
            var next = downstream[idx];
            if (next < 0)
            {
                meandered[offsetY[idx] * width + offsetX[idx]] = 1;
                continue;
            }
            DrawLine(meandered, width, height, offsetX[idx], offsetY[idx], offsetX[next], offsetY[next]);
        }

        return meandered;
    }

    /// <summary>Bresenham line rasterization, so two consecutive offset points always end up
    /// 8-connected on the grid no matter how far apart a meander swing put them.</summary>
    private static void DrawLine(byte[] mask, int width, int height, int x0, int y0, int x1, int y1)
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
                mask[y * width + x] = 1;
            if (x == x1 && y == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
    }
}
