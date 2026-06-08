// IWorldObjectProvider.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using System.Collections.Generic;

    /// <summary>
    /// Provides world objects present in a given location to the simulation engine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the integration seam for Unity (Phase 2).</b><br/>
    /// In Phase 1, <see cref="StaticWorldObjectProvider"/> serves pre-defined objects
    /// loaded from configuration or code.<br/>
    /// In Phase 2, <c>UnityWorldObjectProvider</c> will implement this interface
    /// and bridge Unity's <c>GameObject</c> system — without any changes
    /// to the simulation engine.
    /// </para>
    /// <para>
    /// Design rule: the simulation engine only ever depends on this interface,
    /// never on a concrete provider.
    /// </para>
    /// </remarks>
    public interface IWorldObjectProvider
    {
        /// <summary>
        /// Returns all world objects currently present at the specified location.
        /// </summary>
        /// <param name="locationId">
        /// The location identifier matching <see cref="GameEngineTools.World.Location.LocationDescriptor.Id"/>.
        /// </param>
        /// <returns>
        /// Enumerable of <see cref="WorldObject"/> instances at the location.
        /// Returns an empty collection if the location has no objects or is unknown.
        /// </returns>
        IEnumerable<WorldObject> GetObjectsAt(string locationId);

        /// <summary>
        /// Returns all world objects across all locations.
        /// Useful for diagnostics and NPC Watcher visualization.
        /// </summary>
        IEnumerable<WorldObject> GetAllObjects();

        /// <summary>
        /// Adds a world object to a location at runtime.
        /// The object exists only in the in-memory cache — it is never written back to CSV.
        /// Use cases: character drops an item at a new location, crafting, narrative spawns.
        /// </summary>
        void AddObject(WorldObject obj);

        /// <summary>
        /// Finds a world object by its unique ID across all locations.
        /// Returns <c>null</c> if no object with the given ID is known.
        /// </summary>
        /// <param name="objectId">The unique object identifier.</param>
        WorldObject? FindObject(string objectId);
    }
}
