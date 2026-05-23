// SqliteWorldObjectProvider.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// SQLite-backed implementation of <see cref="IMutableWorldObjectProvider"/>.
    /// Replaces <see cref="CsvWorldObjectProvider"/> for production use.
    /// All queries hit an indexed SQLite database — efficient for large worlds
    /// and compatible with Unity's <c>Mono.Data.Sqlite</c> runtime.
    /// </summary>
    public sealed class SqliteWorldObjectProvider : IMutableWorldObjectProvider
    {
        #region Private state

        private readonly SqliteWorldDatabase _db;

        #endregion

        #region Construction

        /// <summary>
        /// Initialises the provider using the shared world database instance.
        /// </summary>
        /// <param name="db">The shared <see cref="SqliteWorldDatabase"/> singleton.</param>
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

        #region IMutableWorldObjectProvider

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
    }
}
