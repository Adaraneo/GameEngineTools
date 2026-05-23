// SqliteWorldDatabase.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Globalization;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// Low-level SQLite access layer for the world database.
    /// Maintains a single persistent connection with WAL journal mode.
    /// All public methods are thread-safe via an internal lock.
    /// </summary>
    /// <remarks>
    /// This class owns the <see cref="SqliteConnection"/> lifetime.
    /// Register as a singleton and dispose via DI or application shutdown.
    /// </remarks>
    public sealed class SqliteWorldDatabase : IDisposable
    {
        #region Private state

        private readonly SqliteConnection _connection;
        private readonly object _sync = new();
        private bool _disposed;

        #endregion

        #region Construction

        /// <summary>
        /// Opens a connection to the SQLite database at the given path
        /// and initialises the schema if the database is new.
        /// </summary>
        /// <param name="databasePath">
        /// Path to the <c>.db</c> file. Created automatically if absent.
        /// </param>
        public SqliteWorldDatabase(string databasePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

            _connection = new SqliteConnection($"Data Source={databasePath}");
            _connection.Open();

            // WAL mode: multiple readers + one writer, no full-table locks.
            Execute("PRAGMA journal_mode=WAL;");
            Execute("PRAGMA foreign_keys=ON;");
            Execute(WorldDatabaseSchema.CreateTables);
        }

        #endregion

        #region World Object queries

        /// <summary>
        /// Returns all available (not held, not consumed) objects at the given location.
        /// </summary>
        public IReadOnlyList<WorldObject> GetObjectsAt(string locationId)
        {
            const string sql = """
                SELECT Id, DisplayName, Category, LocationId,
                       HeatSignature, AmbientNoise, BlocksLineOfSight,
                       IsAvailable, IsPickable, WeightGrams, ItemKind,
                       Respawns, RespawnMinutes
                FROM WorldObjects
                WHERE LocationId = @loc
                  AND HeldBy IS NULL
                  AND ConsumedAt IS NULL
                """;

            lock (_sync)
            {
                var objects = ReadObjects(sql, ("@loc", locationId));
                return EnrichWithAffordances(objects);
            }
        }

        /// <summary>
        /// Returns all objects across all locations, including held and consumed.
        /// Used by foraging queries and diagnostics.
        /// </summary>
        public IReadOnlyList<WorldObject> GetAllObjects()
        {
            const string sql = """
                SELECT Id, DisplayName, Category, LocationId,
                       HeatSignature, AmbientNoise, BlocksLineOfSight,
                       IsAvailable, IsPickable, WeightGrams, ItemKind,
                       Respawns, RespawnMinutes
                FROM WorldObjects
                """;

            lock (_sync)
            {
                var objects = ReadObjects(sql);
                return EnrichWithAffordances(objects);
            }
        }

        /// <summary>
        /// Finds a single object by ID across all locations.
        /// Returns <c>null</c> if not found.
        /// </summary>
        public WorldObject? FindObject(string objectId)
        {
            const string sql = """
                SELECT Id, DisplayName, Category, LocationId,
                       HeatSignature, AmbientNoise, BlocksLineOfSight,
                       IsAvailable, IsPickable, WeightGrams, ItemKind,
                       Respawns, RespawnMinutes
                FROM WorldObjects
                WHERE Id = @id
                """;

            lock (_sync)
            {
                var objects = ReadObjects(sql, ("@id", objectId));
                if (objects.Count == 0) return null;
                return EnrichWithAffordances(objects)[0];
            }
        }

        /// <summary>
        /// Returns all objects currently held by the specified character.
        /// </summary>
        public IReadOnlyList<WorldObject> GetHeldBy(HumanId holder)
        {
            const string sql = """
                SELECT Id, DisplayName, Category, LocationId,
                       HeatSignature, AmbientNoise, BlocksLineOfSight,
                       IsAvailable, IsPickable, WeightGrams, ItemKind,
                       Respawns, RespawnMinutes
                FROM WorldObjects
                WHERE HeldBy = @holder
                """;

            lock (_sync)
            {
                var objects = ReadObjects(sql, ("@holder", holder.Value.ToString()));
                return EnrichWithAffordances(objects);
            }
        }

        /// <summary>
        /// Inserts or replaces a world object and its affordances and nutritional profile.
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
                    ("@ticks", now.WorldTicks),
                    ("@id", objectId),
                    ("@loc", locationId)) > 0;
        }

        /// <summary>
        /// Clears the held and consumed state, making the object available again.
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
        /// Permanently deletes an object from the database.
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

        #endregion

        #region Location + Connection queries

        /// <summary>
        /// Returns all registered location descriptors.
        /// </summary>
        public IReadOnlyList<(LocationDescriptor Descriptor, string Region)> GetAllLocations()
        {
            const string sql = """
                SELECT Id, DisplayName, Type, Region,
                       BaseNoise, NoisePerPerson, Capacity, AllowsPrivacy,
                       Terrain, DangerLevel, AllowsPickup
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
                        AllowsPickup: reader.GetInt32(10) != 0);

                    results.Add((descriptor, reader.GetString(3)));
                }

                return results;
            }
        }

        /// <summary>
        /// Returns all connections in the world adjacency graph.
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

        #endregion

        #region Seed helpers (used by WorldDatabaseSeeder)

        /// <summary>
        /// Inserts a location row. Used during initial seeding from CSV.
        /// </summary>
        public void InsertLocation(LocationDescriptor d, string region)
        {
            const string sql = """
                INSERT OR IGNORE INTO Locations
                    (Id, DisplayName, Type, Region, BaseNoise, NoisePerPerson,
                     Capacity, AllowsPrivacy, Terrain, DangerLevel, AllowsPickup)
                VALUES
                    (@id, @name, @type, @region, @noise, @npp,
                     @cap, @priv, @terrain, @danger, @pickup)
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
                    ("@pickup", d.AllowsPickup ? 1 : 0));
        }

        /// <summary>
        /// Inserts a connection row. Used during initial seeding from CSV.
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

        #endregion

        #region IDisposable

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _connection.Dispose();
        }

        #endregion

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

        #endregion

        #region Private helpers — object reading

        /// <summary>
        /// Executes a WorldObjects SELECT and returns raw records (no affordances yet).
        /// </summary>
        private List<WorldObject> ReadObjects(string sql,
            params (string Name, object? Value)[] parameters)
        {
            var results = new List<WorldObject>();

            using var cmd = CreateCommand(sql, parameters);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                results.Add(new WorldObject
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
                    // Affordances and NutritionalProfile populated by EnrichWithAffordances()
                });
            }

            return results;
        }

        /// <summary>
        /// Loads affordances and nutritional profiles for a batch of objects,
        /// then returns enriched copies. Batched to avoid N+1 queries.
        /// </summary>
        private IReadOnlyList<WorldObject> EnrichWithAffordances(List<WorldObject> objects)
        {
            if (objects.Count == 0) return Array.Empty<WorldObject>();

            // Build IN clause: (@p0, @p1, @p2, ...)
            var ids = objects.Select(o => o.Id).ToList();
            var inClause = string.Join(", ", ids.Select((_, i) => $"@p{i}"));
            var parameters = ids
                .Select((id, i) => ($"@p{i}", (object?)id))
                .ToArray();

            // Load affordances for all objects in one query.
            var affordanceMap = new Dictionary<string, ImmutableArray<WorldObjectAffordance>.Builder>(
                StringComparer.Ordinal);

            foreach (var id in ids)
                affordanceMap[id] = ImmutableArray.CreateBuilder<WorldObjectAffordance>();

            using (var cmd = CreateCommand(
                $"SELECT ObjectId, Type, Satisfaction FROM Affordances WHERE ObjectId IN ({inClause})",
                parameters))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var objectId = reader.GetString(0);
                    if (affordanceMap.TryGetValue(objectId, out var builder))
                    {
                        builder.Add(new WorldObjectAffordance(
                            Enum.Parse<AffordanceType>(reader.GetString(1), ignoreCase: true),
                            reader.GetDouble(2)));
                    }
                }
            }

            // Load nutritional profiles.
            var profileMap = new Dictionary<string, NutritionalProfile>(StringComparer.Ordinal);

            using (var cmd = CreateCommand(
                $"""
                SELECT ObjectId, CalorieGain, ProteinGain, IronGain, VitaminDGain, HydrationGain
                FROM NutritionalProfiles
                WHERE ObjectId IN ({inClause})
                """,
                parameters))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    profileMap[reader.GetString(0)] = new NutritionalProfile(
                        CalorieGain: reader.IsDBNull(1) ? null : reader.GetDouble(1),
                        ProteinGain: reader.IsDBNull(2) ? null : reader.GetDouble(2),
                        IronGain: reader.IsDBNull(3) ? null : reader.GetDouble(3),
                        VitaminDGain: reader.IsDBNull(4) ? null : reader.GetDouble(4),
                        HydrationGain: reader.IsDBNull(5) ? null : reader.GetDouble(5));
                }
            }

            // Merge.
            return objects.Select(o => o with
            {
                Affordances = affordanceMap[o.Id].ToImmutable(),
                NutritionalProfile = profileMap.GetValueOrDefault(o.Id)
            }).ToList();
        }

        private void UpsertObject(WorldObject obj)
        {
            const string sql = """
                INSERT OR REPLACE INTO WorldObjects
                    (Id, DisplayName, Category, LocationId, HeatSignature, AmbientNoise,
                     BlocksLineOfSight, IsAvailable, IsPickable, WeightGrams, ItemKind,
                     Respawns, RespawnMinutes)
                VALUES
                    (@id, @name, @cat, @loc, @heat, @noise,
                     @blos, @avail, @pick, @weight, @kind,
                     @resp, @respMin)
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
                ("@respMin", obj.RespawnMinutes));
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
                    (ObjectId, CalorieGain, ProteinGain, IronGain, VitaminDGain, HydrationGain)
                VALUES
                    (@id, @cal, @prot, @iron, @vitd, @hydra)
                """;

            ExecuteNonQuery(sql,
                ("@id", objectId),
                ("@cal", (object?)profile.CalorieGain ?? DBNull.Value),
                ("@prot", (object?)profile.ProteinGain ?? DBNull.Value),
                ("@iron", (object?)profile.IronGain ?? DBNull.Value),
                ("@vitd", (object?)profile.VitaminDGain ?? DBNull.Value),
                ("@hydra", (object?)profile.HydrationGain ?? DBNull.Value));
        }

        #endregion
    }
}
