// ObjectRespawnScheduler.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using System;
    using System.Linq;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Checks all consumed world objects each simulation tick and restores those
    /// whose respawn timer has elapsed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Driven by the simulation loop (e.g. <c>SimulationScene</c>).
    /// Call <see cref="Tick"/> once per simulation step.
    /// </para>
    /// <para>
    /// Depends on <see cref="IMutableWorldObjectProvider"/>
    /// and <see cref="SqliteWorldObjectProvider"/>.
    /// </para>
    /// </remarks>
    public sealed class ObjectRespawnScheduler
    {
        #region Private state

        /// <summary>
        /// Mutable provider used to inspect consumed objects and restore them.
        /// Requires <see cref="IMutableWorldObjectProvider.GetKnownLocationIds"/> and
        /// <see cref="IMutableWorldObjectProvider.GetAllObjectsAt"/> — both are part
        /// of the mutable contract, not the read-only <see cref="IWorldObjectProvider"/>.
        /// </summary>
        private readonly IMutableWorldObjectProvider _provider;

        #endregion Private state

        #region Construction

        /// <summary>
        /// Initialises the scheduler with the mutable world object provider.
        /// </summary>
        /// <param name="provider">
        /// The mutable provider. Injected as singleton — must be the same instance
        /// used by the simulation engines.
        /// </param>
        public ObjectRespawnScheduler(IMutableWorldObjectProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            _provider = provider;
        }

        #endregion Construction

        #region Public API

        /// <summary>
        /// Inspects all consumed objects across all known locations and restores
        /// any that have exceeded their <see cref="WorldObject.RespawnMinutes"/> timer.
        /// </summary>
        /// <param name="now">Current simulation time used to evaluate elapsed duration.</param>
        public void Tick(WDateTime now)
        {
            foreach (var locationId in _provider.GetKnownLocationIds())
            {
                // GetAllObjectsAt includes consumed and held objects — required
                // for respawn inspection. GetObjectsAt filters them out.
                var all = _provider.GetAllObjectsAt(locationId).ToList();

                foreach (var obj in all)
                {
                    // Only process consumed objects that are configured to respawn.
                    if (obj.ConsumedAt is null || !obj.Respawns)
                        continue;

                    var elapsed = WDateTime.Difference(now, obj.ConsumedAt.Value).TotalMinutes;

                    if (elapsed >= obj.RespawnMinutes)
                        _provider.RestoreObject(locationId, obj.Id);
                }
            }
        }

        #endregion Public API
    }
}
