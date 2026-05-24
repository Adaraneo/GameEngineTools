// WorldDatabaseSeeder.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using GameEngineTools.Constants;
    using GameEngineTools.World.Objects;

    /// <summary>
    /// One-time migration utility that seeds a <see cref="SqliteWorldDatabase"/>
    /// from existing CSV source files.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All INSERT statements use <c>INSERT OR IGNORE</c> — safe to call on every
    /// startup without risk of duplicating data.
    /// </para>
    /// <para>
    /// <b>Migration order:</b>
    /// <list type="number">
    ///   <item>Locations + Connections from <c>Locations.csv</c> and <c>Connections.csv</c>.</item>
    ///   <item>World objects (with affordances and nutritional profiles) from per-location CSV files.</item>
    /// </list>
    /// Locations must be seeded before objects because of the foreign key constraint on
    /// <c>WorldObjects.LocationId</c>.
    /// </para>
    /// <para>
    /// <b>Run-once guard:</b> If the database already contains locations (seeded on a
    /// previous run), the method returns immediately without re-reading the CSV files.
    /// </para>
    /// </remarks>
    public static class WorldDatabaseSeeder
    {
        /// <summary>
        /// Seeds the database from the standard CSV paths defined in
        /// <see cref="FileSystemConstant.SourceFilePath"/>.
        /// No-op when the database already contains data.
        /// </summary>
        /// <param name="db">Target database to seed.</param>
        public static void SeedFromDefaultPaths(SqliteWorldDatabase db)
            => Seed(db,
                locationsCsv:    FileSystemConstant.SourceFilePath.Locations,
                connectionsCsv:  FileSystemConstant.SourceFilePath.Connections,
                worldObjectsDir: FileSystemConstant.SourceFilePath.WorldObjectsDirectory);

        /// <summary>
        /// Seeds the database from explicit file paths.
        /// No-op when the database already contains data (run-once guard).
        /// </summary>
        /// <param name="db">Target database to seed.</param>
        /// <param name="locationsCsv">Absolute path to <c>Locations.csv</c>.</param>
        /// <param name="connectionsCsv">Absolute path to <c>Connections.csv</c>.</param>
        /// <param name="worldObjectsDir">
        /// Directory containing per-location object CSV files,
        /// each named <c>{locationId}.csv</c>.
        /// </param>
        public static void Seed(
            SqliteWorldDatabase db,
            string locationsCsv,
            string connectionsCsv,
            string worldObjectsDir)
        {
            ArgumentNullException.ThrowIfNull(db);

            // Run-once guard: skip seeding if the database already has locations.
            // INSERT OR IGNORE would be safe, but this avoids re-reading all CSV files
            // on every application start.
            if (db.GetAllLocations().Count > 0)
                return;

            SeedLocationsAndConnections(db, locationsCsv, connectionsCsv);
            SeedWorldObjects(db, worldObjectsDir);
        }

        #region Private

        /// <summary>
        /// Parses Locations.csv and Connections.csv via <see cref="WorldMapLoader"/> and
        /// writes all locations and connections to the database.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="WorldMapLoader.Load"/> returns a <see cref="WorldMap"/> that exposes
        /// <c>AllLocations</c> (<see cref="IEnumerable{LocationDescriptor}"/>) and
        /// <c>AllConnections</c> (<see cref="IEnumerable{T}"/> of (FromId, Connections) tuples).
        /// Region information is reconstructed by inverting the map's internal region dictionary
        /// via <see cref="WorldMap.GetLocationsInRegion"/> combined with <c>GetAllRegions()</c>.
        /// </para>
        /// </remarks>
        private static void SeedLocationsAndConnections(
            SqliteWorldDatabase db,
            string locationsCsv,
            string connectionsCsv)
        {
            // WorldMapLoader already handles all CSV parsing — no duplication needed.
            var map = WorldMapLoader.Load(locationsCsv, connectionsCsv);

            // Build a reverse lookup: locationId → region name.
            // WorldMap stores regions as region → [locationIds]; we need the inverse.
            var locationToRegion = BuildLocationToRegionMap(map);

            // Seed locations.
            foreach (var location in map.AllLocations)
            {
                var region = locationToRegion.GetValueOrDefault(location.Id, string.Empty);
                db.InsertLocation(location, region);
            }

            // Seed connections.
            foreach (var (fromId, connections) in map.AllConnections)
            {
                foreach (var conn in connections)
                    db.InsertConnection(fromId, conn.TargetLocationId, conn.DistanceMeters);
            }
        }

        /// <summary>
        /// Builds a reverse-lookup dictionary: location ID → region name.
        /// </summary>
        /// <remarks>
        /// <see cref="WorldMap"/> stores regions as (region → locationIds).
        /// This method inverts that mapping for efficient per-location lookup during seeding.
        /// Locations without a region are absent from the result; callers default to empty string.
        /// </remarks>
        private static Dictionary<string, string> BuildLocationToRegionMap(WorldMap map)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            // GetAllRegions() exposes region names; GetLocationsInRegion() exposes their IDs.
            foreach (var region in map.GetAllRegions())
            {
                foreach (var locationId in map.GetLocationsInRegion(region))
                    result[locationId] = region;
            }

            return result;
        }

        /// <summary>
        /// Uses <see cref="CsvWorldObjectProvider"/> to enumerate all per-location
        /// CSV files and seeds each object into the database.
        /// </summary>
        private static void SeedWorldObjects(SqliteWorldDatabase db, string worldObjectsDir)
        {
            if (!Directory.Exists(worldObjectsDir))
                return;

            // CsvWorldObjectProvider parses all per-location files.
            // GetAllObjects() triggers lazy loading of every file in the directory.
            var csvProvider = new CsvWorldObjectProvider(worldObjectsDir);

            foreach (var obj in csvProvider.GetAllObjects())
                db.AddObject(obj);
        }

        #endregion
    }
}
