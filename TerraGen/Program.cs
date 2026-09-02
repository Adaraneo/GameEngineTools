using System.Globalization;
using GameEngineTools.World.Data;
using TerraGen;
using TerraGen.Generation;

Console.OutputEncoding = System.Text.Encoding.UTF8;
// Every printed number must round-trip through --lat-range/--lon-range's own parser, which reads
// CultureInfo.InvariantCulture (decimal POINT) — under a comma-decimal OS locale (e.g. Czech),
// default ToString()/interpolation formatting would print commas instead, so the --scan landmass
// table's own "--lat-range X:Y" hints couldn't be pasted back into a real command. Force invariant
// culture process-wide, once, rather than patching every individual WriteLine by hand.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var options = CliOptions.Parse(args);
if (options is null)
{
    CliOptions.PrintUsage();
    return 1;
}

var planet = PlanetSettings.Load(options.DbPath);
// --tectonic-plates overrides the planet's own appsettings.World.json setting when given;
// otherwise every run against this planet uses the same plate count without having to repeat it.
var tectonicPlateCount = options.TectonicPlateCount ?? planet.TectonicPlateCount;
Console.WriteLine($"Planeta: {planet.PlanetName}  gravitace={planet.GravityMs2:0.00} m/s²  " +
                   $"poloměr={planet.PlanetRadiusMeters / 1000.0:0.0} km  seed={planet.Seed}  " +
                   $"tektonické desky={(tectonicPlateCount > 0 ? tectonicPlateCount.ToString() : "vypnuto")}");

if (options.Scan)
{
    var scanNoiseParams = new PlanetNoise.Parameters(Seed: planet.Seed, GravityMs2: planet.GravityMs2,
        TectonicPlateCount: tectonicPlateCount);
    var scanPlates = tectonicPlateCount > 0 ? TectonicPlates.Generate(planet.Seed, tectonicPlateCount) : null;

    if (options.ScanDetail && options.LonMax - options.LonMin >= 90.0)
        Console.WriteLine("Poznámka: --scan-detail je myšlený na úzké okno (řádově jednotky/desítky stupňů) " +
                           "— na takhle širokém rozsahu bude vrstva pohoří zkreslená (viz --help).");

    var scanOptions = new PlanetScanner.Options(
        Width: options.ScanWidth, Height: options.ScanHeight,
        LatMin: options.LatMin, LatMax: options.LatMax, LonMin: options.LonMin, LonMax: options.LonMax,
        BoundaryInfluenceThreshold: options.ScanBoundaryThreshold, Detail: options.ScanDetail);

    if (options.ScanLevels <= 1)
    {
        Console.WriteLine($"Sken lat [{options.LatMin}:{options.LatMax}] lon [{options.LonMin}:{options.LonMax}], " +
                           $"{options.ScanWidth}x{options.ScanHeight} buněk (bez eroze, nic se neukládá)" +
                           (options.ScanDetail ? ", včetně vrstvy pohoří" : "") + "...");

        var scanResult = PlanetScanner.Scan(scanNoiseParams, planet.PlanetRadiusMeters, scanPlates, scanOptions);
        var landmasses = LandmassDetector.Detect(scanResult, planet.PlanetRadiusMeters);
        ScanRenderer.RenderToConsole(scanResult, landmasses);
        if (options.ScanOutputPath is { } outputPath)
        {
            ScanRenderer.SaveToFile(scanResult, landmasses, outputPath);
            Console.WriteLine($"Mapa uložena do {outputPath}.");
        }
        return 0;
    }

    Console.WriteLine($"Postupný sken: {options.ScanLevels} úrovní, každá {options.ScanZoomFactor}x užší " +
                       $"okno kolem nejbližšího pobřeží (bez eroze, nic se neukládá)...");

    var progressiveOptions = new ProgressiveScanner.Options(options.ScanLevels, options.ScanZoomFactor, scanOptions);
    var levels = ProgressiveScanner.Run(scanNoiseParams, planet.PlanetRadiusMeters, scanPlates, progressiveOptions);

    foreach (var level in levels)
    {
        var w = level.WindowUsed;
        Console.WriteLine();
        Console.WriteLine($"══ Úroveň {level.Level + 1}/{levels.Count} — lat [{w.LatMin:0.###}:{w.LatMax:0.###}] " +
                           $"lon [{w.LonMin:0.###}:{w.LonMax:0.###}] ══");
        ScanRenderer.RenderToConsole(level.Scan, level.Landmasses);
        Console.WriteLine(level.CoastlineTarget is { } t
            ? $"Další úroveň se přibližuje k pobřeží u lat={t.LatDeg:0.###} lon={t.LonDeg:0.###}."
            : "V tomhle okně nebylo nalezeno žádné pobřeží — další úroveň se přiblíží ke středu okna.");

        if (options.ScanOutputPath is { } basePath)
        {
            var levelPath = LevelOutputPath(basePath, level.Level);
            ScanRenderer.SaveToFile(level.Scan, level.Landmasses, levelPath);
            Console.WriteLine($"Mapa uložena do {levelPath}.");
        }
    }
    return 0;
}

