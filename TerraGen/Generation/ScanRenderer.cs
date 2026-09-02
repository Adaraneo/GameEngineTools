namespace TerraGen.Generation;

/// <summary>Renders a <see cref="PlanetScanner.Result"/> (plus its <see cref="LandmassDetector.Detection"/>)
/// as a colored ASCII map to the console, or as a plain-text file with a legend and a ranked
/// landmass table — kept separate from <see cref="PlanetScanner"/>/<see cref="LandmassDetector"/>
/// themselves so the scan/detection logic stays presentation-free (testable without a console).</summary>
public static class ScanRenderer
{
    /// <summary>Per-landmass map label, indexed by <c>Rank - 1</c> — ranks beyond this fall back
    /// to '#' rather than throwing (still shown correctly in the table either way).</summary>
    private const string LandmassLabels = "123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const int MaxDetailedLandmasses = 20;

    public static void RenderToConsole(PlanetScanner.Result result, LandmassDetector.Detection detection)
    {
        var height = result.Cells.GetLength(0);
        var width = result.Cells.GetLength(1);
        var (elevationMin, elevationMax) = ElevationRange(result.ElevationsMeters);

        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                Console.ForegroundColor = result.Cells[row, col] switch
                {
                    PlanetScanner.Cell.Convergent => ConsoleColor.Red,
                    PlanetScanner.Cell.Divergent => ConsoleColor.Magenta,
                    PlanetScanner.Cell.Transform => ConsoleColor.DarkYellow,
                    _ => ElevationColor(result.ElevationsMeters[row, col], elevationMin, elevationMax),
                };
                Console.Write(LabeledSymbol(result, detection, row, col));
            }
            Console.ResetColor();
            Console.WriteLine();
        }
        Console.ResetColor();

        var options = result.Options;
        Console.WriteLine($"lat [{options.LatMax:0.0} nahoře .. {options.LatMin:0.0} dole]  " +
                           $"lon [{options.LonMin:0.0} vlevo .. {options.LonMax:0.0} vpravo]" +
                           (options.Detail ? "  (--scan-detail: včetně vrstvy pohoří)" : ""));
        Console.WriteLine("Legenda: 1-9/A-Z = souš, číslo/písmeno = ID pevniny (viz tabulka níže; barva = " +
                           "nadmořská/podmořská výška)   ~ = oceán   ^ = sbíhavá hranice (pohoří)   " +
                           "v = rozbíhavá (rift)   x = transformní");

        WriteLandmassTable(Console.Out, detection);
    }

    private static (double Min, double Max) ElevationRange(double[,] elevations)
    {
        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var e in elevations)
        {
            if (e < min) min = e;
            if (e > max) max = e;
        }
        return (min, max);
    }

    /// <summary>Three shades each side of sea level, scaled against the ACTUAL min/max elevation
    /// in this particular scan (not a fixed constant) — so shading stays meaningful whether this
    /// is a global overview (small landmass-only amplitude) or a <see cref="PlanetScanner.Options.Detail"/>
    /// zoom (much taller, since it includes the mountain-ridge layer).</summary>
    private static ConsoleColor ElevationColor(double elevationMeters, double min, double max)
    {
        if (elevationMeters < 0.0)
        {
            var depthT = min < 0.0 ? Math.Clamp(elevationMeters / min, 0.0, 1.0) : 0.0;
            return depthT > 0.66 ? ConsoleColor.DarkBlue : depthT > 0.33 ? ConsoleColor.Blue : ConsoleColor.Cyan;
        }

        var heightT = max > 0.0 ? Math.Clamp(elevationMeters / max, 0.0, 1.0) : 0.0;
        return heightT > 0.66 ? ConsoleColor.DarkGray : heightT > 0.33 ? ConsoleColor.DarkGreen : ConsoleColor.Green;
    }

    public static void SaveToFile(PlanetScanner.Result result, LandmassDetector.Detection detection, string path)
    {
        var options = result.Options;
        var height = result.Cells.GetLength(0);
        var width = result.Cells.GetLength(1);

        using var writer = new StreamWriter(path, append: false, System.Text.Encoding.UTF8);
        writer.WriteLine($"TerraGen --scan  lat [{options.LatMin:0.###}:{options.LatMax:0.###}]  " +
                          $"lon [{options.LonMin:0.###}:{options.LonMax:0.###}]  {width}x{height}" +
                          (options.Detail ? "  (--scan-detail: včetně vrstvy pohoří)" : ""));
        for (var row = 0; row < height; row++)
        {
            var line = new char[width];
            for (var col = 0; col < width; col++)
                line[col] = LabeledSymbol(result, detection, row, col);
            writer.WriteLine(line);
        }
        writer.WriteLine("Legenda: 1-9/A-Z = souš, číslo/písmeno = ID pevniny (viz tabulka níže)   " +
                          "~ = oceán   ^ = sbíhavá hranice (pohoří)   v = rozbíhavá (rift)   x = transformní");

        WriteLandmassTable(writer, detection);
    }

    /// <summary>Land cells show their owning landmass's label instead of a flat '.'; ocean and
    /// plate-boundary markers are unaffected — those already carry their own meaning.</summary>
    private static char LabeledSymbol(PlanetScanner.Result result, LandmassDetector.Detection detection, int row, int col)
    {
        if (result.Cells[row, col] != PlanetScanner.Cell.Land) return result.Symbol(row, col);

        var rank = detection.LandmassRankByCell[row, col];
        if (rank <= 0) return result.Symbol(row, col); // shouldn't happen, but never crash a preview tool over it
        return rank <= LandmassLabels.Length ? LandmassLabels[rank - 1] : '#';
    }

    private static void WriteLandmassTable(TextWriter writer, LandmassDetector.Detection detection)
    {
        writer.WriteLine();
        writer.WriteLine($"Nalezeno {detection.Landmasses.Count} souvislých pevnin (řazeno podle plochy):");

        foreach (var lm in detection.Landmasses.Take(MaxDetailedLandmasses))
        {
            var label = lm.Rank <= LandmassLabels.Length ? LandmassLabels[lm.Rank - 1].ToString() : "#";
            writer.WriteLine($"  [{label}] {lm.AreaKm2:N0} km²  ({lm.CellCount} buněk skenu)  " +
                              $"střed lat={lm.CentroidLatDeg:0.00} lon={lm.CentroidLonDeg:0.00}   " +
                              $"--lat-range {lm.LatMin:0.###}:{lm.LatMax:0.###} --lon-range {lm.LonMin:0.###}:{lm.LonMax:0.###}");
        }

        if (detection.Landmasses.Count > MaxDetailedLandmasses)
        {
            var rest = detection.Landmasses.Skip(MaxDetailedLandmasses).ToList();
            writer.WriteLine($"  ... a dalších {rest.Count} menších (souhrnná plocha {rest.Sum(l => l.AreaKm2):N0} km²)");
        }

        writer.WriteLine("Rozsahy jsou hrubé (odhad ze skenu, ne přesná hranice pobřeží) — před skutečným");
        writer.WriteLine("generováním --lat-range/--lon-range mírně přidej rezervu na okraje.");
    }
}
