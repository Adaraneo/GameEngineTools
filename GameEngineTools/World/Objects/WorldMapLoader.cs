// WorldMapLoader.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using GameEngineTools.Constants;
    using GameEngineTools.FileSystem;
    using GameEngineTools.World.Location;

    /// <summary>
    /// Loads a <see cref="WorldMap"/> from CSV source files at startup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads two files:
    /// <list type="bullet">
    ///   <item>
    ///     <c>Locations.csv</c> — one row per location, defines
    ///     <see cref="LocationDescriptor"/> fields plus a <c>Region</c> column.
    ///   </item>
    ///   <item>
    ///     <c>Connections.csv</c> — one row per directed edge in the adjacency graph,
    ///     defines FromId, ToId, and DistanceMeters.
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Locations.csv column order:</b><br/>
    /// <c>Id;DisplayName;Type;Region;BaseNoise;NoisePerPerson;Capacity;AllowsPrivacy</c>
    /// </para>
    /// <para>
    /// <b>Connections.csv column order:</b><br/>
    /// <c>FromId;ToId;DistanceMeters</c>
    /// </para>
    /// </remarks>
    public static class WorldMapLoader
    {
        #region Public API

        /// <summary>
        /// Loads and constructs a <see cref="WorldMap"/> using the default paths
        /// from <see cref="FileSystemConstant.SourceFilePath"/>.
        /// </summary>
        /// <returns>An immutable <see cref="WorldMap"/> ready for use at runtime.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if a connection references a location ID not present in Locations.csv.
        /// </exception>
        public static WorldMap Load()
            => Load(
                FileSystemConstant.SourceFilePath.Locations,
                FileSystemConstant.SourceFilePath.Connections);

        /// <summary>
        /// Loads and constructs a <see cref="WorldMap"/> from explicit file paths.
        /// Intended for tests and alternative configurations.
        /// </summary>
        /// <param name="locationsCsvPath">Path to Locations.csv.</param>
        /// <param name="connectionsCsvPath">Path to Connections.csv.</param>
        public static WorldMap Load(string locationsCsvPath, string connectionsCsvPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locationsCsvPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionsCsvPath);

            // ── Step 1: Load location descriptors + region assignments ────────
            var locationRows = CsvLoader.Load(locationsCsvPath, ParseLocationRow);

            var locations = locationRows
                .ToDictionary(r => r.Descriptor.Id, r => r.Descriptor);

            // region → list of location IDs
            var regions = locationRows
                .GroupBy(r => r.Region)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<string>)g.Select(r => r.Descriptor.Id).ToList());

            // ── Step 2: Load adjacency graph ──────────────────────────────────
            var connectionRows = CsvLoader.Load(connectionsCsvPath, ParseConnectionRow);

            // Validate all referenced IDs exist
            foreach (var conn in connectionRows)
            {
                if (!locations.ContainsKey(conn.FromId))
                    throw new InvalidOperationException(
                        $"Connections.csv references unknown location '{conn.FromId}'.");

                if (!locations.ContainsKey(conn.TargetLocationId))
                    throw new InvalidOperationException(
                        $"Connections.csv references unknown location '{conn.TargetLocationId}'.");
            }

            var adjacency = connectionRows
                .GroupBy(c => c.FromId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<WorldConnection>)g
                        .Select(c => new WorldConnection(c.TargetLocationId, c.DistanceMeters))
                        .ToList());

            return new WorldMap(locations, adjacency, regions);
        }

        #endregion Public API

        #region Private parsers

        /// <summary>
        /// Intermediate DTO carrying a parsed location row including region assignment.
        /// Region is world-level metadata; it does not belong in <see cref="LocationDescriptor"/>.
        /// </summary>
        private sealed record LocationRow(LocationDescriptor Descriptor, string Region);

        /// <summary>
        /// Intermediate DTO for a parsed connection row before building <see cref="WorldConnection"/>.
        /// </summary>
        private sealed record ConnectionRow(string FromId, string TargetLocationId, double DistanceMeters);

        /// <summary>
        /// <summary>
        /// Parses a single row from Locations.csv.
        /// Expected columns (0-indexed):
        /// 0=Id, 1=DisplayName, 2=Type, 3=Region,
        /// 4=BaseNoise, 5=NoisePerPerson, 6=Capacity, 7=AllowsPrivacy,
        /// optionally 8=Terrain, 9=DangerLevel, 10=AllowsPickup, 11=X, 12=Y
        /// </summary>
        private static LocationRow ParseLocationRow(string[] v)
        {
            var descriptor = new LocationDescriptor(
                Id: v[0].Trim(),
                DisplayName: v[1].Trim(),
                Type: Enum.Parse<LocationType>(v[2].Trim(), ignoreCase: true),
                BaseNoise: double.Parse(v[4].Trim(), CultureInfo.InvariantCulture),
                NoisePerPerson: double.Parse(v[5].Trim(), CultureInfo.InvariantCulture),
                Capacity: int.Parse(v[6].Trim(), CultureInfo.InvariantCulture),
                AllowsPrivacy: bool.Parse(v[7].Trim()),
                Terrain: v.Length > 8 ? Enum.Parse<TerrainType>(v[8].Trim(), ignoreCase: true) : TerrainType.Indoor,
                DangerLevel: v.Length > 9 ? double.Parse(v[9].Trim(), CultureInfo.InvariantCulture) : 0.0,
                AllowsPickup: v.Length > 10 ? bool.Parse(v[10].Trim()) : true,
                X: v.Length > 11 ? double.Parse(v[11].Trim(), CultureInfo.InvariantCulture) : 0.0,
                Y: v.Length > 12 ? double.Parse(v[12].Trim(), CultureInfo.InvariantCulture) : 0.0);

            return new LocationRow(descriptor, Region: v[3].Trim());
        }

        /// <summary>
        /// Parses a single row from Connections.csv.
        /// Expected columns: 0=FromId, 1=ToId, 2=DistanceMeters
        /// </summary>
        private static ConnectionRow ParseConnectionRow(string[] v)
            => new(
                FromId: v[0].Trim(),
                TargetLocationId: v[1].Trim(),
                DistanceMeters: double.Parse(v[2].Trim(), CultureInfo.InvariantCulture));

        #endregion Private parsers
    }
}
