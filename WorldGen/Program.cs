using System.Globalization;
using GameEngineTools.World.Data;
using WorldGen;
using WorldGen.Generation;

Console.OutputEncoding = System.Text.Encoding.UTF8;
// Keep console number formatting consistent with TerraGen's own fix — an ambient comma-decimal
// locale (e.g. Czech) would otherwise print values that don't round-trip through the
// InvariantCulture parsers this tool's own CLI args use.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var options = CliOptions.Parse(args);
if (options is null)
{
    CliOptions.PrintUsage();
    return 1;
}

if (!File.Exists(options.TerrainDbPath))
{
    Console.Error.WriteLine($"terrain.db nenalezena: {options.TerrainDbPath}");
    Console.Error.WriteLine("Spusť napřed terragen — worldgen umísťuje lokace jen na už vygenerovaný terén.");
    return 1;
}

using var terrainDb = new SqliteWorldDatabase(options.TerrainDbPath);
WorldDatabaseSeeder.InitializeTerrainDatabase(terrainDb);

var summaries = terrainDb.ListHeightmaps();
if (summaries.Count == 0)
{
    Console.Error.WriteLine($"V {options.TerrainDbPath} nejsou žádné vygenerované dlaždice.");
    Console.Error.WriteLine("Spusť napřed terragen — worldgen umísťuje lokace jen na už vygenerovaný terén.");
    return 1;
}

var tiles = summaries
    .Select(s => terrainDb.LoadHeightmap(s.Id))
    .Where(t => t is not null)
    .Select(t => t!)
    .ToList();

Console.WriteLine($"Nalezeno {tiles.Count} dlaždic terénu v {options.TerrainDbPath}.");

var catalog = NutritionCatalogLoader.Load(options.NutritionCsvPath);
var catalogSource = options.NutritionCsvPath is not null && File.Exists(options.NutritionCsvPath)
    ? options.NutritionCsvPath
    : "vestavěný výchozí katalog";
Console.WriteLine($"Katalog jídla/pití/odpočinku: {catalog.Count} vzorů ({catalogSource}).");

// worldgen genuinely creates world.db when it doesn't exist yet — unlike TerraGen (which stays
// terrain-only), adding locations/connections/objects to a world.db is its entire purpose.
using var worldDb = new SqliteWorldDatabase(options.WorldDbPath);
WorldDatabaseSeeder.Initialize(worldDb);

var rng = options.Seed is { } seed ? new Random(seed) : new Random();

// Reads the SAME appsettings.World.json TerraGen reads for this planet, so the tectonic plate
// layout WorldGen samples for danger weighting is automatically the exact one the terrain being
// placed on was generated with — no matching CLI flags to keep in sync by hand across two tools.
var planet = WorldGen.PlanetSettings.Load(options.TerrainDbPath);
var tectonicPlateCount = options.TectonicPlateCount ?? planet.TectonicPlateCount;
if (tectonicPlateCount > 0)
    Console.WriteLine($"Tektonické desky: {tectonicPlateCount} (seed={planet.Seed}, poloměr={planet.PlanetRadiusMeters / 1000.0:0.0} km).");

var genOptions = new WorldContentGenerator.Options(
    Count: options.Count,
    Region: options.Region,
    MinDistanceMeters: options.MinDistanceMeters,
    ConnectionsPerLocation: options.ConnectionsPerLocation,
    MountainThresholdMeters: options.MountainThresholdMeters,
    CoastRadiusMeters: options.CoastRadiusMeters,
    TectonicPlateCount: tectonicPlateCount,
    TectonicSeed: planet.Seed,
    PlanetRadiusMeters: planet.PlanetRadiusMeters,
    // Reuses the planet's own seed (already read from the same appsettings.World.json TerraGen
    // reads) so the climate map is reproducible per-planet without a separate CLI flag — same
    // convention as TectonicSeed above.
    ClimateSeed: planet.Seed,
    GenerateHouses: options.GenerateHouses,
    GenerateCemetery: options.GenerateCemetery,
    GenerateProductionChain: options.GenerateProductionChain);

Console.WriteLine($"Generuji {options.Count} lokací v regionu '{options.Region}'...");

var result = WorldContentGenerator.Generate(worldDb, tiles, genOptions, rng, catalog);

Console.WriteLine($"Hotovo — {result.LocationsPlaced}/{options.Count} lokací, {result.ConnectionsCreated} spojení, " +
                   $"{result.ObjectsCreated} objektů uloženo do {options.WorldDbPath}.");
if (result.CemeteryLocationId is not null)
    Console.WriteLine($"Hřbitov: {result.CemeteryLocationId}");
if (result.LocationsPlaced < options.Count)
    Console.WriteLine($"Poznámka: {options.Count - result.LocationsPlaced} lokací se nepodařilo umístit " +
                       "(nedostatek volné souše, nebo moc málo místa při zadaném --min-distance).");

