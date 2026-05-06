// WorldMap.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.World.Location;

    /// <summary>
    /// Describes a directed connection between two locations.
    /// </summary>
    /// <param name="TargetLocationId">The ID of the destination location.</param>
    /// <param name="TravelMinutes">
    /// Approximate travel time in simulation minutes.
    /// Used by the behavior engine when emitting <c>MoveTo:*</c> actions.
    /// </param>
    public sealed record WorldConnection(string TargetLocationId, int TravelMinutes);

    /// <summary>
    /// Immutable map of the game world: location descriptors, adjacency graph,
    /// and region groupings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Loaded once at startup from <c>Locations.csv</c> and <c>Connections.csv</c>
    /// via <see cref="WorldMapLoader"/>. After construction this object is read-only.
    /// </para>
    /// <para>
    /// <b>Regions</b> are named groups of locations (e.g. "Castle", "Village").
    /// They allow the behavior engine to reason about broad areas without
    /// knowing every individual location ID.
    /// </para>
    /// <para>
    /// <b>Connections</b> are directed — if A→B exists, B→A must be declared
    /// separately in <c>Connections.csv</c> unless the map is symmetric by design.
    /// </para>
    /// </remarks>
    public sealed class WorldMap
    {
        #region Private state

        /// <summary>All location descriptors keyed by location ID.</summary>
        private readonly IReadOnlyDictionary<string, LocationDescriptor> _locations;

        /// <summary>
        /// Adjacency list keyed by source location ID.
        /// Value is the list of outgoing connections from that location.
        /// </summary>
        private readonly IReadOnlyDictionary<string, IReadOnlyList<WorldConnection>> _adjacency;

        /// <summary>Region name → list of location IDs belonging to that region.</summary>
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _regions;

        #endregion

        #region Construction

        /// <summary>
        /// Constructs a <see cref="WorldMap"/> from pre-parsed data.
        /// Intended to be called by <see cref="WorldMapLoader"/> only.
        /// </summary>
        /// <param name="locations">All location descriptors keyed by ID.</param>
        /// <param name="adjacency">Outgoing connections per location ID.</param>
        /// <param name="regions">Region name → location ID mapping.</param>
        internal WorldMap(
            IReadOnlyDictionary<string, LocationDescriptor> locations,
            IReadOnlyDictionary<string, IReadOnlyList<WorldConnection>> adjacency,
            IReadOnlyDictionary<string, IReadOnlyList<string>> regions)
        {
            _locations = locations;
            _adjacency = adjacency;
            _regions   = regions;
        }

        #endregion

        #region Location queries

        /// <summary>
        /// All registered locations, keyed by location ID.
        /// </summary>
        public IReadOnlyDictionary<string, LocationDescriptor> Locations => _locations;

        /// <summary>
        /// Returns the descriptor for a specific location, or <c>null</c> if not found.
        /// </summary>
        /// <param name="locationId">The location ID to look up.</param>
        public LocationDescriptor? GetLocation(string locationId)
            => _locations.GetValueOrDefault(locationId);

        #endregion

        #region Adjacency queries

        /// <summary>
        /// Returns all outgoing connections from the specified location.
        /// Returns an empty list if the location has no connections or is unknown.
        /// </summary>
        /// <param name="locationId">Source location ID.</param>
        public IReadOnlyList<WorldConnection> GetConnections(string locationId)
            => _adjacency.TryGetValue(locationId, out var connections)
                ? connections
                : Array.Empty<WorldConnection>();

        /// <summary>
        /// Returns all location IDs that can be reached directly from the given location,
        /// ordered by ascending travel time.
        /// </summary>
        /// <param name="locationId">Source location ID.</param>
        public IReadOnlyList<string> GetNeighbors(string locationId)
            => GetConnections(locationId)
                .OrderBy(c => c.TravelMinutes)
                .Select(c => c.TargetLocationId)
                .ToList();

        #endregion

        #region Region queries

        /// <summary>
        /// All region names present in the map.
        /// </summary>
        public IReadOnlyList<string> AllRegions => _regions.Keys.ToList();

        /// <summary>
        /// Returns all location IDs belonging to the specified region.
        /// Returns an empty list if the region is unknown.
        /// </summary>
        /// <param name="region">Region name (case-sensitive, as declared in Locations.csv).</param>
        public IReadOnlyList<string> GetLocationsInRegion(string region)
            => _regions.TryGetValue(region, out var ids)
                ? ids
                : Array.Empty<string>();

        /// <summary>
        /// Returns the region name for the specified location, or <c>null</c> if not found.
        /// </summary>
        /// <param name="locationId">Location ID to query.</param>
        public string? GetRegionOf(string locationId)
        {
            foreach (var (region, ids) in _regions)
            {
                if (ids.Contains(locationId))
                    return region;
            }
            return null;
        }

        #endregion

        #region ILocationService integration

        /// <summary>
        /// Registers all locations from this map into the provided <see cref="ILocationService"/>.
        /// </summary>
        /// <remarks>
        /// Call this once at startup, after loading the map and before placing any characters.
        /// This replaces manual <c>locationService.RegisterLocation(...)</c> calls in Program.cs.
        /// </remarks>
        /// <param name="locationService">The service to register locations into.</param>
        public void RegisterAllLocations(ILocationService locationService)
        {
            ArgumentNullException.ThrowIfNull(locationService);

            foreach (var descriptor in _locations.Values)
                locationService.RegisterLocation(descriptor);
        }

        #endregion
    }
}
