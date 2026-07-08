// SqliteWorldDatabase.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Data.Sqlite;
    using static GameEngineTools.World.Objects.WorldObjectWriteBuffer;

    /// <summary>
    /// Raw database row for a social norm — used by <see cref="SqliteWorldDatabase"/>
    /// and <see cref="SqliteSocialNormProvider"/>.
    /// </summary>
    public sealed record SocialNormRow(
        string Id,
        string DisplayName,
        string Kind,
        double Severity,
        double EnforcementProbability,
        string? RelationalModel,
        string? CultureId,
        int? ValidFromYear,
        int? ValidToYear);

    /// <summary>
    /// Low-level SQLite access layer for the world database.
    /// Maintains a single persistent connection with WAL journal mode for
    /// concurrent read performance and single-writer safety.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Register as a singleton and dispose via the application host lifetime.
    /// All public methods are thread-safe via an internal synchronisation lock.
    /// </para>
    /// <para>
    /// This class is an infrastructure detail — consume it only through
    /// <see cref="SqliteWorldObjectProvider"/> and <see cref="SqliteWorldMapLoader"/>,
    /// never directly from simulation engines.
    /// </para>
    /// </remarks>
    public sealed class SqliteWorldDatabase : IDisposable
    {
        #region Private state

        /// <summary>Persistent connection kept open for the application lifetime.</summary>
        private readonly SqliteConnection _connection;

        /// <summary>Guards all query execution against concurrent access.</summary>
        private readonly object _sync = new();

        private bool _disposed;

        #endregion Private state

        #region Construction

        /// <summary>
        /// Opens a connection to the SQLite database at the given path and
        /// initialises the schema if the database is new.
        /// </summary>
        /// <param name="databasePath">
        /// Filesystem path to the <c>.db</c> file. Created automatically when absent.
        /// </param>
        public SqliteWorldDatabase(string databasePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

            _connection = new SqliteConnection($"Data Source={databasePath}");
            _connection.Open();

            // WAL mode: multiple concurrent readers + one writer, no full-table locks.
            Execute("PRAGMA journal_mode=WAL;");
            Execute("PRAGMA foreign_keys=ON;");
        }

        #endregion Construction

        #region World Object queries

        /// <summary>
        /// Returns all available (not held, not consumed) objects at the given location.
        /// This is the hot-path query — called every behavior tick for each character.
        /// </summary>
        public IReadOnlyList<WorldObject> GetObjectsAt(string locationId)
        {
            const string sql = """
                SELECT Id, DisplayName, Category, LocationId,
                       HeatSignature, AmbientNoise, BlocksLineOfSight,
                       IsAvailable, IsPickable, WeightGrams, ItemKind,
                       Respawns, RespawnMinutes, Price, ShopId
                FROM WorldObjects
                WHERE LocationId = @loc
                  AND HeldBy IS NULL
                  AND ConsumedAt IS NULL
                """;

            lock (_sync)
                return EnrichWithAffordances(ReadObjects(sql, ("@loc", locationId)));
        }

        /// <summary>
        /// Returns ALL objects at the given location — including held and consumed ones.
        /// Used exclusively by <see cref="ObjectRespawnScheduler"/> to inspect respawn timers.
        /// Unlike <see cref="GetObjectsAt"/>, this does not filter by availability.
        /// </summary>
        public IReadOnlyList<WorldObject> GetAllObjectsAt(string locationId)
        {
            // Selects two extra runtime columns after Price/ShopId: HeldBy (15) and ConsumedAt (16).
            const string sql = """
                SELECT Id, DisplayName, Category, LocationId,
                       HeatSignature, AmbientNoise, BlocksLineOfSight,
                       IsAvailable, IsPickable, WeightGrams, ItemKind,
                       Respawns, RespawnMinutes, Price, ShopId, HeldBy, ConsumedAt
                FROM WorldObjects
                WHERE LocationId = @loc
                """;

            lock (_sync)
                return EnrichWithAffordances(ReadObjects(sql, ("@loc", locationId),
                    includeRuntimeState: true));
        }

        /// <summary>
        /// Returns all objects across all locations, including held and consumed.
        /// Used by foraging queries (<see cref="GameEngineTools.Characters.Engines.Behavior.Needs.ContingencySearchEngine"/>) and diagnostics.
        /// </summary>
        public IReadOnlyList<WorldObject> GetAllObjects()
        {
            const string sql = """
                SELECT Id, DisplayName, Category, LocationId,
                       HeatSignature, AmbientNoise, BlocksLineOfSight,
                       IsAvailable, IsPickable, WeightGrams, ItemKind,
                       Respawns, RespawnMinutes, Price, ShopId
                FROM WorldObjects
                """;

            lock (_sync)
                return EnrichWithAffordances(ReadObjects(sql));
        }

        /// <summary>
        /// Returns all distinct location IDs that have at least one registered object.
        /// Used by <see cref="ObjectRespawnScheduler"/> to enumerate locations without
        /// a full table scan.
        /// </summary>
        public IReadOnlyList<string> GetKnownLocationIds()
        {
            const string sql = "SELECT DISTINCT LocationId FROM WorldObjects";

            lock (_sync)
            {
                var ids = new List<string>();
                using var cmd = CreateCommand(sql);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    ids.Add(reader.GetString(0));
                return ids;
            }
        }

        /// <summary>
        /// Finds a single object by its unique ID across all locations.
        /// Returns <c>null</c> if no object with the given ID exists.
        /// </summary>
        public WorldObject? FindObject(string objectId)
        {
            const string sql = """
                SELECT Id, DisplayName, Category, LocationId,
                       HeatSignature, AmbientNoise, BlocksLineOfSight,
                       IsAvailable, IsPickable, WeightGrams, ItemKind,
                       Respawns, RespawnMinutes, Price, ShopId
                FROM WorldObjects
                WHERE Id = @id
                """;

            lock (_sync)
            {
                var objects = EnrichWithAffordances(ReadObjects(sql, ("@id", objectId)));
                return objects.Count == 0 ? null : objects[0];
            }
        }

        /// <summary>
        /// Returns all objects currently held by the specified character, across all locations.
        /// </summary>
        public IReadOnlyList<WorldObject> GetHeldBy(HumanId holder)
        {
            const string sql = """
                SELECT Id, DisplayName, Category, LocationId,
                       HeatSignature, AmbientNoise, BlocksLineOfSight,
                       IsAvailable, IsPickable, WeightGrams, ItemKind,
                       Respawns, RespawnMinutes, Price, ShopId
                FROM WorldObjects
                WHERE HeldBy = @holder
                """;

            lock (_sync)
                return EnrichWithAffordances(
                    ReadObjects(sql, ("@holder", holder.Value.ToString())));
        }

        /// <summary>
        /// Inserts or replaces a world object together with its affordances and
        /// nutritional profile. All three are written in a single transaction.
        /// </summary>
        public void AddObject(WorldObject obj)
        {
            lock (_sync)
            {
                using var tx = _connection.BeginTransaction();
                UpsertObject(obj);
                DeleteAffordances(obj.Id);
                InsertAffordances(obj.Id, obj.Affordances);
                UpsertNutritionalProfile(obj.Id, obj.NutritionalProfile);
                tx.Commit();
            }
        }

        /// <summary>
        /// Marks an object as consumed at the given simulation time.
        /// The object will be hidden from <see cref="GetObjectsAt"/> until restored.
        /// </summary>
        public bool ConsumeObject(string locationId, string objectId, WDateTime now)
        {
            const string sql = """
                UPDATE WorldObjects
                SET ConsumedAt = @ticks
                WHERE Id = @id AND LocationId = @loc
                """;

            lock (_sync)
                return ExecuteNonQuery(sql,
                    // WDateTime stores time as WorldTicks — NOT .Ticks (which doesn't exist).
                    ("@ticks", now.WorldTicks),
                    ("@id", objectId),
                    ("@loc", locationId)) > 0;
        }

        /// <summary>
        /// Clears held and consumed state, restoring the object to full availability.
        /// </summary>
        public bool RestoreObject(string locationId, string objectId)
        {
            const string sql = """
                UPDATE WorldObjects
                SET HeldBy = NULL, ConsumedAt = NULL
                WHERE Id = @id AND LocationId = @loc
                """;

            lock (_sync)
                return ExecuteNonQuery(sql,
                    ("@id", objectId),
                    ("@loc", locationId)) > 0;
        }

        /// <summary>
        /// Permanently deletes an object from the database (no respawn possible).
        /// Cascades to <c>Affordances</c> and <c>NutritionalProfiles</c> via FK.
        /// </summary>
        public bool RemoveObject(string locationId, string objectId)
        {
            const string sql = """
                DELETE FROM WorldObjects
                WHERE Id = @id AND LocationId = @loc
                """;

            lock (_sync)
                return ExecuteNonQuery(sql,
                    ("@id", objectId),
                    ("@loc", locationId)) > 0;
        }

        /// <summary>
        /// Assigns or clears the character currently holding this object.
        /// A held object is excluded from <see cref="GetObjectsAt"/>.
        /// </summary>
        public bool SetHeldBy(string locationId, string objectId, HumanId? holder)
        {
            const string sql = """
                UPDATE WorldObjects
                SET HeldBy = @holder
                WHERE Id = @id AND LocationId = @loc
                """;

            lock (_sync)
                return ExecuteNonQuery(sql,
                    ("@holder", (object?)holder?.Value.ToString() ?? DBNull.Value),
                    ("@id", objectId),
                    ("@loc", locationId)) > 0;
        }

        #endregion World Object queries

        /// <summary>
        /// Applies a batch of world object mutations in a single SQLite transaction.
        /// Called by <see cref="WorldObjectWriteBuffer.Flush"/> at the end of each substep.
        /// </summary>
        /// <param name="mutations">
        /// Mutations to apply. Last-write-wins per object is guaranteed by the caller
        /// (<see cref="WorldObjectWriteBuffer"/> uses a dictionary keyed by object ID).
        /// </param>
        public void ApplyBatch(IReadOnlyList<WorldObjectWriteBuffer.PendingMutation> mutations)
        {
            if (mutations.Count == 0)
                return;

            const string consumeSql = """
        UPDATE WorldObjects
        SET ConsumedAt = @ticks
        WHERE Id = @id AND LocationId = @loc
        """;

            const string restoreSql = """
        UPDATE WorldObjects
        SET HeldBy = NULL, ConsumedAt = NULL
        WHERE Id = @id AND LocationId = @loc
        """;

            const string heldBySql = """
        UPDATE WorldObjects
        SET HeldBy = @holder
        WHERE Id = @id AND LocationId = @loc
        """;

            const string removeSql = """
        DELETE FROM WorldObjects
        WHERE Id = @id AND LocationId = @loc
        """;

            lock (_sync)
            {
                using var tx = _connection.BeginTransaction();

                foreach (var m in mutations)
                {
                    var sql = m.Kind switch
                    {
                        MutationKind.Consumed => consumeSql,
                        MutationKind.Restored => restoreSql,
                        MutationKind.HeldBy => heldBySql,
                        MutationKind.Removed => removeSql,
                        _ => throw new InvalidOperationException($"Unknown MutationKind: {m.Kind}")
                    };

                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = sql;
                    cmd.Parameters.AddWithValue("@id", m.ObjectId);
                    cmd.Parameters.AddWithValue("@loc", m.LocationId);

                    if (m.Kind == MutationKind.Consumed)
                        cmd.Parameters.AddWithValue("@ticks", m.ConsumedAtTicks!.Value);

                    if (m.Kind == MutationKind.HeldBy)
                        cmd.Parameters.AddWithValue("@holder",
                            m.HolderGuid is null ? (object)DBNull.Value : m.HolderGuid);

                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
        }

        #region Location + Connection queries

        /// <summary>
        /// Returns all registered location descriptors together with their region name.
        /// </summary>
        public IReadOnlyList<(LocationDescriptor Descriptor, string Region)> GetAllLocations()
        {
            const string sql = """
                SELECT Id, DisplayName, Type, Region,
                       BaseNoise, NoisePerPerson, Capacity, AllowsPrivacy,
                       Terrain, DangerLevel, AllowsPickup, NormId
                FROM Locations
                """;

            lock (_sync)
            {
                var results = new List<(LocationDescriptor, string)>();

                using var cmd = CreateCommand(sql);
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var descriptor = new LocationDescriptor(
                        Id: reader.GetString(0),
                        DisplayName: reader.GetString(1),
                        Type: Enum.Parse<LocationType>(reader.GetString(2), ignoreCase: true),
                        BaseNoise: reader.GetDouble(4),
                        NoisePerPerson: reader.GetDouble(5),
                        Capacity: reader.GetInt32(6),
                        AllowsPrivacy: reader.GetInt32(7) != 0,
                        Terrain: Enum.Parse<TerrainType>(reader.GetString(8), ignoreCase: true),
                        DangerLevel: reader.GetDouble(9),
                        AllowsPickup: reader.GetInt32(10) != 0,
                        NormId: reader.IsDBNull(11) ? null : reader.GetString(11));

                    results.Add((descriptor, reader.GetString(3)));
                }

                return results;
            }
        }

        /// <summary>
        /// Returns all adjacency connections in the world graph.
        /// </summary>
        public IReadOnlyList<(string FromId, string ToId, double DistanceMeters)> GetAllConnections()
        {
            const string sql = "SELECT FromId, ToId, DistanceMeters FROM Connections";

            lock (_sync)
            {
                var results = new List<(string, string, double)>();

                using var cmd = CreateCommand(sql);
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                    results.Add((reader.GetString(0), reader.GetString(1), reader.GetDouble(2)));

                return results;
            }
        }

        #endregion Location + Connection queries

        #region Social Norm queries

        /// <summary>
        /// Inserts a social norm row. Uses INSERT OR IGNORE — safe to call repeatedly.
        /// </summary>
        public void InsertSocialNorm(SocialNormRow norm)
        {
            const string sql = """
                INSERT OR IGNORE INTO SocialNorms
                    (Id, DisplayName, Kind, Severity, EnforcementProbability,
                     RelationalModel, CultureId, ValidFromYear, ValidToYear)
                VALUES
                    (@id, @name, @kind, @sev, @enf, @rm, @culture, @fromYear, @toYear)
                """;
            lock (_sync)
                ExecuteNonQuery(sql,
                    ("@id", norm.Id),
                    ("@name", norm.DisplayName),
                    ("@kind", norm.Kind),
                    ("@sev", norm.Severity),
                    ("@enf", norm.EnforcementProbability),
                    ("@rm", (object?)norm.RelationalModel ?? DBNull.Value),
                    ("@culture", (object?)norm.CultureId ?? DBNull.Value),
                    ("@fromYear", (object?)norm.ValidFromYear ?? DBNull.Value),
                    ("@toYear", (object?)norm.ValidToYear ?? DBNull.Value));
        }

        /// <summary>Returns all social norms in the database.</summary>
        public IReadOnlyList<SocialNormRow> GetAllSocialNorms()
        {
            const string sql = """
                SELECT Id, DisplayName, Kind, Severity, EnforcementProbability,
                       RelationalModel, CultureId, ValidFromYear, ValidToYear
                FROM SocialNorms
                """;
            lock (_sync)
            {
                var results = new List<SocialNormRow>();
                using var cmd = CreateCommand(sql);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    results.Add(new SocialNormRow(
                        Id: reader.GetString(0),
                        DisplayName: reader.GetString(1),
                        Kind: reader.GetString(2),
                        Severity: reader.GetDouble(3),
                        EnforcementProbability: reader.GetDouble(4),
                        RelationalModel: reader.IsDBNull(5) ? null : reader.GetString(5),
                        CultureId: reader.IsDBNull(6) ? null : reader.GetString(6),
                        ValidFromYear: reader.IsDBNull(7) ? null : reader.GetInt32(7),
                        ValidToYear: reader.IsDBNull(8) ? null : reader.GetInt32(8)));
                return results;
            }
        }

        /// <summary>Returns a single social norm by id, or <c>null</c> if not found.</summary>
        public SocialNormRow? GetSocialNorm(string id)
        {
            const string sql = """
                SELECT Id, DisplayName, Kind, Severity, EnforcementProbability,
                       RelationalModel, CultureId, ValidFromYear, ValidToYear
                FROM SocialNorms WHERE Id = @id
                """;
            lock (_sync)
            {
                using var cmd = CreateCommand(sql, ("@id", id));
                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return null;
                return new SocialNormRow(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetDouble(3), reader.GetDouble(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8));
            }
        }

        #endregion Social Norm queries

        #region Seed helpers (used by WorldDatabaseSeeder)

        /// <summary>
        /// Inserts a location row. Uses INSERT OR IGNORE — safe to call repeatedly.
        /// </summary>
        public void InsertLocation(LocationDescriptor d, string region)
        {
            const string sql = """
                INSERT OR IGNORE INTO Locations
                    (Id, DisplayName, Type, Region, BaseNoise, NoisePerPerson,
                     Capacity, AllowsPrivacy, Terrain, DangerLevel, AllowsPickup, NormId)
                VALUES
                    (@id, @name, @type, @region, @noise, @npp,
                     @cap, @priv, @terrain, @danger, @pickup, @normId)
                """;

            lock (_sync)
                ExecuteNonQuery(sql,
                    ("@id", d.Id),
                    ("@name", d.DisplayName),
                    ("@type", d.Type.ToString()),
                    ("@region", region),
                    ("@noise", d.BaseNoise),
                    ("@npp", d.NoisePerPerson),
                    ("@cap", d.Capacity),
                    ("@priv", d.AllowsPrivacy ? 1 : 0),
                    ("@terrain", d.Terrain.ToString()),
                    ("@danger", d.DangerLevel),
                    ("@pickup", d.AllowsPickup ? 1 : 0),
                    ("@normId", (object?)d.NormId ?? DBNull.Value));
        }

        /// <summary>
        /// Inserts a connection row. Uses INSERT OR IGNORE — safe to call repeatedly.
        /// </summary>
        public void InsertConnection(string fromId, string toId, double distanceMeters)
        {
            const string sql = """
                INSERT OR IGNORE INTO Connections (FromId, ToId, DistanceMeters)
                VALUES (@from, @to, @dist)
                """;

            lock (_sync)
                ExecuteNonQuery(sql,
                    ("@from", fromId),
                    ("@to", toId),
                    ("@dist", distanceMeters));
        }

        #endregion Seed helpers (used by WorldDatabaseSeeder)

        #region IDisposable

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _connection.Dispose();
        }

        #endregion IDisposable

        #region Private helpers — query execution

        private void Execute(string sql)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private int ExecuteNonQuery(string sql, params (string Name, object? Value)[] parameters)
        {
            using var cmd = CreateCommand(sql, parameters);
            return cmd.ExecuteNonQuery();
        }

        private SqliteCommand CreateCommand(string sql,
            params (string Name, object? Value)[] parameters)
        {
            var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;

            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

            return cmd;
        }

        /// <summary>
        /// Executes a multi-statement SQL script against the database connection.
        /// Used by <see cref="WorldDatabaseSeeder"/> to apply schema and seed data.
        /// </summary>
        /// <param name="sql">
        /// Full SQL script content. May contain multiple statements separated by
        /// semicolons and SQL comments (<c>--</c>).
        /// </param>
        /// <remarks>
        /// Each statement is executed in sequence. If any statement fails, the
        /// exception propagates immediately — no partial rollback.
        /// For atomic multi-statement operations, wrap in a transaction externally.
        /// </remarks>
        public void ExecuteScript(string sql)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sql);

            lock (_sync)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        #endregion Private helpers — query execution

        #region Private helpers — object reading

        /// <summary>
        /// Executes a WorldObjects SELECT and returns raw records without affordances.
        /// </summary>
        /// <param name="sql">SELECT statement targeting WorldObjects columns.</param>
        /// <param name="parameters">Named query parameters.</param>
        /// <param name="includeRuntimeState">
        /// <c>true</c> when the SELECT includes columns 13 (HeldBy) and 14 (ConsumedAt).
        /// Only <see cref="GetAllObjectsAt"/> passes <c>true</c> — standard queries
        /// do not return these columns to avoid unnecessary data transfer.
        /// </param>
        private List<WorldObject> ReadObjects(string sql,
            (string Name, object? Value)[] parameters,
            bool includeRuntimeState = false)
        {
            var results = new List<WorldObject>();

            using var cmd = CreateCommand(sql, parameters);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var obj = new WorldObject
                {
                    Id = reader.GetString(0),
                    DisplayName = reader.GetString(1),
                    Category = Enum.Parse<WorldObjectCategory>(reader.GetString(2), ignoreCase: true),
                    LocationId = reader.GetString(3),
                    HeatSignature = reader.GetDouble(4),
                    AmbientNoise = reader.GetDouble(5),
                    BlocksLineOfSight = reader.GetInt32(6) != 0,
                    IsAvailable = reader.GetInt32(7) != 0,
                    IsPickable = reader.GetInt32(8) != 0,
                    WeightGrams = reader.GetInt32(9),
                    ItemKind = Enum.Parse<PickupItemKind>(reader.GetString(10), ignoreCase: true),
                    Respawns = reader.GetInt32(11) != 0,
                    RespawnMinutes = reader.GetInt32(12),
                    // Column 13 = Price (nullable REAL), 14 = ShopId (nullable TEXT) — food-economy Tier 2.
                    Price = reader.IsDBNull(13) ? null : reader.GetDouble(13),
                    ShopId = reader.IsDBNull(14) ? null : reader.GetString(14),
                };

                if (includeRuntimeState)
                {
                    // Column 15 = HeldBy (nullable TEXT — stored as GUID string)
                    // Column 16 = ConsumedAt (nullable INTEGER — stored as WorldTicks)
                    var heldByText = reader.IsDBNull(15) ? null : reader.GetString(15);
                    var consumedAtTicks = reader.IsDBNull(16) ? (long?)null : reader.GetInt64(16);

                    obj = obj with
                    {
                        // WDateTime is constructed directly from WorldTicks — no static FromTicks().
                        HeldBy = heldByText is null ? null : new HumanId(Guid.Parse(heldByText)),
                        ConsumedAt = consumedAtTicks is null
                            ? null
                            : new WDateTime(consumedAtTicks.Value)
                    };
                }

                results.Add(obj);
            }

            return results;
        }

        // Convenience overloads to keep call sites clean.

        private List<WorldObject> ReadObjects(string sql,
            params (string Name, object? Value)[] parameters)
            => ReadObjects(sql, parameters, includeRuntimeState: false);

        private List<WorldObject> ReadObjects(string sql,
            (string Name, object? Value) parameter,
            bool includeRuntimeState = false)
            => ReadObjects(sql, new[] { parameter }, includeRuntimeState);

        /// <summary>
        /// Loads affordances and nutritional profiles for a batch of objects in two
        /// bulk queries (not N+1) and returns enriched copies.
        /// </summary>
        private IReadOnlyList<WorldObject> EnrichWithAffordances(List<WorldObject> objects)
        {
            if (objects.Count == 0)
                return Array.Empty<WorldObject>();

            // Build a shared IN clause for both child queries.
            var ids = objects.Select(o => o.Id).ToList();
            var inClause = string.Join(", ", ids.Select((_, i) => $"@p{i}"));
            var inParams = ids.Select((id, i) => ($"@p{i}", (object?)id)).ToArray();

            // ── Load affordances in one query ─────────────────────────────────
            var affordanceMap = ids.ToDictionary(
                id => id,
                _ => ImmutableArray.CreateBuilder<WorldObjectAffordance>(),
                StringComparer.Ordinal);

            using (var cmd = CreateCommand(
                $"SELECT ObjectId, Type, Satisfaction FROM Affordances WHERE ObjectId IN ({inClause})",
                inParams))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (affordanceMap.TryGetValue(reader.GetString(0), out var builder))
                    {
                        builder.Add(new WorldObjectAffordance(
                            Enum.Parse<AffordanceType>(reader.GetString(1), ignoreCase: true),
                            reader.GetDouble(2)));
                    }
                }
            }

            // ── Load nutritional profiles in one query ────────────────────────
            var profileMap = new Dictionary<string, NutritionalProfile>(StringComparer.Ordinal);

            using (var cmd = CreateCommand(
                $"""
                SELECT ObjectId, CalorieGain, ProteinGain, IronGain, VitaminDGain, HydrationGain,
                       HemeIronFraction, VitaminCMilligrams
                FROM NutritionalProfiles
                WHERE ObjectId IN ({inClause})
                """,
                inParams))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    profileMap[reader.GetString(0)] = new NutritionalProfile(
                        CalorieGain: reader.IsDBNull(1) ? null : reader.GetDouble(1),
                        ProteinGain: reader.IsDBNull(2) ? null : reader.GetDouble(2),
                        IronGain: reader.IsDBNull(3) ? null : reader.GetDouble(3),
                        VitaminDGain: reader.IsDBNull(4) ? null : reader.GetDouble(4),
                        HydrationGain: reader.IsDBNull(5) ? null : reader.GetDouble(5),
                        HemeIronFraction: reader.IsDBNull(6) ? null : reader.GetDouble(6),
                        VitaminCMilligrams: reader.IsDBNull(7) ? null : reader.GetDouble(7));
                }
            }

            // ── Merge results into enriched records ───────────────────────────
            return objects
                .Select(o => o with
                {
                    Affordances = affordanceMap[o.Id].ToImmutable(),
                    NutritionalProfile = profileMap.GetValueOrDefault(o.Id)
                })
                .ToList();
        }

        #endregion Private helpers — object reading

        #region Private helpers — write operations

        private void UpsertObject(WorldObject obj)
        {
            const string sql = """
                INSERT OR REPLACE INTO WorldObjects
                    (Id, DisplayName, Category, LocationId, HeatSignature, AmbientNoise,
                     BlocksLineOfSight, IsAvailable, IsPickable, WeightGrams, ItemKind,
                     Respawns, RespawnMinutes, Price, ShopId, HeldBy)
                VALUES
                    (@id, @name, @cat, @loc, @heat, @noise,
                     @blos, @avail, @pick, @weight, @kind,
                     @resp, @respMin, @price, @shopId, @heldBy)
                """;

            ExecuteNonQuery(sql,
                ("@id", obj.Id),
                ("@name", obj.DisplayName),
                ("@cat", obj.Category.ToString()),
                ("@loc", obj.LocationId),
                ("@heat", obj.HeatSignature),
                ("@noise", obj.AmbientNoise),
                ("@blos", obj.BlocksLineOfSight ? 1 : 0),
                ("@avail", obj.IsAvailable ? 1 : 0),
                ("@pick", obj.IsPickable ? 1 : 0),
                ("@weight", obj.WeightGrams),
                ("@kind", obj.ItemKind.ToString()),
                ("@resp", obj.Respawns ? 1 : 0),
                ("@respMin", obj.RespawnMinutes),
                ("@price", (object?)obj.Price ?? DBNull.Value),
                ("@shopId", (object?)obj.ShopId ?? DBNull.Value),
                ("@heldBy", (object?)obj.HeldBy?.Value.ToString() ?? DBNull.Value));
        }

        private void DeleteAffordances(string objectId)
            => ExecuteNonQuery(
                "DELETE FROM Affordances WHERE ObjectId = @id",
                ("@id", objectId));

        private void InsertAffordances(
            string objectId,
            ImmutableArray<WorldObjectAffordance> affordances)
        {
            if (affordances.IsEmpty) return;

            const string sql = """
                INSERT INTO Affordances (ObjectId, Type, Satisfaction)
                VALUES (@id, @type, @sat)
                """;

            foreach (var a in affordances)
                ExecuteNonQuery(sql,
                    ("@id", objectId),
                    ("@type", a.Type.ToString()),
                    ("@sat", a.Satisfaction));
        }

        private void UpsertNutritionalProfile(string objectId, NutritionalProfile? profile)
        {
            if (profile is null) return;

            const string sql = """
                INSERT OR REPLACE INTO NutritionalProfiles
                    (ObjectId, CalorieGain, ProteinGain, IronGain, VitaminDGain, HydrationGain,
                     HemeIronFraction, VitaminCMilligrams)
                VALUES
                    (@id, @cal, @prot, @iron, @vitd, @hydra, @heme, @vitc)
                """;

            ExecuteNonQuery(sql,
                ("@id", objectId),
                ("@cal", (object?)profile.CalorieGain ?? DBNull.Value),
                ("@prot", (object?)profile.ProteinGain ?? DBNull.Value),
                ("@iron", (object?)profile.IronGain ?? DBNull.Value),
                ("@vitd", (object?)profile.VitaminDGain ?? DBNull.Value),
                ("@hydra", (object?)profile.HydrationGain ?? DBNull.Value),
                ("@heme", (object?)profile.HemeIronFraction ?? DBNull.Value),
                ("@vitc", (object?)profile.VitaminCMilligrams ?? DBNull.Value));
        }

        #endregion Private helpers — write operations
    }
}