return 0;

/// <summary>Parsed and validated CLI arguments for one WorldGen run.</summary>
internal sealed class CliOptions
{
    public required string WorldDbPath { get; init; }
    public required string TerrainDbPath { get; init; }
    public required int Count { get; init; }
    public string Region { get; init; } = "Wilds";
    public double MinDistanceMeters { get; init; } = 150.0;
    public int ConnectionsPerLocation { get; init; } = 2;
    public double MountainThresholdMeters { get; init; } = 300.0;
    public double CoastRadiusMeters { get; init; } = 60.0;
    /// <summary><c>null</c> means "use the planet's own appsettings.World.json tectonic plate
    /// count" (whatever TerraGen was run with) — same override convention as TerraGen's own
    /// <c>--tectonic-plates</c>.</summary>
    public int? TectonicPlateCount { get; init; }
    public int? Seed { get; init; }
    /// <summary>Disk override for the food/drink/rest catalog. Defaults to <c>.\Nutrition.csv</c>
    /// in the current directory when present, else <c>null</c> (embedded default catalog is used).</summary>
    public string? NutritionCsvPath { get; init; }
    /// <summary>Whether Village/Town settlements get Rest-type house sub-locations
    /// (<c>--no-houses</c> disables). On by default so a plain run is ready for a character
    /// simulation (e.g. GameSandbox) to assign homes from.</summary>
    public bool GenerateHouses { get; init; } = true;
    /// <summary>Whether a single deterministic-id cemetery location is created
    /// (<c>--no-cemetery</c> disables).</summary>
    public bool GenerateCemetery { get; init; } = true;
    /// <summary>Whether a field→mill→bakery production chain is attached to the largest placed
    /// settlement (<c>--no-production</c> disables).</summary>
    public bool GenerateProductionChain { get; init; } = true;

    public static void PrintUsage()
    {
        Console.WriteLine("""
            WorldGen — generátor lokací, spojení a affordances do world.db

            Použití (spouštěj přímo ve složce s databázemi, --world-db/--terrain-db se obvykle nezadávají):
              WorldGen --count <N>
                        [--world-db <cesta>, výchozí .\world.db v aktuální složce]
                        [--terrain-db <cesta>, výchozí .\terrain.db v aktuální složce]
                        [--region <název, výchozí "Wilds">]
                        [--min-distance <metry, výchozí 150>]
                        [--connections <počet nejbližších sousedů, výchozí 2>]
                        [--mountain-threshold <metry nadmořské výšky, výchozí 300>]
                        [--coast-radius <metry, výchozí 60>]
                        [--tectonic-plates <počet, výchozí = hodnota z appsettings.World.json>]
                        [--seed <celé číslo, výchozí náhodné>]
                        [--nutrition-csv <cesta>, výchozí .\Nutrition.csv v aktuální složce,
                                            jinak vestavěný výchozí katalog]
                        [--no-houses] [--no-cemetery] [--no-production]

            Kromě samotných osad (ve výchozím nastavení) přidá ke každé vesnici/městu pár
            obytných Rest lokací ("domů"), jeden hřbitov na deterministickém id
            "<region>_cemetery" a řetězec pole→mlýn→pekárna u největší vygenerované osady —
            přesně to, co potřebuje simulace postav (např. GameSandbox) k přiřazení domovů,
            pohřbívání a potravinové ekonomiky bez ručně psaného obsahu. Kterýkoli z --no-*
            přepínačů daný krok vypne.

            Lokace umisťuje výhradně na pozice pokryté už vygenerovanými dlaždicemi v terrain.db
            (spusť napřed terragen) — nikdy negeneruje nový terén. --world-db se VYTVOŘÍ, pokud
            ještě neexistuje (na rozdíl od TerraGenu, který se world.db nikdy nedotýká).

            Každá lokace se klasifikuje podle výšky, sklonu a lehkého klimatického modelu (teplota
            ze zeměpisné šířky + nadmořské výšky, vlhkost z nezávislé šumové vrstvy — žádné
            sezónnosti/větru, viz WorldGen.Generation.ClimateModel): Mountain (nad
            --mountain-threshold) → Tundra (pod bodem mrazu) → Coastline (do --coast-radius od
            vody) → Desert/Jungle (horko+sucho/horko+vlhko) → Savanna/Plains (plochý terén, dle
            vlhkosti) → Forest (zbytek, svažitý terén). Dostane náhodně jednu ze tří úrovní
            osídlení — tábor/vesnice/město — váženou podle biomu (hory/poušť/tundra/džungle =
            většinou tábor, pobřeží/planiny/savana = častěji vesnice nebo město). Silniční síť je
            hierarchická, ne jen nejbližší soused: města mezi sebou tvoří minimální kostru (každé
            dosáhne na každé, nejmíň hran), každá vesnice se připojí k nejbližšímu městu a každý
            tábor k nejbližší vesnici nebo městu (podle toho, co je blíž) — navíc si každá lokace
            přidá pár lokálních spojení k nejbližším sousedům stejné úrovně. Vzdálenosti počítá
            RoadPathfinder (vyhýbá se prudkým svahům a — po `terragen --rivers` — i řekám).

            --tectonic-plates > 0 (čte se automaticky z appsettings.World.json, stejně jako v
            TerraGenu — přepínač jen přebíjí hodnotu pro tento běh) zvyšuje DangerLevel lokací
            blízko sbíhavých/rozbíhavých hranic desek.

            Jídlo/pití/odpočinek pro každou lokaci se vybírá z katalogu v Nutrition.csv podle
            biomu dané lokace (Forest/Mountain/Plains/Coastline/Any) — zkopíruj vestavěný soubor
            vedle databází a uprav/přidej řádky, žádná změna kódu není potřeba.
            """);
    }

