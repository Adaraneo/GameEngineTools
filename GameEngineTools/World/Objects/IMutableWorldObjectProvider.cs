// IMutableWorldObjectProvider.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Extends <see cref="IWorldObjectProvider"/> with runtime mutation operations
    /// required by the interaction and behavior engines.
    /// </summary>
    /// <remarks>
    /// Implementations include <see cref="CsvWorldObjectProvider"/> (Phase 1)
    /// and <c>SqliteWorldObjectProvider</c> (Phase 1.5+).
    /// </remarks>
    public interface IMutableWorldObjectProvider : IWorldObjectProvider
    {
        /// <summary>
        /// Marks an object as consumed at the given simulation time.
        /// The object is excluded from <see cref="IWorldObjectProvider.GetObjectsAt"/>
        /// until respawned or restored.
        /// </summary>
        bool ConsumeObject(string locationId, string objectId, WDateTime now);

        /// <summary>
        /// Restores a consumed or held object to its available state.
        /// </summary>
        bool RestoreObject(string locationId, string objectId);

        /// <summary>
        /// Removes an object from the location permanently (no respawn).
        /// </summary>
        bool RemoveObject(string locationId, string objectId);

        /// <summary>
        /// Assigns or clears the character currently holding this object.
        /// A held object is excluded from <see cref="IWorldObjectProvider.GetObjectsAt"/>.
        /// </summary>
        bool SetHeldBy(string locationId, string objectId, HumanId? holder);

        /// <summary>
        /// Returns all objects currently held by the specified character,
        /// across all locations.
        /// </summary>
        IEnumerable<WorldObject> GetHeldBy(HumanId holder);
    }
}
