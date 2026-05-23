// WorldMap.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.World.Location;

    /// <summary>
    /// Describes a directed connection between two locations.
    /// </summary>
    /// <param name="TargetLocationId">The ID of the destination location.</param>
    /// <param name="DistanceMeters">
    /// Distance between the two locations in metres.
    /// Used by the movement system to compute travel duration based on character speed.
    /// </param>
    public sealed record WorldConnection(string TargetLocationId, double DistanceMeters);

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
        private readonly ConcurrentDictionary<string, LocationDescriptor> _locations;

        /// <summary>
        /// Adjacency list keyed by source location ID.
        /// Value is the list of outgoing connections from that location.
        /// </summary>
        private readonly ConcurrentDictionary<string, List<WorldConnection>> _adjacency;

        /// <summary>Region name → list of location IDs belonging to that region.</summary>
        private readonly ConcurrentDictionary<string, List<string>> _regions;

        #endregion Private state

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
            _locations = new ConcurrentDictionary<string, LocationDescriptor>(locations);
            _adjacency = new ConcurrentDictionary<string, List<WorldConnection>>(
                adjacency.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()));
            _regions = new ConcurrentDictionary<string, List<string>>(
                regions.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()));
        }

        #endregion Construction

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

        #endregion Location queries

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
                .OrderBy(c => c.DistanceMeters)
                .Select(c => c.TargetLocationId)
                .ToList();

        #endregion Adjacency queries

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

        #endregion Region queries

        #region Runtime mutation

        /// <summary>
        /// Registers a new location in the world map at runtime.
        /// The location becomes immediately reachable via <see cref="GetLocation"/>,
        /// <see cref="GetLocationsInRegion"/>, and connection queries.
        /// </summary>
        /// <param name="descriptor">The location to add.</param>
        /// <param name="region">
        /// Optional region name to file this location under.
        /// Pass <c>null</c> or empty to skip region indexing.
        /// </param>
        /// <param name="locationService">
        /// When provided, the location is also registered with the service so characters
        /// can be placed there and context events are dispatched correctly.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a location with the same ID is already registered.
        /// </exception>
        public void AddLocation(
            LocationDescriptor descriptor,
            string? region = null,
            ILocationService? locationService = null)
        {
            if (!_locations.TryAdd(descriptor.Id, descriptor))
                throw new InvalidOperationException(
                    $"Location '{descriptor.Id}' already exists in the world map.");

            if (!string.IsNullOrEmpty(region))
            {
                _regions.AddOrUpdate(
                    region,
                    _ => [descriptor.Id],
                    (_, list) => { list.Add(descriptor.Id); return list; });
            }

            locationService?.RegisterLocation(descriptor);
        }

        /// <summary>
        /// Adds a directed connection from <paramref name="fromId"/> to <paramref name="toId"/>.
        /// To create a bidirectional edge, call this method twice (both directions).
        /// </summary>
        /// <param name="fromId">Source location ID. Must already be registered.</param>
        /// <param name="toId">Target location ID. Must already be registered.</param>
        /// <param name="distanceMeters">Straight-line distance in metres.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when either location ID is not registered in the world map.
        /// </exception>
        public void AddConnection(string fromId, string toId, double distanceMeters)
        {
            if (!_locations.ContainsKey(fromId))
                throw new InvalidOperationException(
                    $"Source location '{fromId}' is not registered in the world map.");
            if (!_locations.ContainsKey(toId))
                throw new InvalidOperationException(
                    $"Target location '{toId}' is not registered in the world map.");

            _adjacency.AddOrUpdate(
                fromId,
                _ => [new WorldConnection(toId, distanceMeters)],
                (_, list) =>
                {
                    list.Add(new WorldConnection(toId, distanceMeters));
                    return list;
                });
        }

        #endregion Runtime mutation

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

        #endregion ILocationService integration
    }
}
