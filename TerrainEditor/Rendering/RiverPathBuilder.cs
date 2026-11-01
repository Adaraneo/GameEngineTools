using GameEngineTools.World.Data;
using SkiaSharp;

namespace TerrainEditor.Rendering;

/// <summary>Builds smoothed vector paths from the persisted river graph, one per Strahler order, in the canvas space the heightmap uses (1 unit = 1 grid cell). Rivers drawn from these keep their exact world position at any zoom or LOD stride instead of being stair-stepped onto the (possibly decimated) cell grid.</summary>
public static class RiverPathBuilder
{
    /// <summary>Lowest Strahler order still drawn at this zoom: every halving of the zoom below 0.5 hides one more order, so a zoomed-out view shows trunks instead of a dense mesh.</summary>
    public static int MinVisibleOrder(double zoom)
        => zoom >= 0.5 ? 2 : Math.Min(2 + (int)Math.Ceiling(Math.Log2(0.5 / Math.Max(zoom, 0.01))), 8);

    /// <summary>One path per order (key) from reaches whose polyline bounding box touches the grid; consecutive vertices are joined with midpoint quadratic curves so bends read as curves, not polylines. The caller owns (and must dispose) the returned paths.</summary>
    public static Dictionary<int, SKPath> BuildByOrder(IReadOnlyList<RiverReach> reaches, double originX, double originY, double cellSizeMeters, int widthCells, int heightCells)
    {
        var paths = new Dictionary<int, SKPath>();
        foreach (var reach in reaches)
        {
            var pts = reach.Polyline;
            if (pts.Count < 2 || !Overlaps(pts, originX, originY, cellSizeMeters, widthCells, heightCells)) continue;

            if (!paths.TryGetValue(reach.StrahlerOrder, out var path))
                paths[reach.StrahlerOrder] = path = new SKPath();
            AddSmoothed(path, pts, originX, originY, cellSizeMeters);
        }

        return paths;
    }

    /// <summary>Same, for the still-water oxbow loops (one style, so a single path).</summary>
    public static SKPath BuildOxbows(IReadOnlyList<OxbowLoop> oxbows, double originX, double originY, double cellSizeMeters, int widthCells, int heightCells)
    {
        var path = new SKPath();
        foreach (var oxbow in oxbows)
        {
            if (oxbow.Polyline.Count >= 2 && Overlaps(oxbow.Polyline, originX, originY, cellSizeMeters, widthCells, heightCells))
                AddSmoothed(path, oxbow.Polyline, originX, originY, cellSizeMeters);
        }

        return path;
    }

    private static void AddSmoothed(SKPath path, IReadOnlyList<(double X, double Y)> pts, double originX, double originY, double cell)
    {
        SKPoint P(int i) => new((float)((pts[i].X - originX) / cell), (float)((pts[i].Y - originY) / cell));

        path.MoveTo(P(0));
        if (pts.Count == 2)
        {
            path.LineTo(P(1));
            return;
        }

        for (var i = 1; i < pts.Count - 1; i++)
        {
            var c = P(i);
            var n = P(i + 1);
            path.QuadTo(c, new SKPoint((c.X + n.X) / 2f, (c.Y + n.Y) / 2f));
        }

        path.LineTo(P(pts.Count - 1));
    }

    private static bool Overlaps(IReadOnlyList<(double X, double Y)> pts, double originX, double originY, double cell, int w, int h)
    {
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var (x, y) in pts)
        {
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        return maxX >= originX && minX <= originX + w * cell && maxY >= originY && minY <= originY + h * cell;
    }
}
