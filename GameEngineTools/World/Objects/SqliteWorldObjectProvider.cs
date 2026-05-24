// SqliteWorldObjectProvider.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// SQLite-backed implementation of <see cref="IMutableWorldObjectProvider"/>.
    /// Production replacement for <see cref="CsvWorldObjectProvider"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All queries are delegated to <see cref="SqliteWorldDatabase"/>, which owns
    /// the connection lifetime and query execution. This class is a thin, stateless
    /// adapter — register it as a singleton.
    /// </para>
    /// <para>
    /// Compatible with Unity's <c>Mono.Data.Sqlite</c> runtime for Phase 2 integration.
    /// Replace the <see cref="SqliteWorldDatabase"/> registration in DI and this class
    /// continues to work without modification.
    /// </para>
    /// </remarks>
    public sealed class SqliteWorldObjectProvider : IMutableWorldObjectProvider
    {
        #region Private state

        /// <summary>Shared database access layer. Injected as singleton.</summary>
        private readonly SqliteWorldDatabase _db;

        #endregion

        #region Construction

        /// <summary>
        /// Initialises the provider with the shared world database instance.
        /// </summary>
        /// <param name="db">Shared <see cref="SqliteWorldDatabase"/> singleton.</param>
        public SqliteWorldObjectProvider(SqliteWorldDatabase db)
        {
            ArgumentNullException.ThrowIfNull(db);
            _db = db;
        }

        #endregion

        #region IWorldObjectProvider

        /// <inheritdoc/>
        public IEnumerable<WorldObject> GetObjectsAt(string locationId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
            return _db.GetObjectsAt(locationId);
        }

        /// <inheritdoc/>
        public IEnumerable<WorldObject> GetAllObjects()
            => _db.GetAllObjects();

        /// <inheritdoc/>
        public WorldObject? FindObject(string objectId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
            return _db.FindObject(objectId);
        }

        /// <inheritdoc/>
        public void AddObject(WorldObject obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
            _db.AddObject(obj);
        }

        #endregion

        #region IMutableWorldObjectProvider — mutations

        /// <inheritdoc/>
        public bool ConsumeObject(string locationId, string objectId, WDateTime now)
            => _db.ConsumeObject(locationId, objectId, now);

        /// <inheritdoc/>
        public bool RestoreObject(string locationId, string objectId)
            => _db.RestoreObject(locationId, objectId);

        /// <inheritdoc/>
        public bool RemoveObject(string locationId, string objectId)
            => _db.RemoveObject(locationId, objectId);

        /// <inheritdoc/>
        public bool SetHeldBy(string locationId, string objectId, HumanId? holder)
            => _db.SetHeldBy(locationId, objectId, holder);

        /// <inheritdoc/>
        public IEnumerable<WorldObject> GetHeldBy(HumanId holder)
            => _db.GetHeldBy(holder);

        #endregion

        #region IMutableWorldObjectProvider — respawn inspection

        /// <inheritdoc/>
        public IEnumerable<string> GetKnownLocationIds()
            => _db.GetKnownLocationIds();

        /// <inheritdoc/>
        public IEnumerable<WorldObject> GetAllObjectsAt(string locationId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
            return _db.GetAllObjectsAt(locationId);
        }

        #endregion
    }
}
