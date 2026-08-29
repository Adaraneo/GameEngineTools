// WorldDatabaseSeeder.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    using System;

    /// <summary>
    /// Initialises a <see cref="SqliteWorldDatabase"/> from SQL script files
    /// when the database is missing or empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Script resolution</b> (handled by <see cref="SqlScriptLoader"/>):
    /// <list type="number">
    ///   <item>Disk override at <c>SourceFiles\World\SQL\{filename}</c> — used when present.</item>
    ///   <item>Embedded resource fallback inside <c>GameEngineTools.dll</c>.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Execution order:</b>
    /// <list type="number">
    ///   <item><c>schema.sql</c> — always executed (idempotent: IF NOT EXISTS / INSERT OR IGNORE).</item>
    ///   <item><c>seed_data.sql</c> — executed only when <c>Locations</c> table is empty.</item>
    /// </list>
    /// </para>
    /// <para>
    /// To regenerate <c>seed_data.sql</c> from an existing database, use
    /// <see cref="WorldDatabaseExporter.ExportSeedSql"/>.
    /// </para>
    /// </remarks>
    public static class WorldDatabaseSeeder
    {
        #region Script filenames

        /// <summary>DDL script — table and index definitions.</summary>
        private const string SchemaScript = "schema.sql";

        /// <summary>DML script — default world data (locations, objects, connections).</summary>
        private const string SeedDataScript = "seed_data.sql";

        #endregion Script filenames

        #region Public API

        /// <summary>
        /// Ensures the database schema exists and, when empty, populates it
        /// with default world data from SQL script files.
        /// </summary>
        /// <param name="db">Target database to initialise.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> is null.</exception>
        public static void Initialize(SqliteWorldDatabase db)
        {
            ArgumentNullException.ThrowIfNull(db);

            // ── Step 1: Schema (always — idempotent) ──────────────────────────
            var schemaSql = SqlScriptLoader.Load(SchemaScript);
            db.ExecuteScript(schemaSql);

            // Migrate databases created before spatial coordinates existed — CREATE TABLE
            // IF NOT EXISTS above is a no-op on an existing Locations table, so older
            // databases need an explicit ALTER TABLE to gain the X/Y columns.
            db.MigrateLocationCoordinateColumns();

            // ── Step 2: Seed data (only when Locations table is empty) ────────
            // This guard prevents re-seeding on every startup and allows the
            // database to be populated externally (e.g. via the NPC Watcher or
            // a Unity editor tool) without being overwritten.
            if (db.GetAllLocations().Count > 0)
                return;

            var seedSql = SqlScriptLoader.Load(SeedDataScript);
            db.ExecuteScript(seedSql);
        }

        #endregion Public API
    }
}
