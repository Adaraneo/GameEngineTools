// WorldObjectWriteBuffer.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Buffers world object mutations for a single simulation substep.
    /// Call <see cref="Flush"/> once at the end of each substep to persist
    /// all changes in a single SQLite transaction.
    /// </summary>
    /// <remarks>
    /// Wraps <see cref="SqliteWorldObjectProvider"/> — reads pass through immediately,
    /// only mutations are buffered.
    /// Register as a singleton alongside <see cref="SqliteWorldObjectProvider"/>.
    /// </remarks>
    public sealed class WorldObjectWriteBuffer : IMutableWorldObjectProvider
    {
        #region Private types

        /// <summary>The kind of mutation buffered for an object.</summary>
        public enum MutationKind
        {
            /// <summary>Mark as consumed at a specific simulation time.</summary>
            Consumed,

            /// <summary>Clear held and consumed state — return to availability.</summary>
            Restored,

            /// <summary>Assign or clear the character holding this object.</summary>
            HeldBy,

            /// <summary>Permanently delete from the database.</summary>
            Removed
        }

        /// <summary>Single pending mutation for one world object.</summary>
        public sealed record PendingMutation(
            MutationKind Kind,
            string LocationId,
            string ObjectId,
            long? ConsumedAtTicks,
            string? HolderGuid);

        #endregion Private types

        #region Private state

        private readonly SqliteWorldObjectProvider _provider;
        private readonly SqliteWorldDatabase _db;
        private readonly object _lock = new();

        /// <summary>
        /// Pending mutations keyed by object ID.
        /// Last-write-wins: later mutations for the same object overwrite earlier ones.
        /// </summary>
        private readonly Dictionary<string, PendingMutation> _pending = new(StringComparer.Ordinal);

        #endregion Private state

        #region Constructor

        /// <summary>
        /// Initialises the buffer wrapping the given provider and database.
        /// </summary>
        /// <param name="provider">Read-through provider for all non-mutation operations.</param>
        /// <param name="db">Database used during <see cref="Flush"/> for batch writes.</param>
        public WorldObjectWriteBuffer(SqliteWorldObjectProvider provider, SqliteWorldDatabase db)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(db);
            _provider = provider;
            _db = db;
        }

        #endregion Constructor

        #region IMutableWorldObjectProvider — reads (pass-through)

        /// <inheritdoc/>
        public IEnumerable<WorldObject> GetObjectsAt(string locationId)
            => _provider.GetObjectsAt(locationId);

        /// <inheritdoc/>
        public IEnumerable<WorldObject> GetAllObjects()
            => _provider.GetAllObjects();

        /// <inheritdoc/>
        public WorldObject? FindObject(string objectId)
            => _provider.FindObject(objectId);

        /// <inheritdoc/>
        public IEnumerable<WorldObject> GetHeldBy(HumanId holder)
            => _provider.GetHeldBy(holder);

        /// <inheritdoc/>
        public IEnumerable<string> GetKnownLocationIds()
            => _provider.GetKnownLocationIds();

        /// <inheritdoc/>
        public IEnumerable<WorldObject> GetAllObjectsAt(string locationId)
            => _provider.GetAllObjectsAt(locationId);

        #endregion IMutableWorldObjectProvider — reads

        #region IMutableWorldObjectProvider — buffered mutations

        /// <summary>
        /// Writes immediately — AddObject is rare (seeding, runtime spawns) and must be
        /// visible to the cache on the next Refresh without waiting for Flush.
        /// </summary>
        /// <inheritdoc/>
        public void AddObject(WorldObject obj)
            => _provider.AddObject(obj);

        /// <inheritdoc/>
        public bool ConsumeObject(string locationId, string objectId, WDateTime now)
        {
            lock (_lock)
                _pending[objectId] = new PendingMutation(
                    MutationKind.Consumed, locationId, objectId,
                    ConsumedAtTicks: now.WorldTicks,
                    HolderGuid: null);
            return true;
        }

        /// <inheritdoc/>
        public bool RestoreObject(string locationId, string objectId)
        {
            lock (_lock)
                _pending[objectId] = new PendingMutation(
                    MutationKind.Restored, locationId, objectId,
                    ConsumedAtTicks: null,
                    HolderGuid: null);
            return true;
        }

        /// <inheritdoc/>
        public bool SetHeldBy(string locationId, string objectId, HumanId? holder)
        {
            lock (_lock)
                _pending[objectId] = new PendingMutation(
                    MutationKind.HeldBy, locationId, objectId,
                    ConsumedAtTicks: null,
                    HolderGuid: holder?.Value.ToString());
            return true;
        }

        /// <inheritdoc/>
        public bool RemoveObject(string locationId, string objectId)
        {
            lock (_lock)
                _pending[objectId] = new PendingMutation(
                    MutationKind.Removed, locationId, objectId,
                    ConsumedAtTicks: null,
                    HolderGuid: null);
            return true;
        }

        #endregion IMutableWorldObjectProvider — buffered mutations

        #region Flush

        /// <summary>
        /// Persists all buffered mutations to SQLite in a single transaction.
        /// Call once at the start of each substep, before <see cref="WorldObjectSnapshotCache.Refresh"/>.
        /// No-op when the buffer is empty.
        /// </summary>
        /// <remarks>
        /// Execution order per substep:
        /// <list type="number">
        ///   <item><see cref="Flush"/> — commits pending mutations from previous substep.</item>
        ///   <item><see cref="WorldObjectSnapshotCache.Refresh"/> — reads fresh state into cache.</item>
        ///   <item>Character ticks — mutations buffered here.</item>
        /// </list>
        /// </remarks>
        public void Flush()
        {
            List<PendingMutation> batch;

            lock (_lock)
            {
                if (_pending.Count == 0)
                    return;

                batch = new List<PendingMutation>(_pending.Values);
                _pending.Clear();
            }

            // Single transaction for all mutations in this substep.
            _db.ApplyBatch(batch);
        }

        #endregion Flush
    }
}
