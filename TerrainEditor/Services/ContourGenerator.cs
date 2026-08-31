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
    public IReadOnlyList<ContourSegment> Generate(TerrainHeightmap grid, int levelCount = 10)
    {
        var segments = new List<ContourSegment>();
        if (grid.Width < 2 || grid.Height < 2)
            return segments;

        var min = grid.Values.Min();
        var max = grid.Values.Max();
        if (max - min < 1e-6f)
            return segments; // flat terrain — no meaningful contours

        var levels = new float[levelCount + 1];
        for (var i = 0; i < levelCount; i++)
            levels[i] = min + (max - min) * (i + 1) / (levelCount + 1);
        levels[levelCount] = 0f; // coastline — harmless no-op if the grid never crosses 0

        for (var gy = 0; gy < grid.Height - 1; gy++)
        {
            for (var gx = 0; gx < grid.Width - 1; gx++)
            {
                var v00 = grid.Values[gy * grid.Width + gx];
                var v10 = grid.Values[gy * grid.Width + gx + 1];
                var v01 = grid.Values[(gy + 1) * grid.Width + gx];
                var v11 = grid.Values[(gy + 1) * grid.Width + gx + 1];

                var x0 = grid.OriginX + gx * grid.CellSizeMeters;
                var y0 = grid.OriginY + gy * grid.CellSizeMeters;
                var x1 = x0 + grid.CellSizeMeters;
                var y1 = y0 + grid.CellSizeMeters;

                foreach (var level in levels)
                    AddCellSegments(segments, level, x0, y0, x1, y1, v00, v10, v01, v11);
            }
        }

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
