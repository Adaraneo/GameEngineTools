namespace TerraGen.Generation;

/// <summary>
/// Runs <see cref="PlanetScanner"/> repeatedly, each pass zooming into the coastline point
/// nearest the previous window's center — walks from a wide overview down to a tight,
/// high-resolution coastline close-up automatically, without hand-copying
/// --lat-range/--lon-range between runs the way a single <c>--scan</c> requires.
/// </summary>
public static class ProgressiveScanner
{
    public sealed record Options(
        /// <summary>How many zoom passes to run — 1 behaves exactly like a plain scan.</summary>
        int Levels,
        /// <summary>Each level's window is this many times narrower (in degrees) than the
        /// previous one, centered on the coastline point that pass found.</summary>
        double ZoomFactor,
        PlanetScanner.Options InitialScanOptions);

    /// <param name="CoastlineTarget"><c>null</c> when no land/ocean boundary was found anywhere
    /// in this level's window (e.g. it landed entirely on open ocean or deep inland) — the next
    /// level then just zooms toward this window's own center instead.</param>
    public sealed record LevelResult(
        int Level, PlanetScanner.Options WindowUsed, PlanetScanner.Result Scan,
        LandmassDetector.Detection Landmasses, (double LatDeg, double LonDeg)? CoastlineTarget);

    private static readonly (int Dr, int Dc)[] Neighbors = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    public static IReadOnlyList<LevelResult> Run(
        PlanetNoise.Parameters noiseParams, double planetRadiusMeters, TectonicPlates.Plate[]? plates, Options options)
    {
        var results = new List<LevelResult>(Math.Max(1, options.Levels));
        var window = options.InitialScanOptions;

        for (var level = 0; level < Math.Max(1, options.Levels); level++)
        {
            var scan = PlanetScanner.Scan(noiseParams, planetRadiusMeters, plates, window);
            var landmasses = LandmassDetector.Detect(scan, planetRadiusMeters);

            var centerLat = (window.LatMin + window.LatMax) / 2.0;
            var centerLon = (window.LonMin + window.LonMax) / 2.0;
            var coastPoint = FindNearestCoastline(scan, centerLat, centerLon);

            results.Add(new LevelResult(level, window, scan, landmasses, coastPoint));

            var isLastLevel = level == options.Levels - 1;
            if (isLastLevel) break;

            var (targetLat, targetLon) = coastPoint ?? (centerLat, centerLon);
            var nextLatSpan = (window.LatMax - window.LatMin) / options.ZoomFactor;
            var nextLonSpan = (window.LonMax - window.LonMin) / options.ZoomFactor;

            window = window with
            {
                LatMin = targetLat - nextLatSpan / 2.0,
                LatMax = targetLat + nextLatSpan / 2.0,
                LonMin = targetLon - nextLonSpan / 2.0,
                LonMax = targetLon + nextLonSpan / 2.0,
            };
        }

        return results;
    }

    /// <summary>The land/ocean boundary cell (by raw elevation sign, independent of any
    /// plate-boundary marker overlay) whose center is closest to (centerLat, centerLon) —
    /// deterministic "zoom toward whichever coast is most central" behavior.</summary>
    private static (double LatDeg, double LonDeg)? FindNearestCoastline(PlanetScanner.Result scan, double centerLat, double centerLon)
    {
        var height = scan.ElevationsMeters.GetLength(0);
        var width = scan.ElevationsMeters.GetLength(1);
        (double Lat, double Lon)? best = null;
        var bestDistanceSquared = double.MaxValue;

        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                var isLand = scan.ElevationsMeters[row, col] >= 0.0;
                var isCoast = false;
                foreach (var (dr, dc) in Neighbors)
                {
                    var nr = row + dr;
                    var nc = col + dc;
                    if (nr < 0 || nr >= height || nc < 0 || nc >= width) continue;
                    if ((scan.ElevationsMeters[nr, nc] >= 0.0) != isLand) { isCoast = true; break; }
                }
                if (!isCoast) continue;

                var (lat, lon) = scan.CellCenter(row, col);
                var dLat = lat - centerLat;
                var dLon = lon - centerLon;
                var distanceSquared = dLat * dLat + dLon * dLon;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    best = (lat, lon);
                }
            }
        }

        return best;
    }
}
