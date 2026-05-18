// StaticWorldObjectProvider.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Phase 1 implementation of <see cref="IWorldObjectProvider"/>.
    /// Serves a fixed, pre-registered set of world objects.
    /// </summary>
    /// <remarks>
    /// Objects are registered at startup via <see cref="Register"/>.
    /// This is intentionally simple — the complexity lives in the
    /// <see cref="WorldObject"/> descriptors themselves.
    /// </remarks>
    public sealed class StaticWorldObjectProvider : IWorldObjectProvider
    {
        #region Private state

        /// <summary>All registered world objects, keyed by object ID.</summary>
        private readonly Dictionary<string, WorldObject> _objects = new();

        #endregion

        #region Registration

        /// <summary>
        /// Registers a world object, making it available to the simulation engine.
        /// </summary>
        /// <param name="obj">The world object to register.</param>
        /// <exception cref="ArgumentException">
        /// Thrown if an object with the same ID is already registered.
        /// </exception>
        public void Register(WorldObject obj)
        {
            ArgumentNullException.ThrowIfNull(obj);

            if (!_objects.TryAdd(obj.Id, obj))
                throw new ArgumentException(
                    $"WorldObject with ID '{obj.Id}' is already registered.", nameof(obj));
        }

        /// <summary>
        /// Updates the availability flag of a registered object.
        /// Use this to mark objects as occupied, broken, or inaccessible at runtime.
        /// </summary>
        /// <param name="objectId">The ID of the object to update.</param>
        /// <param name="isAvailable">New availability state.</param>
        /// <exception cref="KeyNotFoundException">
        /// Thrown if no object with the given ID is registered.
        /// </exception>
        public void SetAvailability(string objectId, bool isAvailable)
        {
            if (!_objects.TryGetValue(objectId, out var existing))
                throw new KeyNotFoundException(
                    $"WorldObject with ID '{objectId}' is not registered.");

            // Records are immutable — replace with updated copy via 'with' expression.
            _objects[objectId] = existing with { IsAvailable = isAvailable };
        }

        #endregion

        #region IWorldObjectProvider

        /// <inheritdoc/>
        public IEnumerable<WorldObject> GetObjectsAt(string locationId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locationId);

            return _objects.Values
                .Where(o => string.Equals(o.LocationId, locationId, StringComparison.Ordinal));
        }

        /// <inheritdoc/>
        public IEnumerable<WorldObject> GetAllObjects()
            => _objects.Values;

        /// <inheritdoc/>
        /// <remarks>
        /// <see cref="StaticWorldObjectProvider"/> does not support runtime addition.
        /// Objects are defined at construction time via <see cref="Register"/>.
        /// This is a no-op for test/static scenarios.
        /// </remarks>
        public void AddObject(WorldObject obj)
        {
            // No-op: StaticWorldObjectProvider serves pre-registered objects only.
        }

        #endregion
    }
}
