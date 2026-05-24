// SqliteWorldMapLoader.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.World.Data;

    /// <summary>
    /// Loads a <see cref="WorldMap"/> from a <see cref="SqliteWorldDatabase"/>.
    /// Drop-in replacement for <see cref="WorldMapLoader"/> when using SQLite storage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call <see cref="Load"/> once at startup and register the resulting
    /// <see cref="WorldMap"/> as a singleton in DI.
    /// </para>
    /// <para>
    /// Throws <see cref="InvalidOperationException"/> early when connections
    /// reference unknown locations — fail-fast rather than silent misbehaviour.
    /// </para>
    /// </remarks>
    public static class SqliteWorldMapLoader
    {
        /// <summary>
        /// Reads all locations and connections from the database and constructs
        /// an immutable <see cref="WorldMap"/>.
        /// </summary>
        /// <param name="db">Open world database instance.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a connection references a location not present in the database.
        /// </exception>
        public static WorldMap Load(SqliteWorldDatabase db)
        {
            ArgumentNullException.ThrowIfNull(db);

            // ── Locations + Regions ────────────────────────────────────────────
            var locationRows = db.GetAllLocations();

            var locations = locationRows.ToDictionary(
                r => r.Descriptor.Id,
                r => r.Descriptor,
                StringComparer.Ordinal);

            var regions = locationRows
                .GroupBy(r => r.Region, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<string>)g.Select(r => r.Descriptor.Id).ToList(),
                    StringComparer.Ordinal);

            // ── Connections ────────────────────────────────────────────────────
            var connectionRows = db.GetAllConnections();

            // Validate referential integrity (DB foreign keys may be off at load time).
            foreach (var (fromId, toId, _) in connectionRows)
            {
                if (!locations.ContainsKey(fromId))
                    throw new InvalidOperationException(
                        $"Connection references unknown source location '{fromId}'.");

                if (!locations.ContainsKey(toId))
                    throw new InvalidOperationException(
                        $"Connection references unknown target location '{toId}'.");
            }

            var adjacency = connectionRows
                .GroupBy(c => c.FromId, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<WorldConnection>)g
                        .Select(c => new WorldConnection(c.ToId, c.DistanceMeters))
                        .ToList(),
                    StringComparer.Ordinal);

            return new WorldMap(locations, adjacency, regions);
        }
    }
}
