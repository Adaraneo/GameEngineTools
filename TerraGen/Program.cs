using System.Globalization;
using GameEngineTools.World.Data;
using TerraGen;
using TerraGen.Generation;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var options = CliOptions.Parse(args);
if (options is null)
{
    CliOptions.PrintUsage();
    return 1;
}

var planet = PlanetSettings.Load(options.DbPath);
Console.WriteLine($"Planeta: {planet.PlanetName}  gravitace={planet.GravityMs2:0.00} m/s²  " +
                   $"poloměr={planet.PlanetRadiusMeters / 1000.0:0.0} km  seed={planet.Seed}");

// TerraGen only ever stores terrain tiles — never Locations/Connections/WorldObjects — so it
// applies the dedicated terrain schema, not the full world schema.
using var db = new SqliteWorldDatabase(options.DbPath);
WorldDatabaseSeeder.InitializeTerrainDatabase(db);

var noiseParams = new PlanetNoise.Parameters(Seed: planet.Seed, GravityMs2: planet.GravityMs2);
var erosionParams = new TileErosion.Parameters(Seed: planet.Seed, DropletCount: options.DropletsPerTile);

var runSettings = new TileGenerator.RunSettings(
    LatMin: options.LatMin, LatMax: options.LatMax,
    LonMin: options.LonMin, LonMax: options.LonMax,
    TileSizeMeters: options.TileKm * 1000.0,
    CellSizeMeters: options.CellMeters,
    NoiseParams: noiseParams,
    ErosionParams: erosionParams,
    PlanetRadiusMeters: planet.PlanetRadiusMeters);

Console.WriteLine($"Generuji lat [{options.LatMin}:{options.LatMax}] lon [{options.LonMin}:{options.LonMax}], " +
                   $"dlaždice {options.TileKm} km, buňka {options.CellMeters} m, eroze {options.ErosionStrength}%...");

var results = TileGenerator.Run(db, runSettings, line => Console.WriteLine(line));

Console.WriteLine($"Hotovo — {results.Count} dlaždic uloženo do {options.DbPath}.");
return 0;

/// <summary>Parsed and validated CLI arguments for one TerraGen run.</summary>
internal sealed class CliOptions
{
    public required string DbPath { get; init; }
    public required double LatMin { get; init; }
    public required double LatMax { get; init; }
    public required double LonMin { get; init; }
    public required double LonMax { get; init; }
    public double TileKm { get; init; } = 1.0;
    public double CellMeters { get; init; } = 2.5;
    public double ErosionStrength { get; init; } = 50.0;

    /// <summary>Same droplet-count-scales-with-cell-count convention TerrainEditor uses, applied
    /// to one tile's own (unpadded) cell count.</summary>
    public int DropletsPerTile
    {
        get
        {
            var cellsPerSide = Math.Max(1, (int)Math.Round(TileKm * 1000.0 / CellMeters));
            var cellCount = cellsPerSide * cellsPerSide;
            return Math.Min(150_000, (int)(ErosionStrength / 100.0 * cellCount * 2));
        }
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            TerraGen — dávkový generátor sousedících dlaždic planety

            Použití (spouštěj přímo ve složce s databázemi, --db se obvykle nezadává):
              TerraGen --lat-range <min>:<max> --lon-range <min>:<max>
                        [--db <cesta k terrain.db>, výchozí .\terrain.db v aktuální složce]
                        [--tile-km <velikost, výchozí 1>] [--cell-m <velikost buňky, výchozí 2.5>]
                        [--erosion <0-100, výchozí 50>]

            --db je čistě terénní databáze (jen dlaždice heightmapy) — TerraGen nikdy neotvírá
            ani nevytváří žádné world.db s lokacemi/spojeními. Bez --db se použije terrain.db
            v aktuálním pracovním adresáři (vytvoří se, pokud tam ještě není).

            appsettings.World.json (planeta: gravitace, poloměr, seed) se hledá ve stejné
            složce jako --db, nebo v některém z jejích rodičovských adresářů.
            """);
    }

    public static CliOptions? Parse(string[] args)
    {
        string? dbPath = null;
        double? latMin = null, latMax = null, lonMin = null, lonMax = null;
        var tileKm = 1.0;
        var cellMeters = 2.5;
        var erosion = 50.0;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--db" when i + 1 < args.Length:
                    dbPath = args[++i];
                    break;
                case "--lat-range" when i + 1 < args.Length && TryParseRange(args[++i], out var latLo, out var latHi):
                    latMin = latLo; latMax = latHi;
                    break;
                case "--lon-range" when i + 1 < args.Length && TryParseRange(args[++i], out var lonLo, out var lonHi):
                    lonMin = lonLo; lonMax = lonHi;
                    break;
                case "--tile-km" when i + 1 < args.Length && double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var t):
                    tileKm = t;
                    break;
                case "--cell-m" when i + 1 < args.Length && double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var c):
                    cellMeters = c;
                    break;
                case "--erosion" when i + 1 < args.Length && double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var e):
                    erosion = e;
                    break;
                default:
                    Console.Error.WriteLine($"Neznámý nebo neúplný argument: {args[i]}");
                    return null;
            }
        }

        // --db is optional — TerraGen is meant to be run from inside the folder that holds the
        // databases, so it defaults to a terrain.db right there (created if missing) instead of
        // requiring the path to be spelled out every time.
        dbPath ??= Path.Combine(Directory.GetCurrentDirectory(), "terrain.db");

        if (latMin is null || lonMin is null)
        {
            Console.Error.WriteLine("Chybí povinné argumenty --lat-range, --lon-range.");
            return null;
        }
        if (tileKm <= 0 || cellMeters <= 0)
        {
            Console.Error.WriteLine("--tile-km a --cell-m musí být kladné.");
            return null;
        }

        return new CliOptions
        {
            DbPath = dbPath,
            LatMin = latMin.Value, LatMax = latMax!.Value,
            LonMin = lonMin.Value, LonMax = lonMax!.Value,
            TileKm = tileKm, CellMeters = cellMeters, ErosionStrength = Math.Clamp(erosion, 0, 100),
        };
    }

    private static bool TryParseRange(string text, out double lo, out double hi)
    {
        lo = hi = 0;
        var parts = text.Split(':', 2);
        if (parts.Length != 2) return false;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lo)) return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out hi)) return false;
        if (lo > hi) (lo, hi) = (hi, lo);
        return true;
    }
}
