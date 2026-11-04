// SchemaMigrator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>One schema step: bring a database at <c>Version − 1</c> up to <c>Version</c>. Steps must be idempotent.</summary>
    internal sealed record SchemaMigration(int Version, string Description, Action<SqliteWorldDatabase> Apply);

    /// <summary>
    /// Versions the SQLite files with <c>PRAGMA user_version</c> and applies the missing migrations in order.
    /// A file written by a newer build is refused rather than silently misread.
    /// </summary>
    internal static class SchemaMigrator
    {
        /// <summary>Migrations of the main world database (<c>world.db</c>).</summary>
        internal static readonly IReadOnlyList<SchemaMigration> World = new[]
        {
            new SchemaMigration(1, "Locations spatial columns X/Y/AltitudeMeters", db => db.MigrateLocationCoordinateColumns()),
            new SchemaMigration(2, "Locations climate columns TemperatureCelsius/Humidity", db => db.MigrateLocationClimateColumns()),
            new SchemaMigration(3, "WorldObjects shop pricing columns Price/ShopId", db =>
            {
                db.AddColumnIfMissing("WorldObjects", "Price", "REAL DEFAULT NULL");
                db.AddColumnIfMissing("WorldObjects", "ShopId", "TEXT DEFAULT NULL");
            }),
            new SchemaMigration(4, "WorldObjects.ProducedAt (spoilage clock of produced food)", db =>
                db.AddColumnIfMissing("WorldObjects", "ProducedAt", "INTEGER DEFAULT NULL")),
        };

        /// <summary>Migrations of the terrain database (<c>terrain.db</c>).</summary>
        internal static readonly IReadOnlyList<SchemaMigration> Terrain = new[]
        {
            new SchemaMigration(1, "TerrainHeightmap river/Shreve/oxbow masks", db => db.MigrateTerrainHeightmapColumns()),
        };

        /// <summary>The version a database ends up at after <see cref="Apply"/>.</summary>
        internal static int LatestVersion(IReadOnlyList<SchemaMigration> migrations) => migrations.Max(m => m.Version);

        /// <summary>Runs the migrations newer than the file's <c>user_version</c> in order, then stamps the latest version.</summary>
        /// <exception cref="InvalidOperationException">The file is newer than this build understands.</exception>
        internal static void Apply(SqliteWorldDatabase db, IReadOnlyList<SchemaMigration> migrations)
        {
            ArgumentNullException.ThrowIfNull(db);
            var latest = LatestVersion(migrations);
            var current = db.UserVersion;

            if (current > latest)
                throw new InvalidOperationException(
                    $"The database is at schema version {current}, newer than this build understands ({latest}). Update the application.");

            foreach (var m in migrations.Where(m => m.Version > current).OrderBy(m => m.Version))
            {
                m.Apply(db);
                db.UserVersion = m.Version;
            }

            if (db.UserVersion < latest)
                db.UserVersion = latest;
        }
    }
}
