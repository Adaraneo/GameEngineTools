// WorldObjectSnapshotCache.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Per-tick in-memory snapshot of world objects, eliminating repeated SQLite queries
    /// during the behavior tick loop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Problem it solves:</b><br/>
    /// Without a cache, <see cref="IWorldObjectProvider.GetObjectsAt"/> is called once per
    /// character per substep. With 50 characters in the same location, that is 50 identical
    /// SQL queries per substep — most returning the same data.
    /// </para>
    /// <para>
    /// <b>How it works:</b>
    /// <list type="number">
    ///   <item>
    ///     At the start of each substep, <see cref="SimulationScene"/> calls
    ///     <see cref="Refresh"/> with the set of active location IDs.
    ///     This issues one <see cref="IWorldObjectProvider.GetObjectsAt"/> query
    ///     per <i>distinct location</i> — typically 2–5 queries regardless of character count.
    ///   </item>
    ///   <item>
    ///     All <see cref="GetObjectsAt"/> calls during the substep are served from the
    ///     in-memory snapshot — zero additional SQL queries.
    ///   </item>
    ///   <item>
    ///     Mutations (<c>ConsumeObject</c>, <c>SetHeldBy</c>, etc.) bypass the cache
    ///     and go directly to <see cref="IMutableWorldObjectProvider"/>. The next
    ///     <see cref="Refresh"/> picks up the updated state.
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Staleness within a substep:</b><br/>
    /// If character A consumes a food object, character B will still see it in the snapshot
    /// until the next <see cref="Refresh"/>. This is intentional and correct —
    /// a substep represents a discrete time slice; concurrent perception is synchronised
    /// at the substep boundary, not within it.
    /// </para>
    /// <para>
    /// Register as a singleton in DI. <see cref="SimulationScene"/> holds a reference
    /// and calls <see cref="Refresh"/> once per <c>SimulateSingleStep</c>.
    /// </para>
    /// </remarks>
    public sealed class WorldObjectSnapshotCache : IWorldObjectProvider
    {
        #region Private state

        /// <summary>Backing provider — queries go here during Refresh.</summary>
        private readonly IWorldObjectProvider _source;

        /// <summary>
        /// Current per-location snapshot. Rebuilt each substep.
        /// Key = locationId, Value = available objects at that location.
        /// </summary>
        private Dictionary<string, IReadOnlyList<WorldObject>> _snapshot = new(StringComparer.Ordinal);

        /// <summary>Cached cross-location held-objects lookup. Rebuilt each substep.</summary>
        private List<WorldObject> _heldObjects = new();

        #endregion

        #region Construction

        /// <summary>
        /// Initialises the cache wrapping the given provider.
        /// </summary>
        /// <param name="source">
        /// The real provider (e.g. <see cref="SqliteWorldObjectProvider"/>).
        /// Queried only during <see cref="Refresh"/>.
        /// </param>
        public WorldObjectSnapshotCache(IWorldObjectProvider source)
        {
            ArgumentNullException.ThrowIfNull(source);
            _source = source;
        }

        #endregion

        #region Cache management

        /// <summary>
        /// Rebuilds the snapshot for the given set of active location IDs.
        /// Call once at the start of each simulation substep, before any character ticks.
        /// </summary>
        /// <param name="activeLocationIds">
        /// Location IDs that currently have characters assigned.
        /// Duplicate IDs are handled internally — one query per distinct location.
        /// </param>
        /// <remarks>
        /// Locations not in <paramref name="activeLocationIds"/> return an empty
        /// collection from <see cref="GetObjectsAt"/> for the duration of this substep.
        /// This is correct — if no character is there, no character will query it.
        /// </remarks>
        public void Refresh(IEnumerable<string> activeLocationIds)
        {
            ArgumentNullException.ThrowIfNull(activeLocationIds);

            var newSnapshot = new Dictionary<string, IReadOnlyList<WorldObject>>(StringComparer.Ordinal);

            foreach (var locationId in activeLocationIds)
            {
                // Skip nulls (unplaced characters) and duplicates.
                if (string.IsNullOrEmpty(locationId) || newSnapshot.ContainsKey(locationId))
                    continue;

                newSnapshot[locationId] = _source.GetObjectsAt(locationId).ToList();
            }

            _snapshot    = newSnapshot;
            _heldObjects = newSnapshot.Values.SelectMany(objs => objs).ToList();
        }

        #endregion

        #region IWorldObjectProvider

        /// <inheritdoc/>
        /// <remarks>
        /// Served from the in-memory snapshot — no SQL query issued.
        /// Returns empty when the location was not included in the last <see cref="Refresh"/>.
        /// </remarks>
        public IEnumerable<WorldObject> GetObjectsAt(string locationId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locationId);

            return _snapshot.TryGetValue(locationId, out var objects)
                ? objects
                : Array.Empty<WorldObject>();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Bypasses the snapshot and queries the backing provider directly.
        /// Not called on the hot path — used only for diagnostics and NPC Watcher.
        /// </remarks>
        public IEnumerable<WorldObject> GetAllObjects()
            => _source.GetAllObjects();

        /// <inheritdoc/>
        /// <remarks>
        /// Bypasses the snapshot — required for mutation paths (Drop logic in
        /// <see cref="DefaultObjectInteractionEngine"/>) where the exact object
        /// state at the DB level is needed, not the tick-start snapshot.
        /// </remarks>
        public WorldObject? FindObject(string objectId)
            => _source.FindObject(objectId);

        /// <inheritdoc/>
        /// <remarks>
        /// Writes through to the backing provider immediately.
        /// The change will be visible in the next <see cref="Refresh"/>.
        /// </remarks>
        public void AddObject(WorldObject obj)
            => _source.AddObject(obj);

        #endregion
    }
}
