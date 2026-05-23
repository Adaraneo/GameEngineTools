// WorldDatabaseSeeder.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    using System;
    using System.IO;
    using System.Linq;
    using GameEngineTools.World.Objects;

    /// <summary>
    /// One-time migration utility that seeds a <see cref="SqliteWorldDatabase"/>
    /// from existing CSV source files.
    /// </summary>
    /// <remarks>
    /// Call <see cref="Seed"/> once when the database is empty (e.g. first run).
    /// Subsequent runs are safe — INSERT OR IGNORE prevents duplicate rows.
    /// </remarks>
    public static class WorldDatabaseSeeder
    {
        /// <summary>
        /// Seeds the database from standard CSV paths defined in
        /// <see cref="Constants.FileSystemConstant.SourceFilePath"/>.
        /// </summary>
        public static void SeedFromDefaultPaths(SqliteWorldDatabase db)
            => Seed(db,
                locationsCsv: Constants.FileSystemConstant.SourceFilePath.Locations,
                connectionsCsv: Constants.FileSystemConstant.SourceFilePath.Connections,
                worldObjectsDir: Constants.FileSystemConstant.SourceFilePath.WorldObjectsDirectory);

        /// <summary>
        /// Seeds the database from explicit file paths.
        /// Safe to call multiple times — uses INSERT OR IGNORE.
        /// </summary>
        /// <param name="db">Target database to seed.</param>
        /// <param name="locationsCsv">Path to Locations.csv.</param>
        /// <param name="connectionsCsv">Path to Connections.csv.</param>
        /// <param name="worldObjectsDir">
        /// Directory containing per-location object CSV files
        /// (one file per location, named <c>{locationId}.csv</c>).
        /// </param>
        public static void Seed(
            SqliteWorldDatabase db,
            string locationsCsv,
            string connectionsCsv,
            string worldObjectsDir)
        {
            ArgumentNullException.ThrowIfNull(db);

            SeedLocationsAndConnections(db, locationsCsv, connectionsCsv);
            SeedWorldObjects(db, worldObjectsDir);
        }

        #region Private

        private static void SeedLocationsAndConnections(
            SqliteWorldDatabase db,
            string locationsCsv,
            string connectionsCsv)
        {
            // Reuse the existing CSV WorldMap loader to avoid duplicating parse logic.
            var map = WorldMapLoader.Load(locationsCsv, connectionsCsv);

            foreach (var location in map.AllLocations)
            {
                // Region is stored on the map but not on LocationDescriptor directly —
                // resolve it from the map.
                var region = map.GetRegionOf(location.Id) ?? string.Empty;
                db.InsertLocation(location, region);
            }

            foreach (var (fromId, connections) in map.AllConnections)
            {
                foreach (var conn in connections)
                    db.InsertConnection(fromId, conn.TargetLocationId, conn.DistanceMeters);
            }
        }

        private static void SeedWorldObjects(SqliteWorldDatabase db, string worldObjectsDir)
        {
            if (!Directory.Exists(worldObjectsDir))
                return;

            // CsvWorldObjectProvider loads per-location files lazily.
            // We use it to read ALL locations' objects via GetAllObjects().
            var csvProvider = new CsvWorldObjectProvider(worldObjectsDir);

            foreach (var obj in csvProvider.GetAllObjects())
                db.AddObject(obj);
        }

        #endregion
    }
}
