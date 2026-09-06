using System.IO;
using GameEngineTools.World.Core.Astro;
using GameEngineTools.World.Data;
using GameEngineTools.World.Location;
using TerrainEditor.Diagnostics;
using TerrainEditor.Models;

namespace TerrainEditor.Services;

/// <summary>
/// Thin wrapper around <see cref="SqliteWorldDatabase"/> for the TerrainEditor UI — exposes
/// locations/heightmap as UI-friendly shapes and writes edits back. Heightmap storage always
/// lives in a dedicated <c>terrain.db</c> (same convention WorldObserver's <c>TerrainMapService</c>
/// already uses) — <c>world.db</c>'s own schema no longer has a <c>TerrainHeightmap</c> table at
/// all. Three ways to open it, mirrored by <see cref="IsTerrainOnly"/>:
/// <list type="bullet">
///   <item><see cref="Open"/> / <see cref="OpenBlank"/> — a world.db file, which auto-opens its
///   sibling <c>terrain.db</c> in the same folder alongside it. Locations, connections, and the
///   heightmap are all available.</item>
///   <item><see cref="OpenTerrainOnly"/> — just a terrain.db, no paired world.db at all. Only
///   heightmap operations work; location/connection/export methods throw.</item>
/// </list>
/// </summary>
public sealed class WorldDatabaseService : IDisposable
{
    /// <summary>Id of the single heightmap grid this tool authors today (see project plan for multi-grid).</summary>
    public const string DefaultHeightmapId = "default";

    private const string TerrainDatabaseFileName = "terrain.db";

    private SqliteWorldDatabase? _db;
    private SqliteWorldDatabase? _terrainDb;

    /// <summary>Bounds how many decoded tiles <see cref="_heightmapCache"/> holds at once — without
    /// a cap, a long session panning across many tiles would accumulate every one of them
    /// (a 400×400 tile is ~640KB just for the elevation floats) for the rest of the session.</summary>
    private const int HeightmapCacheCapacity = 40;

    /// <summary>
    /// In-memory cache of decoded heightmaps, keyed by id — avoids re-hitting SQLite and
    /// re-decoding the BLOB every time the same tile is revisited (e.g. panning back and forth
    /// across a tile boundary, or <see cref="TileStitcher"/> re-stitching a viewport that
    /// still includes tiles from the previous combined view). Kept consistent with what's on disk
    /// by <see cref="SaveHeightmap"/> (updates the entry) and <see cref="Close"/> (clears it
    /// entirely — ids aren't guaranteed unique across different terrain.db files).
    /// </summary>
    private readonly LruCache<string, TerrainHeightmap> _heightmapCache = new(HeightmapCacheCapacity);

    /// <summary>Guards <see cref="_heightmapCache"/> — MainWindow's continuous-tile-panning feature
    /// loads/stitches tiles on a background thread (see <c>EnsureViewportCoverage</c>) while the UI
    /// thread can still call <see cref="LoadHeightmap(string)"/>/<see cref="SaveHeightmap"/> at the
    /// same time (e.g. Save while a pan-triggered background stitch is in flight); the cache itself
    /// has no internal locking, unlike <c>SqliteWorldDatabase</c>'s own connection access.</summary>
    private readonly object _cacheSync = new();

    /// <summary>True once either database is open — world.db+terrain.db (<see cref="Open"/>/
    /// <see cref="OpenBlank"/>) or terrain.db alone (<see cref="OpenTerrainOnly"/>).</summary>
    public bool IsOpen => _db is not null || _terrainDb is not null;

    /// <summary>True when only <c>terrain.db</c> is open (via <see cref="OpenTerrainOnly"/>) —
    /// no world.db, so location/connection/export operations aren't available. UI should disable
    /// those commands and only offer heightmap generation/painting + the tile browser.</summary>
    public bool IsTerrainOnly => _terrainDb is not null && _db is null;

    public string? DatabasePath { get; private set; }

    /// <summary>
    /// This world's cosmology settings, read from an <c>appsettings.World.json</c> next to the
    /// open database (see <see cref="WorldSettingsLoader"/>) — <c>null</c> if that file isn't
    /// present next to this particular database. Not yet consumed by any generator; kept here for
    /// a planned altitude→temperature follow-up.
    /// </summary>
    public AstroConfig? CosmologyConfig { get; private set; }

