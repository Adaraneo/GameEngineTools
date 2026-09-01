namespace TerraGen.Generation;

/// <summary>Renders a <see cref="PlanetScanner.Result"/> as a colored ASCII map to the console,
/// or as a plain-text file with a legend — kept separate from <see cref="PlanetScanner"/> itself
/// so the scan logic stays presentation-free (testable without a console).</summary>
public static class ScanRenderer
{
    public static void RenderToConsole(PlanetScanner.Result result)
    {
        var options = result.Options;
        var height = result.Cells.GetLength(0);
        var width = result.Cells.GetLength(1);

        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                Console.ForegroundColor = result.Cells[row, col] switch
                {
                    PlanetScanner.Cell.Convergent => ConsoleColor.Red,
                    PlanetScanner.Cell.Divergent => ConsoleColor.Magenta,
                    PlanetScanner.Cell.Transform => ConsoleColor.DarkYellow,
                    PlanetScanner.Cell.Land => ConsoleColor.Green,
                    _ => ConsoleColor.Cyan,
                };
                Console.Write(result.Symbol(row, col));
            }
            Console.ResetColor();
            Console.WriteLine();
        }
        Console.ResetColor();

        Console.WriteLine($"lat [{options.LatMax:0.0} nahoře .. {options.LatMin:0.0} dole]  " +
                           $"lon [{options.LonMin:0.0} vlevo .. {options.LonMax:0.0} vpravo]");
        Console.WriteLine("Legenda: . = souš   ~ = oceán   ^ = sbíhavá hranice (pohoří)   " +
                           "v = rozbíhavá (rift)   x = transformní");
    }

    public static void SaveToFile(PlanetScanner.Result result, string path)
    {
        var options = result.Options;
        var height = result.Cells.GetLength(0);
        var width = result.Cells.GetLength(1);

        using var writer = new StreamWriter(path, append: false, System.Text.Encoding.UTF8);
        writer.WriteLine($"TerraGen --scan  lat [{options.LatMin:0.###}:{options.LatMax:0.###}]  " +
                          $"lon [{options.LonMin:0.###}:{options.LonMax:0.###}]  {width}x{height}");
        for (var row = 0; row < height; row++)
        {
            var line = new char[width];
            for (var col = 0; col < width; col++)
                line[col] = result.Symbol(row, col);
            writer.WriteLine(line);
        }
        writer.WriteLine("Legenda: . = souš   ~ = oceán   ^ = sbíhavá hranice (pohoří)   " +
                          "v = rozbíhavá (rift)   x = transformní");
    }
}
