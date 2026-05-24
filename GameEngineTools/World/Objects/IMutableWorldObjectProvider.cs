// IMutableWorldObjectProvider.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Extends <see cref="IWorldObjectProvider"/> with runtime mutation and
    /// respawn-inspection operations required by the interaction and respawn engines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementation <see cref="SqliteWorldObjectProvider"/>
    /// </para>
    /// <para>
    /// <b>Why not put these on <see cref="IWorldObjectProvider"/>?</b><br/>
    /// <see cref="IWorldObjectProvider"/> is a read-only query contract consumed by simulation
    /// engines. Mutation methods belong to a separate interface so read-only consumers
    /// (e.g. <see cref="ContingencySearchEngine"/>) do not need to know about mutations.
    /// </para>
    /// </remarks>
    public interface IMutableWorldObjectProvider : IWorldObjectProvider
    {
        #region Runtime mutations

        /// <summary>
        /// Marks an object as consumed at the given simulation time.
        /// The object is excluded from <see cref="IWorldObjectProvider.GetObjectsAt"/>
        /// until respawned or explicitly restored.
        /// </summary>
        /// <param name="locationId">Location where the object resides.</param>
        /// <param name="objectId">Unique object identifier.</param>
        /// <param name="now">Simulation time of consumption, used for respawn timing.</param>
        /// <returns><c>true</c> if the object was found and marked; <c>false</c> otherwise.</returns>
        bool ConsumeObject(string locationId, string objectId, WDateTime now);

        /// <summary>
        /// Clears the held and consumed state, making the object available again.
        /// </summary>
        /// <param name="locationId">Location where the object resides.</param>
        /// <param name="objectId">Unique object identifier.</param>
        /// <returns><c>true</c> if the object was found and restored; <c>false</c> otherwise.</returns>
        bool RestoreObject(string locationId, string objectId);

        /// <summary>
        /// Permanently removes an object from storage (no respawn).
        /// </summary>
        /// <param name="locationId">Location where the object resides.</param>
        /// <param name="objectId">Unique object identifier.</param>
        /// <returns><c>true</c> if the object was found and removed; <c>false</c> otherwise.</returns>
        bool RemoveObject(string locationId, string objectId);

        /// <summary>
        /// Assigns or clears the character currently holding this object.
        /// A held object is excluded from <see cref="IWorldObjectProvider.GetObjectsAt"/>.
        /// </summary>
        /// <param name="locationId">Location where the object resides.</param>
        /// <param name="objectId">Unique object identifier.</param>
        /// <param name="holder"><c>null</c> to release the hold; a <see cref="HumanId"/> to assign.</param>
        /// <returns><c>true</c> if the object was found and updated; <c>false</c> otherwise.</returns>
        bool SetHeldBy(string locationId, string objectId, HumanId? holder);

        /// <summary>
        /// Returns all objects currently held by the specified character, across all locations.
        /// </summary>
        IEnumerable<WorldObject> GetHeldBy(HumanId holder);

        #endregion Runtime mutations

        #region Respawn inspection

        /// <summary>
        /// Returns all location IDs known to this provider.
        /// Used by <see cref="ObjectRespawnScheduler"/> to enumerate consumed objects
        /// without a full table scan.
        /// </summary>
        IEnumerable<string> GetKnownLocationIds();

        /// <summary>
        /// Returns ALL objects at the given location — including consumed and held ones.
        /// Unlike <see cref="IWorldObjectProvider.GetObjectsAt"/>, which filters them out.
        /// Used exclusively by <see cref="ObjectRespawnScheduler"/> to inspect respawn timers.
        /// </summary>
        /// <param name="locationId">Location to query.</param>
        IEnumerable<WorldObject> GetAllObjectsAt(string locationId);

        #endregion Respawn inspection
    }
}
