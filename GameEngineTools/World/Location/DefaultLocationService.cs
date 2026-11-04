// DefaultLocationService.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Location
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Default implementation of <see cref="ILocationService"/>.
    /// Maintains a character→location map and derives noise/crowding
    /// from the static <see cref="LocationDescriptor"/> plus the live
    /// character count at each location.
    /// </summary>
    public sealed class DefaultLocationService : ILocationService
    {
        private readonly ISocialNormProvider? _normProvider;

        /// <summary>
        /// Creates a location service without norm lookup.
        /// Norm contexts will always be <c>null</c> in dispatched <see cref="ContextChanged"/> events.
        /// </summary>
        public DefaultLocationService()
        { }

        /// <summary>
        /// Creates a location service that resolves norm contexts from <paramref name="normProvider"/>.
        /// </summary>
        public DefaultLocationService(ISocialNormProvider normProvider)
        {
            _normProvider = normProvider;
        }

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

        #endregion Public Methods

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

        /// <summary>Noise/crowding/privacy each character was last told about, to re-announce when occupancy changes them materially.</summary>
        private readonly Dictionary<HumanId, (double Noise, double Crowding, bool Privacy)> _lastDispatchedEnvironment = new();

        private const double NoiseRedispatchDelta = 0.05;
        private const double CrowdingRedispatchDelta = 0.1;

        #endregion Private state

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
        public void ForgetDispatchState(HumanId characterId)
        {
            _lastDispatchedLocation.Remove(characterId);
            _lastDispatchedEnvironment.Remove(characterId);
        }

        /// <summary>Captures who stands where for a world save (characters in transit are not placed and so not listed).</summary>
        public IReadOnlyList<Persistence.PlacementEntry> SnapshotPlacements()
            => _characterLocation.Select(kv => new Persistence.PlacementEntry(kv.Key, kv.Value)).ToList();

        /// <summary>
        /// Replaces every placement with a saved one and forgets the dispatch cache, so the next dispatch announces
        /// each character's surroundings afresh.
        /// </summary>
        public void RestorePlacements(IEnumerable<Persistence.PlacementEntry> placements)
        {
            ArgumentNullException.ThrowIfNull(placements);
            _characterLocation.Clear();
            foreach (var p in placements)
                _characterLocation[p.Character] = p.LocationId;
            ResetDispatchState();
        }

        /// <inheritdoc/>
        public void ResetDispatchState()
        {
            _lastDispatchedLocation.Clear();
            _lastDispatchedEnvironment.Clear();
        }

        /// <inheritdoc/>
        public void RemoveCharacter(HumanId characterId)
            => _characterLocation.Remove(characterId);

        /// <inheritdoc/>
        public string? GetLocation(HumanId characterId)
            => _characterLocation.GetValueOrDefault(characterId);

        /// <inheritdoc/>
        public LocationDescriptor? GetDescriptor(string locationId)
            => _descriptors.GetValueOrDefault(locationId);

        #endregion ILocationService — registration

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
                var previousLocation = _lastDispatchedLocation.GetValueOrDefault(character.Id);

                // Unplaced (never placed, or currently in transit between locations).
                if (!_characterLocation.TryGetValue(character.Id, out var locationId))
                {
                    // Already known to be nowhere, or never placed → nothing to announce.
                    if (string.IsNullOrEmpty(previousLocation))
                        continue;

                    // Just left a real location: emit a one-shot "on the road" context (empty
                    // Location, no objects/privacy) so engines stop reading the old surface, then
                    // record the nowhere sentinel so it is not re-emitted every substep.
                    character.ReceiveEvent(new ContextChanged(
                        OccurredAt: now,
                        Human: character.Id,
                        Location: string.Empty,
                        HasPrivacy: false,
                        Noise: 0.0,
                        Crowding: 0.0,
                        Kind: SurfaceKind.Unknown,
                        NormContext: null));

                    _lastDispatchedLocation[character.Id] = string.Empty;
                    continue;
                }

                if (!_descriptors.TryGetValue(locationId, out var desc))
                    continue;

                var characterCount = countPerLocation.GetValueOrDefault(locationId, 1);

                var noise = ComputeNoise(desc, characterCount);
                var crowding = ComputeCrowding(desc, characterCount);
                var privacy = desc.AllowsPrivacy && characterCount <= 2;

                // Dispatch when the location changed, when the caller forces a full refresh, or when others arriving/leaving
                // changed the surface materially (a stale "private" or "quiet" would otherwise stay with the character).
                if (!forceAll
                    && previousLocation == locationId
                    && _lastDispatchedEnvironment.TryGetValue(character.Id, out var last)
                    && last.Privacy == privacy
                    && Math.Abs(last.Noise - noise) < NoiseRedispatchDelta
                    && Math.Abs(last.Crowding - crowding) < CrowdingRedispatchDelta)
                {
                    continue;
                }

                var normContext = desc.NormId is { } normId && _normProvider is not null
                    ? _normProvider.GetNormContext(normId)
                    : null;

                character.ReceiveEvent(new ContextChanged(
                    OccurredAt: now,
                    Human: character.Id,
                    Location: locationId,
                    HasPrivacy: privacy,
                    Noise: noise,
                    Crowding: crowding,
                    Kind: MapSurface(desc.Type),
                    NormContext: normContext));

                _lastDispatchedLocation[character.Id] = locationId;
                _lastDispatchedEnvironment[character.Id] = (noise, crowding, privacy);
            }
        }

        #endregion ILocationService — dispatch

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

        #endregion Private — environment formulas
    }
}
