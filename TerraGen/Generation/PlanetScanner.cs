namespace TerraGen.Generation;

/// <summary>
/// Fast whole-planet (or windowed) land/ocean/plate-boundary preview — deliberately independent
/// of <see cref="TileGenerator"/>: no tile grid, no hydraulic erosion, no database writes, just a
/// direct <see cref="PlanetNoise.SampleLandmass"/> call per lat/lon grid point (globally seamless
/// true-sphere noise, so this is accurate anywhere, unlike the mountain-ridge detail layer — see
/// <see cref="PlanetNoise"/>'s own remarks on why that layer is only valid near one shared flat
/// reference point). Plate boundaries come from <see cref="TectonicPlates.Sample"/>, likewise a
/// true-sphere lookup with no reference-point limitation, so the boundary overlay is accurate at
/// planet scale even though the actual uplifted mountain TEXTURE isn't shown — this answers "where
/// is land, and where would ranges/rifts form", not "what does the terrain look like up close".
/// <see cref="Options.Detail"/> trades that planet-scale accuracy for exactly the opposite: a
/// zoomed-in preview that DOES include the mountain-ridge layer, valid only for the requested
/// window itself (see the option's own remarks).
/// </summary>
public static class PlanetScanner
{
    public sealed record Options(
        int Width,
        int Height,
        double LatMin,
        double LatMax,
        double LonMin,
        double LonMax,
        /// <summary>Minimum <see cref="TectonicPlates.BoundarySample.BoundaryInfluence"/> for a
        /// cell to render as a boundary marker instead of plain land/ocean. 0.9 by default — the
        /// influence falloff is roughly linear with distance across a whole plate, so a lower
        /// threshold reads as a wide colored region rather than a thin boundary line at this
        /// map's resolution.</summary>
        double BoundaryInfluenceThreshold = 0.9,
        /// <summary>When true, also samples the mountain-ridge layer (<see cref="PlanetNoise.SampleCombined"/>)
        /// using this window's OWN CENTER as an ad-hoc flat reference point, instead of the
        /// pure globally-seamless landmass layer alone. Meaningful only for a genuinely local/
        /// zoomed window — at planet scale the mountain layer's flat-plane approximation breaks
        /// down (see <see cref="PlanetNoise"/>'s remarks), so this is opt-in, off by default.</summary>
        bool Detail = false);

    public enum Cell { Ocean, Land, Convergent, Divergent, Transform }

    public sealed record Result(Cell[,] Cells, double[,] ElevationsMeters, Options Options)
    {
        public char Symbol(int row, int col) => Cells[row, col] switch
        {
            Cell.Convergent => '^',
            Cell.Divergent => 'v',
            Cell.Transform => 'x',
            Cell.Land => '.',
            _ => '~',
        };

        /// <summary>The (lat,lon) a given cell's center represents — row 0 is the northern
        /// (LatMax) edge, column 0 the western (LonMin) edge, matching standard map orientation.</summary>
        public (double LatDeg, double LonDeg) CellCenter(int row, int col)
        {
            var t = Options.Height <= 1 ? 0.5 : row / (double)(Options.Height - 1);
            var u = Options.Width <= 1 ? 0.5 : col / (double)(Options.Width - 1);
            var lat = Options.LatMax - t * (Options.LatMax - Options.LatMin);
            var lon = Options.LonMin + u * (Options.LonMax - Options.LonMin);
            return (lat, lon);
        }
    }

    /// <summary>Runs the scan. <paramref name="plates"/> — pass <c>null</c> (or an empty array) to
    /// render land/ocean only, no boundary overlay.</summary>
    public static Result Scan(PlanetNoise.Parameters noiseParams, double planetRadiusMeters, TectonicPlates.Plate[]? plates, Options options)
    {
        var cells = new Cell[options.Height, options.Width];
        var elevations = new double[options.Height, options.Width];

        // Only used when Options.Detail is set — the window's own center, so
        // SampleCombined's flat mountain-plane approximation is evaluated as close as possible
        // to where it's actually being asked to be accurate.
        var detailRefLat = (options.LatMin + options.LatMax) / 2.0;
        var detailRefLon = (options.LonMin + options.LonMax) / 2.0;

        for (var row = 0; row < options.Height; row++)
        {
            var t = options.Height <= 1 ? 0.5 : row / (double)(options.Height - 1);
            var lat = options.LatMax - t * (options.LatMax - options.LatMin);

            for (var col = 0; col < options.Width; col++)
            {
                var u = options.Width <= 1 ? 0.5 : col / (double)(options.Width - 1);
                var lon = options.LonMin + u * (options.LonMax - options.LonMin);

                double elevation;
                if (options.Detail)
                {
                    var (offsetX, offsetY) = PlanetNoise.LatLonToOffset(lat, lon, detailRefLat, detailRefLon, planetRadiusMeters);
                    elevation = PlanetNoise.SampleCombined(offsetX, offsetY, detailRefLat, detailRefLon, noiseParams, planetRadiusMeters, plates);
                }
                else
                {
                    elevation = PlanetNoise.SampleLandmass(lat, lon, noiseParams, planetRadiusMeters);
                }
                elevations[row, col] = elevation;

                var cell = elevation >= 0.0 ? Cell.Land : Cell.Ocean;
                if (plates is { Length: > 0 })
                {
                    var (x, y, z) = PlanetNoise.LatLonToUnitVector(lat, lon);
                    var boundary = TectonicPlates.Sample(plates, x, y, z);
                    if (boundary.BoundaryInfluence >= options.BoundaryInfluenceThreshold)
                    {
                        cell = boundary.Boundary switch
                        {
                            TectonicPlates.BoundaryType.Convergent => Cell.Convergent,
                            TectonicPlates.BoundaryType.Divergent => Cell.Divergent,
                            TectonicPlates.BoundaryType.Transform => Cell.Transform,
                            _ => cell,
                        };
                    }
                }

                cells[row, col] = cell;
            }
        }

        return new Result(cells, elevations, options);
    }
}
