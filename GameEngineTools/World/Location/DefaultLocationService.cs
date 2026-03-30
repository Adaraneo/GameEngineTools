// DefaultLocationService.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Location
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Default implementation of <see cref="ILocationService"/>.
    /// Maintains a character→location map and derives noise/crowding
    /// from the static <see cref="LocationDescriptor"/> plus the live
    /// character count at each location.
    /// </summary>
    public sealed class DefaultLocationService : ILocationService
    {
        #region Public Methods
        /// <inheritdoc/>
        public IReadOnlyList<HumanId> GetCharactersAt(string locationId)
            => _characterLocation
                .Where(kv => kv.Value == locationId)
                .Select(kv => kv.Key)
                .ToList();

        /// <inheritdoc/>
        public IReadOnlyList<string> GetLocationsByType(LocationType type)
            => _descriptors.Values
                .Where(d => d.Type == type)
                .Select(d => d.Id)
                .ToList();
        #endregion

        #region Private state

        /// <summary>Registered location descriptors, keyed by location id.</summary>
        private readonly Dictionary<string, LocationDescriptor> _descriptors = new();

        /// <summary>Current location assignment per character.</summary>
        private readonly Dictionary<HumanId, string> _characterLocation = new();

        /// <summary>
        /// Tracks which location each character was in when we last dispatched events.
        /// Used to detect movement and avoid redundant dispatches.
        /// </summary>
        private readonly Dictionary<HumanId, string> _lastDispatchedLocation = new();

        #endregion

        #region ILocationService — registration

        /// <inheritdoc/>
        public void RegisterLocation(LocationDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            _descriptors[descriptor.Id] = descriptor;
        }

        /// <inheritdoc/>
        public void MoveCharacter(HumanId characterId, string locationId)
        {
            if (!_descriptors.ContainsKey(locationId))
                throw new InvalidOperationException(
                    $"Location '{locationId}' has not been registered. " +
                    $"Call RegisterLocation() before MoveCharacter().");

            _characterLocation[characterId] = locationId;
        }

        /// <inheritdoc/>
        public string? GetLocation(HumanId characterId)
            => _characterLocation.GetValueOrDefault(characterId);

        public LocationDescriptor? GetDescriptor(string locationId)
            => _descriptors.GetValueOrDefault(locationId);

        #endregion

        #region ILocationService — dispatch

        /// <inheritdoc/>
        public void DispatchContextEvents(
            WDateTime now,
            IReadOnlyList<IHuman> characters,
            bool forceAll = false)
        {
            // Build a snapshot: how many characters are at each location right now.
            // We use this to compute crowding for everyone at once.
            var countPerLocation = _characterLocation.Values
                .GroupBy(loc => loc)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var character in characters)
            {
                // Skip characters that have not been placed in any location yet.
                if (!_characterLocation.TryGetValue(character.Id, out var locationId))
                    continue;

                // Only dispatch if location changed — or if caller forces a full refresh.
                var previousLocation = _lastDispatchedLocation.GetValueOrDefault(character.Id);
                if (!forceAll && previousLocation == locationId)
                    continue;

                if (!_descriptors.TryGetValue(locationId, out var desc))
                    continue;

                var characterCount = countPerLocation.GetValueOrDefault(locationId, 1);

                var noise = ComputeNoise(desc, characterCount);
                var crowding = ComputeCrowding(desc, characterCount);
                var privacy = desc.AllowsPrivacy && characterCount <= 2;

                character.ReceiveEvent(new ContextChanged(
                    OccurredAt: now,
                    Human: character.Id,
                    Location: locationId,
                    HasPrivacy: privacy,
                    Noise: noise,
                    Crowding: crowding,
                    Kind: MapSurface(desc.Type)));

                _lastDispatchedLocation[character.Id] = locationId;
            }
        }

        #endregion

        #region Private — environment formulas

        /// <summary>
        /// Computes the noise level for a location given the current character count.
        /// </summary>
        /// <remarks>
        /// Formula: <c>BaseNoise + NoisePerPerson * characterCount</c>, clamped to [0, 1].
        /// Linear — each additional person adds a fixed amount.
        /// For logarithmic feel, swap for: <c>BaseNoise + NoisePerPerson * Log(count+1)</c>
        /// </remarks>
        /// <param name="desc">Static location descriptor.</param>
        /// <param name="characterCount">Number of characters currently at this location.</param>
        /// <returns>Noise level in [0, 1].</returns>
        private static double ComputeNoise(LocationDescriptor desc, int characterCount)
            => Math.Clamp(desc.BaseNoise + desc.NoisePerPerson * characterCount, 0.0, 1.0);

        /// <summary>
        /// Computes the crowding level for a location given the current character count.
        /// </summary>
        /// <remarks>
        /// Formula: <c>characterCount / Capacity</c>, clamped to [0, 1].
        /// At exactly <c>Capacity</c> characters the location is considered fully crowded (1.0).
        /// </remarks>
        /// <param name="desc">Static location descriptor.</param>
        /// <param name="characterCount">Number of characters currently at this location.</param>
        /// <returns>Crowding level in [0, 1].</returns>
        private static double ComputeCrowding(LocationDescriptor desc, int characterCount)
            => Math.Clamp((double)characterCount / desc.Capacity, 0.0, 1.0);

        private static SurfaceKind MapSurface(LocationType type)
            => type switch
            {
                LocationType.Social => SurfaceKind.Social,
                LocationType.Private => SurfaceKind.Private,
                LocationType.Work => SurfaceKind.Work,
                LocationType.Rest => SurfaceKind.Rest,
                LocationType.Public => SurfaceKind.Public,
                _ => SurfaceKind.Unknown
            };

        #endregion
    }
}