    /// <summary>
    /// This world's planet — mass, radius, atmosphere, etc. — read from the same
    /// <c>appsettings.World.json</c> as <see cref="CosmologyConfig"/> (its <c>World:Universe</c>
    /// section). <c>null</c> if that file/section isn't present. Consumed by
    /// <c>MainWindow.GoToLatLon</c>, which derives surface gravity from
    /// <c>PlanetMassKg</c>/<c>PlanetEquatorialRadiusKm</c> to scale mountain height.
    /// </summary>
    public UniverseConfig? PlanetConfig { get; private set; }

    /// <summary>The planet-wide reference point/radius the open terrain.db's tiles were flat-
    /// projected against, if it was ever written by a TerraGen batch run (see
    /// <see cref="TerrainGeoReference"/>) — <c>null</c> for a terrain.db authored purely by hand in
    /// TerrainEditor (e.g. only ever used via "Generate Terrain"/"Go to Lat/Long"), since those
    /// grids don't share TerraGen's global tile frame at all.</summary>
    public TerrainGeoReference? GeoReference { get; private set; }

    /// <summary>
    /// Opens (creating if necessary) the world database at <paramref name="path"/>, applying
    /// schema + migrations and — only if the database is entirely empty — the default seed.
    /// Safe to call on an existing, populated database; nothing is overwritten.
    /// </summary>
    public void Open(string path)
    {
        Close();
        _db = new SqliteWorldDatabase(path);
        WorldDatabaseSeeder.Initialize(_db);
        DatabasePath = path;
        CosmologyConfig = WorldSettingsLoader.TryLoadAstroConfig(path);
        PlanetConfig = WorldSettingsLoader.TryLoadUniverseConfig(path);

        OpenTerrainDatabase(path);
        MigrateLegacyTerrainIfPresent();
    }

    /// <summary>
    /// Creates (or re-initialises) the world database at <paramref name="path"/> with schema
    /// only — no <c>seed_data.sql</c> — so <c>Locations</c>/<c>Connections</c> start genuinely
    /// empty, ready to be authored entirely from scratch in the editor. The
    /// caller is responsible for deleting any pre-existing file at <paramref name="path"/> first
    /// if a truly clean slate is intended — schema application alone is idempotent and won't
    /// touch rows already there.
    /// </summary>
    public void OpenBlank(string path)
    {
        Close();
        _db = new SqliteWorldDatabase(path);
        WorldDatabaseSeeder.InitializeSchemaOnly(_db);
        DatabasePath = path;
        CosmologyConfig = WorldSettingsLoader.TryLoadAstroConfig(path);
        PlanetConfig = WorldSettingsLoader.TryLoadUniverseConfig(path);

        OpenTerrainDatabase(path);
        MigrateLegacyTerrainIfPresent();
    }

    /// <summary>
    /// Opens (creating if necessary) ONLY the terrain database at <paramref name="path"/> — no
    /// world.db is opened or created at all. For working purely with heightmap tiles (e.g. a
    /// standalone <c>terrain.db</c> produced by TerraGen, or WorldObserver's persistent one)
    /// without needing a paired world.db alongside it. Location/connection/export operations throw
    /// while in this mode — see <see cref="IsTerrainOnly"/>; only heightmap generation/painting and
    /// the tile browser are available.
    /// </summary>
    public void OpenTerrainOnly(string path)
    {
        Close();
        _terrainDb = new SqliteWorldDatabase(path);
        WorldDatabaseSeeder.InitializeTerrainDatabase(_terrainDb);
        DatabasePath = path;
        CosmologyConfig = WorldSettingsLoader.TryLoadAstroConfig(path);
        PlanetConfig = WorldSettingsLoader.TryLoadUniverseConfig(path);
        GeoReference = _terrainDb.LoadGeoReference();
    }