/// <summary>Inserts <c>_level{N}</c> before the extension — e.g. <c>coast.txt</c> →
/// <c>coast_level2.txt</c> — so a multi-level progressive scan doesn't overwrite the same file
/// on every pass.</summary>
static string LevelOutputPath(string basePath, int level)
{
    var dir = Path.GetDirectoryName(basePath);
    var name = Path.GetFileNameWithoutExtension(basePath);
    var ext = Path.GetExtension(basePath);
    var fileName = $"{name}_level{level}{ext}";
    return string.IsNullOrEmpty(dir) ? fileName : Path.Combine(dir, fileName);
}

// TerraGen only ever stores terrain tiles — never Locations/Connections/WorldObjects — so it
// applies the dedicated terrain schema, not the full world schema.
using var db = new SqliteWorldDatabase(options.DbPath);
WorldDatabaseSeeder.InitializeTerrainDatabase(db);

var noiseParams = new PlanetNoise.Parameters(Seed: planet.Seed, GravityMs2: planet.GravityMs2,
    TectonicPlateCount: tectonicPlateCount);
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
    /// <summary>Full globe (-90:90 / -180:180) unless <c>--lat-range</c>/<c>--lon-range</c> was
    /// given — required outside <see cref="Scan"/> mode, optional (defaults to the whole planet)
    /// within it, since a scan is what you'd run BEFORE knowing which range is worth generating.</summary>
    public required double LatMin { get; init; }
    public required double LatMax { get; init; }
    public required double LonMin { get; init; }
    public required double LonMax { get; init; }
    public double TileKm { get; init; } = 1.0;
    public double CellMeters { get; init; } = 2.5;
    public double ErosionStrength { get; init; } = 50.0;

    /// <summary>Switches to a fast land/ocean/plate-boundary preview (see
    /// <see cref="TerraGen.Generation.PlanetScanner"/>) instead of real tile generation — no
    /// erosion, no database writes, just direct noise sampling over a coarse lat/lon grid.</summary>
    public bool Scan { get; init; }
    public int ScanWidth { get; init; } = 120;
    /// <summary>~ScanWidth/3 by default — a typical terminal character cell is roughly twice as
    /// tall as it is wide, so this keeps the printed map's proportions reading as roughly correct
    /// for the requested lat/lon window instead of vertically stretched.</summary>
    public int ScanHeight { get; init; } = 40;
    public double ScanBoundaryThreshold { get; init; } = 0.9;
    public string? ScanOutputPath { get; init; }
    /// <summary>Adds the mountain-ridge layer to the scan (see <see cref="PlanetScanner.Options.Detail"/>)
    /// — meant for a narrow, already-zoomed-in --lat-range/--lon-range (e.g. one landmass a
    /// plain --scan already pointed you at), not the whole planet.</summary>
    public bool ScanDetail { get; init; }

    /// <summary>1 (default) is a single plain scan. &gt;1 switches to
    /// <see cref="TerraGen.Generation.ProgressiveScanner"/>: repeats the scan, each pass zooming
    /// into the coastline nearest the previous window's center, instead of the caller hand-copying
    /// a landmass's --lat-range/--lon-range between separate runs.</summary>
    public int ScanLevels { get; init; } = 1;
    /// <summary>Each progressive level's window is this many times narrower than the previous
    /// one. Only used when ScanLevels &gt; 1.</summary>
    public double ScanZoomFactor { get; init; } = 4.0;

    /// <summary><c>null</c> (default — not passed on the command line) means "use whatever
    /// <c>appsettings.World.json</c>'s <c>PlanetTectonicPlateCount</c> says for this planet" (see
    /// <see cref="PlanetSettings"/>). Passing <c>--tectonic-plates</c> explicitly overrides that
    /// for just this one run, including passing <c>0</c> to fall back to the original
    /// single-global-belt mountain layer for a planet whose config otherwise has tectonics on.</summary>
    public int? TectonicPlateCount { get; init; }

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
                        [--erosion <0-100, výchozí 50>] [--tectonic-plates <počet, výchozí 0 = vypnuto>]

            --tectonic-plates > 0 přepne pohoří/prolomeniny z jednoho pevného pásu (výchozí) na
            desky — pohoří vznikají na sbíhavých hranicích desek, prolomeniny na rozbíhavých.
            Typická hodnota pro planetu podobnou Zemi je 8-16. Existující dlaždice vygenerované
            bez tohoto přepínače musí dál generovat identicky, proto je výchozí hodnota 0.

            --db je čistě terénní databáze (jen dlaždice heightmapy) — TerraGen nikdy neotvírá
            ani nevytváří žádné world.db s lokacemi/spojeními. Bez --db se použije terrain.db
            v aktuálním pracovním adresáři (vytvoří se, pokud tam ještě není).

            appsettings.World.json (planeta: gravitace, poloměr, seed, tektonické desky —
            World:Universe:PlanetTectonicPlateCount) se hledá ve stejné složce jako --db, nebo
            v některém z jejích rodičovských adresářů. --tectonic-plates přebije hodnotu z configu
            jen pro tento běh.

            ── Sken (rychlý náhled pevnina/oceán/hranice desek, bez ukládání) ──────────────
              TerraGen --scan
                        [--lat-range <min>:<max> --lon-range <min>:<max>, výchozí celá planeta]
                        [--scan-width <znaků, výchozí 120>] [--scan-height <řádků, výchozí 40>]
                        [--scan-boundary-threshold <0-1, výchozí 0.9>]
                        [--scan-detail]
                        [--scan-levels <počet, výchozí 1> --scan-zoom <násobek zúžení, výchozí 4>]
                        [--scan-output <cesta .txt>, volitelně uloží mapu i do souboru]

            --scan vypíše ASCII mapu do konzole (barevně, včetně stínování nadmořské/podmořské
            výšky) přímým vzorkováním kontinentálního šumu (bez eroze, bez dlaždic, bez zápisu
            do DB) — použij ho PŘED skutečným generováním, ať víš, kam vůbec mířit
            --lat-range/--lon-range. Souš je značená číslem/písmenem podle toho, ke které
            souvislé pevnině patří (viz tabulka pod mapou); '~' = oceán; s aktivními
            --tectonic-plates navíc '^' = sbíhavá hranice (pohoří), 'v' = rozbíhavá
            (prolomenina/rift), 'x' = transformní. Pod mapou je tabulka VŠECH nalezených pevnin
            seřazená podle plochy — u každé je odhad km², střed a rovnou hotový
            --lat-range/--lon-range k vložení do dalšího běhu.

            Normální --scan ukazuje jen kde vznikne SOUŠ, ne jak hornatá bude zblízka — vrstva
            pohoří je totiž platná jen lokálně kolem jednoho referenčního bodu (viz komentáře u
            PlanetNoise), takže ji přes celou planetu nemá smysl vzorkovat. --scan-detail tohle
            obchází: použije STŘED aktuálního --lat-range/--lon-range okna jako svůj vlastní
            referenční bod a přidá i vrstvu pohoří — takže dává smysl JEN na už zúžené okno
            (typicky ten --lat-range/--lon-range, co ti vypsal běžný --scan pro konkrétní
            pevninu), ne na celou planetu. Pracovní postup: --scan → najdi pevninu v tabulce →
            zkopíruj její --lat-range/--lon-range → spusť znovu s --scan-detail pro detailnější
            náhled hor/prolomenin → teprve pak skutečné generování (bez --scan) se skutečnou
            erozí.

            --scan-levels > 1 tenhle postup zautomatizuje: místo jednoho skenu proběhne N kol,
            každé s --scan-zoom-krát užším oknem (ve stupních) než to předchozí, VŽDY vystředěné
            na nejbližší pobřeží (hranici souš/oceán) k předchozímu středu — samo se tedy postupně
            přiblíží ke konkrétnímu kusu pobřeží, aniž bys musel mezi běhy ručně kopírovat
            --lat-range/--lon-range. Každá úroveň se vypíše zvlášť (a s --scan-output uloží do
            samostatného souboru s příponou _level0, _level1, ...). Když v okně žádné pobřeží
            není (celé moře/celá pevnina), další úroveň se přiblíží jen ke středu okna.
            """);
    }

    public static CliOptions? Parse(string[] args)
    {
        string? dbPath = null;
        double? latMin = null, latMax = null, lonMin = null, lonMax = null;
        var tileKm = 1.0;
        var cellMeters = 2.5;
        var erosion = 50.0;
        int? tectonicPlateCount = null;
        var scan = false;
        var scanWidth = 120;
        var scanHeight = 40;
        var scanBoundaryThreshold = 0.9;
        string? scanOutputPath = null;
        var scanDetail = false;
        var scanLevels = 1;
        var scanZoomFactor = 4.0;

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
                case "--tectonic-plates" when i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tp):
                    tectonicPlateCount = tp;
                    break;
                case "--scan":
                    scan = true;
                    break;
                case "--scan-width" when i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sw):
                    scanWidth = sw;
                    break;
                case "--scan-height" when i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sh):
                    scanHeight = sh;
                    break;
                case "--scan-boundary-threshold" when i + 1 < args.Length && double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var sbt):
                    scanBoundaryThreshold = sbt;
                    break;
                case "--scan-output" when i + 1 < args.Length:
                    scanOutputPath = args[++i];
                    break;
                case "--scan-detail":
                    scanDetail = true;
                    break;
                case "--scan-levels" when i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sl):
                    scanLevels = sl;
                    break;
                case "--scan-zoom" when i + 1 < args.Length && double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var sz):
                    scanZoomFactor = sz;
                    break;
                default:
                    Console.Error.WriteLine($"Neznámý nebo neúplný argument: {args[i]}");
                    return null;
            }
        }

        // --db is optional — TerraGen is meant to be run from inside the folder that holds the
        // databases, so it defaults to a terrain.db right there (created if missing) instead of
        // requiring the path to be spelled out every time. --scan never touches it, but it's still
        // used to locate appsettings.World.json (see PlanetSettings.Load), so it's resolved either way.
        dbPath ??= Path.Combine(Directory.GetCurrentDirectory(), "terrain.db");

        if (latMin is null || lonMin is null)
        {
            if (!scan)
            {
                Console.Error.WriteLine("Chybí povinné argumenty --lat-range, --lon-range.");
                return null;
            }
            // --scan without an explicit window previews the whole planet — that's the point of
            // running it before deciding which range is even worth generating for real.
            latMin = -90.0; latMax = 90.0;
            lonMin = -180.0; lonMax = 180.0;
        }
        if (tileKm <= 0 || cellMeters <= 0)
        {
            Console.Error.WriteLine("--tile-km a --cell-m musí být kladné.");
            return null;
        }
        if (tectonicPlateCount < 0)
        {
            Console.Error.WriteLine("--tectonic-plates nesmí být záporné.");
            return null;
        }
        if (scan && (scanWidth <= 0 || scanHeight <= 0))
        {
            Console.Error.WriteLine("--scan-width a --scan-height musí být kladné.");
            return null;
        }
        if (scanDetail && !scan)
        {
            Console.Error.WriteLine("--scan-detail má smysl jen společně s --scan.");
            return null;
        }
        if (scanLevels > 1 && !scan)
        {
            Console.Error.WriteLine("--scan-levels má smysl jen společně s --scan.");
            return null;
        }
        if (scanLevels < 1)
        {
            Console.Error.WriteLine("--scan-levels musí být kladné.");
            return null;
        }
        if (scanLevels > 1 && scanZoomFactor <= 1.0)
        {
            Console.Error.WriteLine("--scan-zoom musí být > 1 (jinak by se okno mezi úrovněmi nezmenšovalo).");
            return null;
        }

        return new CliOptions
        {
            DbPath = dbPath,
            LatMin = latMin.Value, LatMax = latMax!.Value,
            LonMin = lonMin.Value, LonMax = lonMax!.Value,
            TileKm = tileKm, CellMeters = cellMeters, ErosionStrength = Math.Clamp(erosion, 0, 100),
            TectonicPlateCount = tectonicPlateCount,
            Scan = scan, ScanWidth = scanWidth, ScanHeight = scanHeight,
            ScanBoundaryThreshold = scanBoundaryThreshold, ScanOutputPath = scanOutputPath,
            ScanDetail = scanDetail, ScanLevels = scanLevels, ScanZoomFactor = scanZoomFactor,
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
