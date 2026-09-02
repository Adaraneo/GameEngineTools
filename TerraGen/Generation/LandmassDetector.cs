namespace TerraGen.Generation;

/// <summary>
/// Groups a <see cref="PlanetScanner.Result"/>'s land cells into contiguous landmasses via flood
/// fill, ranked by approximate real area — so a <c>--scan</c> run tells you not just "here's the
/// shape" but "here are N distinct landmasses, ranked, each with a ready-to-paste
/// <c>--lat-range</c>/<c>--lon-range</c> for whichever one you actually want to generate for
/// real."
/// </summary>
public static class LandmassDetector
{
    public sealed record Landmass(
        /// <summary>1-based, largest area first.</summary>
        int Rank,
        int CellCount,
        double AreaKm2,
        double LatMin, double LatMax, double LonMin, double LonMax,
        double CentroidLatDeg, double CentroidLonDeg);

    /// <param name="LandmassRankByCell">Same shape as <see cref="PlanetScanner.Result.Cells"/> —
    /// 0 for ocean, else the owning <see cref="Landmass.Rank"/>.</param>
    public sealed record Detection(IReadOnlyList<Landmass> Landmasses, int[,] LandmassRankByCell);

    /// <summary>Longitudes here are UNWRAPPED — for a component that crosses the antimeridian
    /// seam, values naturally continue past ±180 instead of jumping, so min/max/centroid stay
    /// meaningful. <see cref="Detect"/> shifts the whole component back into a normal ±180 range
    /// once flood fill is done (see its bottom).</summary>
    private sealed record RawComponent(
        List<(int Row, int Col)> Cells, double AreaKm2,
        double LatMin, double LatMax, double LonMinUnwrapped, double LonMaxUnwrapped,
        double SumLat, double SumLonUnwrapped);