    /// <summary>Opens (or creates) the sibling <c>terrain.db</c> next to <paramref name="worldDbPath"/>.</summary>
    private void OpenTerrainDatabase(string worldDbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(worldDbPath));
        var terrainPath = string.IsNullOrEmpty(dir) ? TerrainDatabaseFileName : Path.Combine(dir, TerrainDatabaseFileName);
        _terrainDb = new SqliteWorldDatabase(terrainPath);
        WorldDatabaseSeeder.InitializeTerrainDatabase(_terrainDb);
        GeoReference = _terrainDb.LoadGeoReference();
    }

    /// <summary>
    /// One-time migration for a world.db saved before terrain moved to its own database: if the
    /// just-opened file still physically has a <c>TerrainHeightmap</c> table (schema.sql no
    /// longer creates one, but an existing file on disk still has whatever it was created with),
    /// copy its rows into the sibling terrain.db and drop the table from world.db so this only
    /// ever runs once. Silently does nothing for a file that never had the table (the normal case
    /// going forward) or one already migrated.
    /// </summary>
    private void MigrateLegacyTerrainIfPresent()
    {
        if (_db is null || _terrainDb is null) return;

        IReadOnlyList<TerrainHeightmapSummary> legacy;
        try
        {
            legacy = _db.ListHeightmaps();
        }
        catch
        {
            // No TerrainHeightmap table on this file at all — the expected case for anything
            // created after this change, and for a brand-new file. Nothing to migrate.
            return;
        }

        foreach (var summary in legacy)
        {
            var grid = _db.LoadHeightmap(summary.Id);
            if (grid is not null) _terrainDb.SaveHeightmap(grid);
        }

        _db.ExecuteScript("DROP TABLE IF EXISTS TerrainHeightmap;");
    }

    public void Close()
    {
        _db?.Dispose();
        _db = null;
        _terrainDb?.Dispose();
        _terrainDb = null;
        DatabasePath = null;
        CosmologyConfig = null;
        PlanetConfig = null;
        GeoReference = null;
        lock (_cacheSync)
            _heightmapCache.Clear();
    }

    public IReadOnlyList<LocationInfo> GetLocations()
    {
        RequireWorldOpen();
        return _db!.GetAllLocations()
            .Select(l => new LocationInfo(l.Descriptor.Id, l.Descriptor.DisplayName, l.Region,
                l.Descriptor.X, l.Descriptor.Y, l.Descriptor.AltitudeMeters,
                l.Descriptor.Type, l.Descriptor.Terrain, l.Descriptor.BaseNoise,
                l.Descriptor.NoisePerPerson, l.Descriptor.Capacity, l.Descriptor.AllowsPrivacy))
            .ToList();
    }

    /// <summary>Loads the default heightmap grid, or <c>null</c> if none has been saved yet.</summary>
    public TerrainHeightmap? LoadHeightmap() => LoadHeightmap(DefaultHeightmapId);

    /// <summary>Loads a specific heightmap grid by id (e.g. a tile saved by TerraGen), or
    /// <c>null</c> if no row exists with that id. Cached — see <see cref="_heightmapCache"/>.</summary>
    public TerrainHeightmap? LoadHeightmap(string id)
    {
        RequireTerrainOpen();

        lock (_cacheSync)
        {
            if (_heightmapCache.TryGetValue(id, out var cached))
            {
                PerfLog.Log("Cache", $"Heightmap cache HIT: {id}");
                return cached;
            }
        }

        TerrainHeightmap? loaded;
        using (PerfLog.Scope("Cache", $"Heightmap cache MISS: {id} — načítám z terrain.db"))
            loaded = _terrainDb!.LoadHeightmap(id); // SqliteWorldDatabase locks its own connection internally
        if (loaded is not null)
        {
            lock (_cacheSync)
                _heightmapCache.Set(id, loaded);
        }
        return loaded;
    }

    /// <summary>Lists every saved heightmap's metadata (id, position, size) — cheap, doesn't load
    /// any grid's full elevation data. Used by the tile browser to let the designer pick one of
    /// potentially many saved grids (e.g. tiles from a batch planet generator).</summary>
    public IReadOnlyList<TerrainHeightmapSummary> ListHeightmaps()
    {
        RequireTerrainOpen();
        return _terrainDb!.ListHeightmaps();
    }

    /// <summary>Every reach/oxbow in the open terrain.db, for <see cref="RiverNetworkRasterizer"/> — see
    /// <see cref="SqliteWorldDatabase.LoadAllReaches"/>'s remarks on why this isn't network- or tile-keyed.</summary>
    public (IReadOnlyList<RiverReach> Reaches, IReadOnlyList<OxbowLoop> Oxbows) LoadRiverReachesAndOxbows()
    {
        RequireTerrainOpen();
        return (_terrainDb!.LoadAllReaches(), _terrainDb!.LoadAllOxbows());
    }

    public void SaveHeightmap(TerrainHeightmap grid)
    {
        RequireTerrainOpen();
        _terrainDb!.SaveHeightmap(grid);
        lock (_cacheSync)
            _heightmapCache.Set(grid.Id, grid); // keep the cache consistent with what was just written
    }

    /// <summary>
    /// Inserts a brand-new location authored in the editor — not persisted until <c>Save</c> is
    /// clicked. <paramref name="info"/> carries the full descriptor (position gets sampled fresh
    /// at save time anyway, but the social/physical fields come straight from here).
    /// </summary>
    public void InsertLocation(LocationInfo info)
    {
        RequireWorldOpen();
        var descriptor = new LocationDescriptor(
            Id: info.Id,
            DisplayName: info.DisplayName,
            BaseNoise: info.BaseNoise,
            NoisePerPerson: info.NoisePerPerson,
            Capacity: info.Capacity,
            AllowsPrivacy: info.AllowsPrivacy,
            Type: info.Type,
            Terrain: info.Terrain,
            X: info.X,
            Y: info.Y,
            AltitudeMeters: info.AltitudeMeters);
        _db!.InsertLocation(descriptor, info.Region);
    }

    public bool UpdateLocationPosition(string locationId, double x, double y, double altitudeMeters)
    {
        RequireWorldOpen();
        return _db!.UpdateLocationPosition(locationId, x, y, altitudeMeters);
    }

    public bool UpdateLocationRegion(string locationId, string region)
    {
        RequireWorldOpen();
        return _db!.UpdateLocationRegion(locationId, region);
    }

    /// <summary>Updates an existing location's name and social/physical fields (type, terrain,
    /// noise, capacity, privacy) — used by the location-edit dialog (double-click a marker).</summary>
    public bool UpdateLocationDetails(LocationInfo info)
    {
        RequireWorldOpen();
        return _db!.UpdateLocationDetails(info.Id, info.DisplayName, info.Type, info.Terrain,
            info.BaseNoise, info.NoisePerPerson, info.Capacity, info.AllowsPrivacy);
    }

    public IReadOnlyList<ConnectionInfo> GetConnections()
    {
        RequireWorldOpen();
        return _db!.GetAllConnections()
            .Select(c => new ConnectionInfo(c.FromId, c.ToId, c.DistanceMeters))
            .ToList();
    }

    public bool UpdateConnectionDistance(string fromId, string toId, double distanceMeters)
    {
        RequireWorldOpen();
        return _db!.UpdateConnectionDistance(fromId, toId, distanceMeters);
    }

    /// <summary>Inserts a brand-new connection authored in the editor — not persisted until
    /// <c>Save</c> is clicked. Connections are directed rows, so a bidirectional link between two
    /// locations needs this called once per direction.</summary>
    public void InsertConnection(string fromId, string toId, double distanceMeters)
    {
        RequireWorldOpen();
        _db!.InsertConnection(fromId, toId, distanceMeters);
    }

    /// <summary>Regenerates a seed_data.sql-compatible script from the database's current contents.</summary>
    public string ExportSeedSql()
    {
        RequireWorldOpen();
        return WorldDatabaseExporter.ExportSeedSql(_db!, _terrainDb);
    }

    /// <summary>Guards location/connection/export operations, which need world.db — not available
    /// in terrain-only mode (<see cref="OpenTerrainOnly"/>).</summary>
    private void RequireWorldOpen()
    {
        if (_db is null)
            throw new InvalidOperationException(IsTerrainOnly
                ? "Only a terrain-only database is open (OpenTerrainOnly) — location/connection/export operations need a world.db. Call Open() or OpenBlank() instead."
                : "No world database is open. Call Open() first.");
    }

    /// <summary>Guards heightmap operations, which only need terrain.db — available in every open
    /// mode (<see cref="Open"/>, <see cref="OpenBlank"/>, and <see cref="OpenTerrainOnly"/>).</summary>
    private void RequireTerrainOpen()
    {
        if (_terrainDb is null)
            throw new InvalidOperationException("No terrain database is open. Call Open(), OpenBlank(), or OpenTerrainOnly() first.");
    }

    public void Dispose() => Close();
}
