using System.IO;
using GameEngineTools.World.Core.Astro;
using GameEngineTools.World.Data;
using GameEngineTools.World.Location;
using TerrainEditor.Models;

namespace TerrainEditor.Services;

/// <summary>
/// Thin wrapper around <see cref="SqliteWorldDatabase"/> for the TerrainEditor UI — opens a
/// world.db file, exposes locations/heightmap as UI-friendly shapes, and writes edits back.
/// Heightmap storage lives in a SEPARATE sibling <c>terrain.db</c> next to the opened world.db
/// (same convention WorldObserver's <c>TerrainMapService</c> already uses) — <c>world.db</c>'s
/// own schema no longer has a <c>TerrainHeightmap</c> table at all.
/// </summary>
public sealed class WorldDatabaseService : IDisposable
{
    /// <summary>Id of the single heightmap grid this tool authors today (see project plan for multi-grid).</summary>
    public const string DefaultHeightmapId = "default";

    private const string TerrainDatabaseFileName = "terrain.db";

    private SqliteWorldDatabase? _db;
    private SqliteWorldDatabase? _terrainDb;

    public bool IsOpen => _db is not null;
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

    /// <summary>Opens (or creates) the sibling <c>terrain.db</c> next to <paramref name="worldDbPath"/>.</summary>
    private void OpenTerrainDatabase(string worldDbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(worldDbPath));
        var terrainPath = string.IsNullOrEmpty(dir) ? TerrainDatabaseFileName : Path.Combine(dir, TerrainDatabaseFileName);
        _terrainDb = new SqliteWorldDatabase(terrainPath);
        WorldDatabaseSeeder.InitializeTerrainDatabase(_terrainDb);
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
    }

    public IReadOnlyList<LocationInfo> GetLocations()
    {
        RequireOpen();
        return _db!.GetAllLocations()
            .Select(l => new LocationInfo(l.Descriptor.Id, l.Descriptor.DisplayName, l.Region,
                l.Descriptor.X, l.Descriptor.Y, l.Descriptor.AltitudeMeters,
                l.Descriptor.Type, l.Descriptor.Terrain, l.Descriptor.BaseNoise,
                l.Descriptor.NoisePerPerson, l.Descriptor.Capacity, l.Descriptor.AllowsPrivacy))
            .ToList();
    }

    /// <summary>Loads the default heightmap grid, or <c>null</c> if none has been saved yet.</summary>
    public TerrainHeightmap? LoadHeightmap()
    {
        RequireOpen();
        return _terrainDb!.LoadHeightmap(DefaultHeightmapId);
    }

    /// <summary>Loads a specific heightmap grid by id (e.g. a tile saved by TerraGen), or
    /// <c>null</c> if no row exists with that id.</summary>
    public TerrainHeightmap? LoadHeightmap(string id)
    {
        RequireOpen();
        return _terrainDb!.LoadHeightmap(id);
    }

    /// <summary>Lists every saved heightmap's metadata (id, position, size) — cheap, doesn't load
    /// any grid's full elevation data. Used by the tile browser to let the designer pick one of
    /// potentially many saved grids (e.g. tiles from a batch planet generator).</summary>
    public IReadOnlyList<TerrainHeightmapSummary> ListHeightmaps()
    {
        RequireOpen();
        return _terrainDb!.ListHeightmaps();
    }

    public void SaveHeightmap(TerrainHeightmap grid)
    {
        RequireOpen();
        _terrainDb!.SaveHeightmap(grid);
    }

    /// <summary>
    /// Inserts a brand-new location authored in the editor — not persisted until <c>Save</c> is
    /// clicked. <paramref name="info"/> carries the full descriptor (position gets sampled fresh
    /// at save time anyway, but the social/physical fields come straight from here).
    /// </summary>
    public void InsertLocation(LocationInfo info)
    {
        RequireOpen();
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
        RequireOpen();
        return _db!.UpdateLocationPosition(locationId, x, y, altitudeMeters);
    }

    public bool UpdateLocationRegion(string locationId, string region)
    {
        RequireOpen();
        return _db!.UpdateLocationRegion(locationId, region);
    }

    /// <summary>Updates an existing location's name and social/physical fields (type, terrain,
    /// noise, capacity, privacy) — used by the location-edit dialog (double-click a marker).</summary>
    public bool UpdateLocationDetails(LocationInfo info)
    {
        RequireOpen();
        return _db!.UpdateLocationDetails(info.Id, info.DisplayName, info.Type, info.Terrain,
            info.BaseNoise, info.NoisePerPerson, info.Capacity, info.AllowsPrivacy);
    }

    public IReadOnlyList<ConnectionInfo> GetConnections()
    {
        RequireOpen();
        return _db!.GetAllConnections()
            .Select(c => new ConnectionInfo(c.FromId, c.ToId, c.DistanceMeters))
            .ToList();
    }

    public bool UpdateConnectionDistance(string fromId, string toId, double distanceMeters)
    {
        RequireOpen();
        return _db!.UpdateConnectionDistance(fromId, toId, distanceMeters);
    }

    /// <summary>Inserts a brand-new connection authored in the editor — not persisted until
    /// <c>Save</c> is clicked. Connections are directed rows, so a bidirectional link between two
    /// locations needs this called once per direction.</summary>
    public void InsertConnection(string fromId, string toId, double distanceMeters)
    {
        RequireOpen();
        _db!.InsertConnection(fromId, toId, distanceMeters);
    }

    /// <summary>Regenerates a seed_data.sql-compatible script from the database's current contents.</summary>
    public string ExportSeedSql()
    {
        RequireOpen();
        return WorldDatabaseExporter.ExportSeedSql(_db!, _terrainDb);
    }

    private void RequireOpen()
    {
        if (_db is null)
            throw new InvalidOperationException("No world database is open. Call Open() first.");
    }

    public void Dispose() => Close();
}
