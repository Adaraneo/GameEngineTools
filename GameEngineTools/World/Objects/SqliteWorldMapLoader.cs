// SqliteWorldMapLoader.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Location;

    /// <summary>
    /// Loads a <see cref="WorldMap"/> from a <see cref="SqliteWorldDatabase"/>.
    /// Drop-in replacement for <see cref="WorldMapLoader"/> when using SQLite storage.
    /// </summary>
    public static class SqliteWorldMapLoader
    {
        /// <summary>
        /// Reads all locations, regions, and connections from the database
        /// and constructs an immutable <see cref="WorldMap"/>.
        /// </summary>
        /// <param name="db">Open world database instance.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a connection references a location that is not in the database.
        /// </exception>
        public static WorldMap Load(SqliteWorldDatabase db)
        {
            ArgumentNullException.ThrowIfNull(db);

            // ── Locations + Regions ───────────────────────────────────────────
            var locationRows = db.GetAllLocations();

            var locations = locationRows.ToDictionary(
                r => r.Descriptor.Id,
                r => r.Descriptor);

            var regions = locationRows
                .GroupBy(r => r.Region)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<string>)g.Select(r => r.Descriptor.Id).ToList());

            // ── Connections ───────────────────────────────────────────────────
            var connectionRows = db.GetAllConnections();

            foreach (var (fromId, toId, _) in connectionRows)
            {
                if (!locations.ContainsKey(fromId))
                    throw new InvalidOperationException(
                        $"Connection references unknown location '{fromId}'.");
                if (!locations.ContainsKey(toId))
                    throw new InvalidOperationException(
                        $"Connection references unknown location '{toId}'.");
            }

            var adjacency = connectionRows
                .GroupBy(c => c.FromId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<WorldConnection>)g
                        .Select(c => new WorldConnection(c.ToId, c.DistanceMeters))
                        .ToList());

            return new WorldMap(locations, adjacency, regions);
        }
    }
}
