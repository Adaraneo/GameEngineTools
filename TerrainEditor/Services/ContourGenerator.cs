using System.Threading.Tasks;
using GameEngineTools.World.Data;

namespace TerrainEditor.Services;

/// <summary>A single contour-line segment (world-space meters) at a given elevation level.</summary>
public readonly record struct ContourSegment(double X1, double Y1, double X2, double Y2, float Level);

/// <summary>
/// Derives contour lines from a <see cref="TerrainHeightmap"/> via the marching squares
/// algorithm. Cheap enough to re-run on every brush stroke's mouse-up (not every mouse-move).
/// </summary>
public sealed class ContourGenerator
{
    /// <summary>
    /// Generates <paramref name="levelCount"/> evenly spaced contour levels between the grid's
    /// min and max elevation, and the line segments crossing each level. Sea level (0m) is
    /// always included as an extra level — regardless of spacing — so the coastline (where
    /// land meets water) is never missed; the caller can pick it out via
    /// <see cref="ContourSegment.Level"/> == 0 to render it as a distinct coastline.
    /// </summary>
    /// <param name="stride">Samples every <paramref name="stride"/>-th grid cell instead of
    /// every cell — the marching-squares cost is O(Width*Height), so on a large stitched combined
    /// grid at low zoom this cuts it by <c>stride²</c> at the cost of coarser contour geometry the
    /// user can't actually resolve on-screen at that zoom anyway. Pass the same factor used for
    /// the heightmap bitmap's own LOD downsampling so both degrade together.</param>
    public IReadOnlyList<ContourSegment> Generate(TerrainHeightmap grid, int levelCount = 10, int stride = 1)
    {
        if (grid.Width < 2 || grid.Height < 2)
            return [];

        var min = grid.Values.Min();
        var max = grid.Values.Max();
        if (max - min < 1e-6f)
            return []; // flat terrain — no meaningful contours

        var levels = new float[levelCount + 1];
        for (var i = 0; i < levelCount; i++)
            levels[i] = min + (max - min) * (i + 1) / (levelCount + 1);
        levels[levelCount] = 0f; // coastline — harmless no-op if the grid never crosses 0

        stride = Math.Max(1, stride);
        // Each row of cells is independent (marching squares only reads that row's own two
        // corners-rows) — a stitched combined grid at full zoom can be several million cells, and
        // this loop was the single biggest contributor to RenderOverlay blocking the UI thread for
        // 1-2 seconds on every tile-boundary crossing. Parallel.For with a thread-local segment
        // list (merged once at the end) cuts that by roughly the core count, same technique as
        // RenderGrid's own Parallel.For over pixel rows.
        var rowCount = (grid.Height - 1 + stride - 1) / stride;
        var rowResults = new List<ContourSegment>?[rowCount];
        Parallel.For(0, rowCount, rowIndex =>
        {
            var gy = rowIndex * stride;
            var gy2 = Math.Min(gy + stride, grid.Height - 1);
            var rowSegments = new List<ContourSegment>();

            for (var gx = 0; gx < grid.Width - 1; gx += stride)
            {
                var gx2 = Math.Min(gx + stride, grid.Width - 1);
                var v00 = grid.Values[gy * grid.Width + gx];
                var v10 = grid.Values[gy * grid.Width + gx2];
                var v01 = grid.Values[gy2 * grid.Width + gx];
                var v11 = grid.Values[gy2 * grid.Width + gx2];

                var x0 = grid.OriginX + gx * grid.CellSizeMeters;
                var y0 = grid.OriginY + gy * grid.CellSizeMeters;
                var x1 = grid.OriginX + gx2 * grid.CellSizeMeters;
                var y1 = grid.OriginY + gy2 * grid.CellSizeMeters;

                foreach (var level in levels)
                    AddCellSegments(rowSegments, level, x0, y0, x1, y1, v00, v10, v01, v11);
            }

            rowResults[rowIndex] = rowSegments.Count > 0 ? rowSegments : null;
        });

        var segments = new List<ContourSegment>();
        foreach (var row in rowResults)
            if (row is not null)
                segments.AddRange(row);
        return segments;
    }

    /// <summary>
    /// Standard 16-case marching-squares lookup for one grid cell. Corners: NW=v00, NE=v10,
    /// SE=v11, SW=v01 (bits 1,2,4,8 respectively — "above level" contributes the bit).
    /// The two saddle cases (5, 10) are genuinely ambiguous with 4-corner sampling alone;
    /// resolved here by drawing both diagonal segments, which is visually acceptable for a
    /// terrain-authoring aid (not a scientific contouring tool).
    /// </summary>
    private static void AddCellSegments(
        List<ContourSegment> segments, float level,
        double x0, double y0, double x1, double y1,
        float v00, float v10, float v01, float v11)
    {
        var idx = 0;
        if (v00 > level) idx |= 1;
        if (v10 > level) idx |= 2;
        if (v11 > level) idx |= 4;
        if (v01 > level) idx |= 8;
        if (idx == 0 || idx == 15)
            return;

        (double x, double y) Top() => (Lerp(x0, x1, v00, v10, level), y0);
        (double x, double y) Right() => (x1, Lerp(y0, y1, v10, v11, level));
        (double x, double y) Bottom() => (Lerp(x0, x1, v01, v11, level), y1);
        (double x, double y) Left() => (x0, Lerp(y0, y1, v00, v01, level));

        void Seg((double x, double y) a, (double x, double y) b)
            => segments.Add(new ContourSegment(a.x, a.y, b.x, b.y, level));

        switch (idx)
        {
            case 1: case 14: Seg(Left(), Top()); break;
            case 2: case 13: Seg(Top(), Right()); break;
            case 3: case 12: Seg(Left(), Right()); break;
            case 4: case 11: Seg(Right(), Bottom()); break;
            case 6: case 9: Seg(Top(), Bottom()); break;
            case 7: case 8: Seg(Left(), Bottom()); break;
            case 5:
                Seg(Left(), Top());
                Seg(Right(), Bottom());
                break;
            case 10:
                Seg(Top(), Right());
                Seg(Left(), Bottom());
                break;
        }
    }

    private static double Lerp(double a, double b, float va, float vb, float level)
    {
        if (Math.Abs(vb - va) < 1e-9f)
            return (a + b) / 2;
        var t = (level - va) / (vb - va);
        return a + (b - a) * t;
    }
}