    public static CliOptions? Parse(string[] args)
    {
        string? worldDbPath = null;
        string? terrainDbPath = null;
        int? count = null;
        var region = "Wilds";
        var minDistance = 150.0;
        var connections = 2;
        var mountainThreshold = 300.0;
        var coastRadius = 60.0;
        int? tectonicPlateCount = null;
        int? seed = null;
        string? nutritionCsvPath = null;
        var generateHouses = true;
        var generateCemetery = true;
        var generateProductionChain = true;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--world-db" when i + 1 < args.Length:
                    worldDbPath = args[++i];
                    break;
                case "--terrain-db" when i + 1 < args.Length:
                    terrainDbPath = args[++i];
                    break;
                case "--count" when i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var c):
                    count = c;
                    break;
                case "--region" when i + 1 < args.Length:
                    region = args[++i];
                    break;
                case "--min-distance" when i + 1 < args.Length && double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var md):
                    minDistance = md;
                    break;
                case "--connections" when i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var conn):
                    connections = conn;
                    break;
                case "--mountain-threshold" when i + 1 < args.Length && double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var mt):
                    mountainThreshold = mt;
                    break;
                case "--coast-radius" when i + 1 < args.Length && double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var cr):
                    coastRadius = cr;
                    break;
                case "--tectonic-plates" when i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tp):
                    tectonicPlateCount = tp;
                    break;
                case "--seed" when i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s):
                    seed = s;
                    break;
                case "--nutrition-csv" when i + 1 < args.Length:
                    nutritionCsvPath = args[++i];
                    break;
                case "--no-houses":
                    generateHouses = false;
                    break;
                case "--no-cemetery":
                    generateCemetery = false;
                    break;
                case "--no-production":
                    generateProductionChain = false;
                    break;
                default:
                    Console.Error.WriteLine($"Neznámý nebo neúplný argument: {args[i]}");
                    return null;
            }
        }

        if (count is null)
        {
            Console.Error.WriteLine("Chybí povinný argument --count.");
            return null;
        }
        if (count <= 0)
        {
            Console.Error.WriteLine("--count musí být kladné.");
            return null;
        }
        if (minDistance < 0 || connections < 0)
        {
            Console.Error.WriteLine("--min-distance a --connections nesmí být záporné.");
            return null;
        }
        if (coastRadius < 0)
        {
            Console.Error.WriteLine("--coast-radius nesmí být záporné.");
            return null;
        }
        if (tectonicPlateCount < 0)
        {
            Console.Error.WriteLine("--tectonic-plates nesmí být záporné.");
            return null;
        }

        worldDbPath ??= Path.Combine(Directory.GetCurrentDirectory(), "world.db");
        terrainDbPath ??= Path.Combine(Directory.GetCurrentDirectory(), "terrain.db");
        // No --nutrition-csv given: fall back to a conventional Nutrition.csv in the current
        // directory if present, same disk-override spirit as appsettings.World.json's search —
        // NutritionCatalogLoader itself falls back further to the embedded default catalog.
        nutritionCsvPath ??= Path.Combine(Directory.GetCurrentDirectory(), "Nutrition.csv");

        return new CliOptions
        {
            WorldDbPath = worldDbPath,
            TerrainDbPath = terrainDbPath,
            Count = count.Value,
            Region = region,
            MinDistanceMeters = minDistance,
            ConnectionsPerLocation = connections,
            MountainThresholdMeters = mountainThreshold,
            CoastRadiusMeters = coastRadius,
            TectonicPlateCount = tectonicPlateCount,
            Seed = seed,
            NutritionCsvPath = nutritionCsvPath,
            GenerateHouses = generateHouses,
            GenerateCemetery = generateCemetery,
            GenerateProductionChain = generateProductionChain,
        };
    }
}