    private static readonly (int Dr, int Dc)[] Neighbors = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    public static Detection Detect(PlanetScanner.Result scan, double planetRadiusMeters)
    {
        var options = scan.Options;
        var elevations = scan.ElevationsMeters;
        var height = elevations.GetLength(0);
        var width = elevations.GetLength(1);
        var visited = new bool[height, width];
        var rankByCell = new int[height, width];

        // A full-globe longitude window wraps at the antimeridian — a landmass straddling it
        // (e.g. spanning lon 175 to -175) must flood-fill across that seam as one landmass, not
        // split in two. Only enabled for a genuinely full 360° window; a partial window has
        // nothing to wrap into, matching how a real generation run against that same window
        // would treat it (TerraGen's own --lat-range/--lon-range have no wrap concept either).
        var wrapLongitude = options.LonMax - options.LonMin >= 359.999;

        var raw = new List<RawComponent>();

        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                if (visited[row, col] || elevations[row, col] < 0.0) continue;
                raw.Add(FloodFillComponent(options, elevations, visited, row, col, width, height, wrapLongitude, planetRadiusMeters));
            }
        }

        var ranked = raw.OrderByDescending(c => c.AreaKm2).ToList();
        var landmasses = new List<Landmass>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            var rank = i + 1;
            var c = ranked[i];

            // Only renormalize into a canonical ±180° range when THIS scan was itself a full
            // 360° window — there, ±180 is the one ambiguity-free frame to report in. A partial
            // window (e.g. one already pasted in from an earlier landmass's own --lon-range,
            // itself potentially outside ±180) defines its OWN coordinate frame; shifting a
            // sub-range back into ±180 there would silently report bounds that don't even
            // overlap the window the caller asked to scan. Shift the whole component as a PAIR,
            // not each endpoint independently, or a seam-crossing range like [170, 200] would
            // normalize into the nonsensical [170, -160].
            var shift = !wrapLongitude ? 0.0
                : c.LonMaxUnwrapped > 180.0 ? -360.0
                : c.LonMinUnwrapped < -180.0 ? 360.0 : 0.0;
            var lonMin = c.LonMinUnwrapped + shift;
            var lonMax = c.LonMaxUnwrapped + shift;
            var centroidLon = c.SumLonUnwrapped / c.Cells.Count + shift;

            landmasses.Add(new Landmass(rank, c.Cells.Count, c.AreaKm2, c.LatMin, c.LatMax, lonMin, lonMax,
                c.SumLat / c.Cells.Count, centroidLon));
            foreach (var (row, col) in c.Cells)
                rankByCell[row, col] = rank;
        }

        return new Detection(landmasses, rankByCell);
    }

    private static RawComponent FloodFillComponent(
        PlanetScanner.Options options, double[,] elevations, bool[,] visited, int startRow, int startCol,
        int width, int height, bool wrapLongitude, double planetRadiusMeters)
    {
        var latStepDeg = height <= 1 ? 0.0 : (options.LatMax - options.LatMin) / (height - 1);
        var lonStepDeg = width <= 1 ? 0.0 : (options.LonMax - options.LonMin) / (width - 1);

        (double Lat, double Lon) CellLatLon(int row, int unwrappedCol)
        {
            var t = height <= 1 ? 0.5 : row / (double)(height - 1);
            var u = width <= 1 ? 0.5 : unwrappedCol / (double)(width - 1);
            var lat = options.LatMax - t * (options.LatMax - options.LatMin);
            var lon = options.LonMin + u * (options.LonMax - options.LonMin);
            return (lat, lon);
        }

        // Queue/visited use the real (wrapped) column so the grid arrays stay correctly indexed;
        // the unwrapped column tags along purely to keep lon/min/max/centroid meaningful across
        // the seam — see the type's own remarks.
        var queue = new Queue<(int Row, int Col, int UnwrappedCol)>();
        var cells = new List<(int Row, int Col)>();
        queue.Enqueue((startRow, startCol, startCol));
        visited[startRow, startCol] = true;

        double areaKm2 = 0, sumLat = 0, sumLonUnwrapped = 0;
        var latMin = double.MaxValue; var latMax = double.MinValue;
        var lonMinUnwrapped = double.MaxValue; var lonMaxUnwrapped = double.MinValue;

        while (queue.Count > 0)
        {
            var (row, col, unwrappedCol) = queue.Dequeue();
            cells.Add((row, col));

            var (lat, lon) = CellLatLon(row, unwrappedCol);
            areaKm2 += CellAreaKm2(lat, latStepDeg, lonStepDeg, planetRadiusMeters);
            latMin = Math.Min(latMin, lat); latMax = Math.Max(latMax, lat);
            lonMinUnwrapped = Math.Min(lonMinUnwrapped, lon); lonMaxUnwrapped = Math.Max(lonMaxUnwrapped, lon);
            sumLat += lat; sumLonUnwrapped += lon;

            foreach (var (dr, dc) in Neighbors)
            {
                var nr = row + dr;
                if (nr < 0 || nr >= height) continue;

                var ncWrapped = col + dc;
                var nUnwrapped = unwrappedCol + dc; // keeps counting past the edge regardless of wrap
                if (ncWrapped < 0)
                {
                    if (!wrapLongitude) continue;
                    ncWrapped = width - 1;
                }
                else if (ncWrapped >= width)
                {
                    if (!wrapLongitude) continue;
                    ncWrapped = 0;
                }

                if (visited[nr, ncWrapped] || elevations[nr, ncWrapped] < 0.0) continue;
                visited[nr, ncWrapped] = true;
                queue.Enqueue((nr, ncWrapped, nUnwrapped));
            }
        }

        return new RawComponent(cells, areaKm2, latMin, latMax, lonMinUnwrapped, lonMaxUnwrapped, sumLat, sumLonUnwrapped);
    }

    /// <summary>Approximate real-world area of one equirectangular grid cell centered at
    /// <paramref name="latDeg"/> — narrower toward the poles (the cos(lat) term), the same
    /// correction any equirectangular-projection area estimate needs.</summary>
    private static double CellAreaKm2(double latDeg, double latStepDeg, double lonStepDeg, double planetRadiusMeters)
    {
        var latStepRad = Math.Abs(latStepDeg) * Math.PI / 180.0;
        var lonStepRad = Math.Abs(lonStepDeg) * Math.PI / 180.0;
        var latRad = latDeg * Math.PI / 180.0;
        var areaMeters2 = planetRadiusMeters * planetRadiusMeters * latStepRad * lonStepRad * Math.Cos(latRad);
        return areaMeters2 / 1_000_000.0;
    }
}
