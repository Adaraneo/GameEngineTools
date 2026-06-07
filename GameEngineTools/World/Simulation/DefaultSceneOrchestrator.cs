// DefaultSceneOrchestrator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Simulation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Transactions;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Attraction;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Movement;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Default social orchestration layer for <see cref="SimulationScene.OnTick"/>.
    /// Encapsulates first-impression firing, NPC movement routing, reach-out routing,
    /// and organic micro-positive generation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this class exists:</b><br/>
    /// Without this class, every consumer of GET (Unity, GameSandbox, test harnesses)
    /// would have to re-implement — and re-optimise — the same social orchestration
    /// logic. <see cref="DefaultSceneOrchestrator"/> provides a ready-made, correctly
    /// optimised implementation that can be used as-is or replaced entirely.
    /// </para>
    /// <para>
    /// <b>Usage:</b>
    /// <code>
    /// var orchestrator = new DefaultSceneOrchestrator(...);
    ///
    /// var scene = new SimulationScene(clock, new SimulationSceneOptions
    /// {
    ///     OnTick = orchestrator.OnTick
    /// }, lodRuntime);
    /// </code>
    /// </para>
    /// <para>
    /// <b>What this class does NOT handle:</b>
    /// <list type="bullet">
    ///   <item>Family bond creation on birth — use <c>FamilyOrchestrator</c> or handle in your GameManager.</item>
    ///   <item>Scenario scripting (e.g. "move player to castle on day 16") — belongs in your OnTick extension.</item>
    ///   <item>Diary / narrative export — belongs in the consuming application.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>LOD behaviour:</b><br/>
    /// The orchestrator reads <see cref="ICognitiveResolutionLevelRuntime"/> each tick.
    /// <c>DynamicReachOutRouting</c> and <c>OrganicMicroPositives</c> are skipped for
    /// <see cref="CognitiveResolutionLevel.Background"/> characters — they lack the
    /// cognitive capacity modelled by <c>Coarse</c> perception. First impressions and
    /// movement routing run for all characters regardless of LOD.
    /// </para>
    /// <para>
    /// <b>Performance:</b><br/>
    /// <see cref="FireFirstImpressions"/> uses an internal saturation cache
    /// (<c>_fullyMetLocations</c>) to skip the O(k²) perception scan for locations
    /// where every co-located pair has already met. Call <see cref="InvalidateLocation"/>
    /// whenever you place a new character into a location outside the orchestrator
    /// (e.g. when handling a <c>ChildBorn</c> event).
    /// </para>
    /// <para>
    /// <b>DI registration:</b><br/>
    /// Register as <c>Transient</c> — one instance per scene. Each instance owns its
    /// own saturation cache and reusable buffers.
    /// </para>
    /// </remarks>
    public sealed class DefaultSceneOrchestrator
    {
        #region Private fields

        private readonly IAttractionCalculator _attractionCalculator;
        private readonly ILocationService _locationService;
        private readonly IPerceptionFidelityPolicy _perceptionPolicy;
        private readonly CharacterPerceptionOptions _perceptionOptions;
        private readonly ICognitiveResolutionLevelRuntime _lodRuntime;
        private readonly WorldMap _worldMap;
        private readonly IMovementSpeedProvider _speedProvider;
        private readonly Random _rng;
        private readonly ILogger<DefaultSceneOrchestrator> _log;

        /// <summary>
        /// Tracks location ids where every perceivable pair of co-located characters
        /// has already formed a first impression. Skipping the O(k²) scan for these
        /// locations is the primary performance optimisation for large background scenes.
        /// Invalidated by <see cref="InvalidateLocation"/> and internally by
        /// <see cref="RouteMoveTo"/> on every character arrival.
        /// </summary>
        private readonly HashSet<string> _fullyMetLocations = new(StringComparer.Ordinal);

        /// <summary>
        /// Reusable buffer for the LOD-gated social character subset.
        /// Pre-allocated once; cleared and refilled on every <see cref="OnTick"/> call
        /// to avoid per-substep heap allocations.
        /// </summary>
        private readonly List<IHuman> _socialCharsBuffer = new();

        /// <summary>
        /// Optional world object provider used to locate food and drink objects
        /// across the scene when routing <c>MoveTo:Food</c> and <c>MoveTo:Drink</c> actions.
        /// <c>null</c> disables object-category movement routing.
        /// </summary>
        private readonly IWorldObjectProvider _objectProvider;

        /// <summary>Tunable scalar parameters for this orchestrator instance.</summary>
        private readonly SceneOrchestratorOptions _options;

        #endregion Private fields

        #region Constructor

        /// <summary>
        /// Creates a new <see cref="DefaultSceneOrchestrator"/>.
        /// All dependencies are mandatory — the orchestrator does not provide fallbacks.
        /// </summary>
        /// <param name="attractionCalculator">Shared attraction calculator singleton.</param>
        /// <param name="locationService">Location service for placement and co-location queries.</param>
        /// <param name="perceptionPolicy">Runtime perception fidelity policy (LOD-backed).</param>
        /// <param name="perceptionOptions">Noise and crowding thresholds for perception tiers.</param>
        /// <param name="lodRuntime">Runtime LOD registry used to gate social functions.</param>
        /// <param name="worldMap">World adjacency graph for movement routing.</param>
        /// <param name="speedProvider">Provides movement speed in metres per minute.</param>
        /// <param name="rng">Random source shared across all orchestration methods.</param>
        /// <param name="log">Logger for reach-out diagnostics and movement warnings.</param>
        /// <param name="objectProvider">
        /// World object provider used to locate food and drink objects
        /// across the scene when routing <c>MoveTo:Food</c> and <c>MoveTo:Drink</c> actions.
        /// </param>
        /// <param name="options">Tunable scalar parameters (defaults preserve legacy behavior).</param>
        public DefaultSceneOrchestrator(
            IAttractionCalculator attractionCalculator,
            ILocationService locationService,
            IPerceptionFidelityPolicy perceptionPolicy,
            CharacterPerceptionOptions perceptionOptions,
            ICognitiveResolutionLevelRuntime lodRuntime,
            WorldMap worldMap,
            IMovementSpeedProvider speedProvider,
            Random rng,
            ILogger<DefaultSceneOrchestrator> log,
            IWorldObjectProvider objectProvider,
            SceneOrchestratorOptions options)
        {
            _attractionCalculator = attractionCalculator;
            _locationService = locationService;
            _perceptionPolicy = perceptionPolicy;
            _perceptionOptions = perceptionOptions;
            _lodRuntime = lodRuntime;
            _worldMap = worldMap;
            _speedProvider = speedProvider;
            _rng = rng;
            _log = log;
            _objectProvider = objectProvider;
            _options = options;
        }

        #endregion Constructor

        #region Public API

        /// <summary>
        /// Main orchestration entry point. Assign this to
        /// <see cref="SimulationSceneOptions.OnTick"/> to activate the orchestrator.
        /// </summary>
        /// <remarks>
        /// Execution order per substep:
        /// <list type="number">
        ///   <item>LOD-gated character split — O(N), single pass.</item>
        ///   <item><see cref="FireFirstImpressions"/> — all chars, cache-optimised.</item>
        ///   <item><see cref="RouteMoveTo"/> — all chars.</item>
        ///   <item><see cref="DynamicReachOutRouting"/> — Nearby / Player chars only.</item>
        ///   <item><see cref="OrganicMicroPositives"/> — Nearby / Player chars only.</item>
        /// </list>
        /// </remarks>
        /// <param name="now">Current simulation time.</param>
        /// <param name="chars">All characters in the scene for this substep.</param>
        public void OnTick(WDateTime now, IReadOnlyList<IHuman> chars)
        {
            // ── LOD-gated character split — single O(N) pass ──────────────────────
            // lodRuntime.Get() is an O(1) ConcurrentDictionary lookup.
            // We separate chars into two buckets here so downstream methods do not
            // each repeat the lodRuntime query.
            //
            // Background → first impressions + movement only.
            //              No active social initiative (ReachOut, MicroPositives).
            // Nearby/Player → full social participation.
            _socialCharsBuffer.Clear();

            foreach (var c in chars)
            {
                if (_lodRuntime.Get(c.Id) != CognitiveResolutionLevel.Background)
                    _socialCharsBuffer.Add(c);
            }

            // ── Social functions ──────────────────────────────────────────────────
            FireFirstImpressions(now, chars);
            RouteMoveTo(now, chars);

            if (_socialCharsBuffer.Count > 0)
            {
                DynamicReachOutRouting(now, _socialCharsBuffer);
                OrganicMicroPositives(now, _socialCharsBuffer);
            }
        }

        /// <summary>
        /// Removes a location from the first-impression saturation cache.
        /// Call this whenever you place a new character into a location outside the
        /// orchestrator — for example when handling a <c>ChildBorn</c> event and
        /// placing the newborn at the mother's location.
        /// </summary>
        /// <param name="locationId">The location id to invalidate.</param>
        public void InvalidateLocation(string locationId)
            => _fullyMetLocations.Remove(locationId);

        #endregion Public API

        #region FireFirstImpressions

        /// <summary>
        /// Fires <see cref="FirstImpressionFormed"/> for every perceivable pair of
        /// characters that share a location and have not yet met.
        /// Each ordered pair (A→B and B→A) is processed exactly once per call.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Saturation cache:</b><br/>
        /// <c>_fullyMetLocations</c> skips the entire per-location perception scan once
        /// every perceivable pair in that location has met. The cache entry is added when
        /// a location pass produces zero new impressions, and removed when a character
        /// moves into the location (via <see cref="RouteMoveTo"/> or
        /// <see cref="InvalidateLocation"/>).
        /// </para>
        /// <para>
        /// <b>Per-location perceivedBy — O(k²) not O(N²):</b><br/>
        /// <c>GetPerceivedCharacters</c> receives <c>localChars</c> (already filtered to
        /// the location) instead of all <c>chars</c>. This reduces the internal scan
        /// from O(N) to O(k) per call, where k is the number of characters at the location.
        /// </para>
        /// </remarks>
        private void FireFirstImpressions(WDateTime now, IReadOnlyList<IHuman> chars)
        {
            // Scene-wide id → IHuman lookup for O(1) resolution inside the pair loop.
            var byId = chars.ToDictionary(c => c.Id);

            var occupiedLocations = chars
                .Select(c => _locationService.GetLocation(c.Id))
                .Where(loc => loc is not null)
                .Distinct()!;

            foreach (var locationId in occupiedLocations)
            {
                // Fast path: location is saturated — every perceivable pair has met.
                // Skip the perceivedBy scan entirely.
                if (_fullyMetLocations.Contains(locationId))
                    continue;

                // Resolve local characters from the location service.
                // Passing localChars to GetPerceivedCharacters makes its internal
                // co-location filter O(k) instead of O(N).
                var localChars = _locationService
                    .GetCharactersAt(locationId)
                    .Select(id => byId.TryGetValue(id, out var h) ? h : null)
                    .OfType<IHuman>()
                    .ToList();

                if (localChars.Count < 2)
                {
                    // Single occupant — nothing to pair, mark saturated immediately.
                    _fullyMetLocations.Add(locationId);
                    continue;
                }

                // Build perceivedBy only for this location's characters — O(k²) not O(N²).
                var perceivedBy = localChars.ToDictionary(
                    c => c.Id,
                    c => CharacterPerceptionResolver
                        .GetPerceivedCharacters(c, localChars, _locationService, _perceptionPolicy, _perceptionOptions)
                        .Select(x => x.Id)
                        .ToHashSet());

                var anyNewImpression = false;

                // Unique-pair loop: (i, j) with j > i — no duplicates, no self-pairs.
                for (var i = 0; i < localChars.Count; i++)
                {
                    for (var j = i + 1; j < localChars.Count; j++)
                    {
                        var a = localChars[i];
                        var b = localChars[j];

                        var aSeesB = perceivedBy[a.Id].Contains(b.Id);
                        var bSeesA = perceivedBy[b.Id].Contains(a.Id);

                        // ── Mutual perception → bilateral FirstImpressionFormed ─────────────────
                        // Both characters notice each other simultaneously.
                        // Skip if the observing side already has an edge — avoids re-firing.
                        if (aSeesB && bSeesA)
                        {
                            var aAlreadyKnowsB = a.Snapshot.Relationships.Edges.ContainsKey(b.Id);
                            var bAlreadyKnowsA = b.Snapshot.Relationships.Edges.ContainsKey(a.Id);

                            if (aAlreadyKnowsB && bAlreadyKnowsA)
                                continue;

                            anyNewImpression = true;

                            var viewA = AppearanceProjector.Compute(
                                a.PhysicalAppearance, a.Snapshot.Physiology, a.Biology, a.Snapshot.Physiology.Aging);
                            var viewB = AppearanceProjector.Compute(
                                b.PhysicalAppearance, b.Snapshot.Physiology, b.Biology, b.Snapshot.Physiology.Aging);

                            // A sees B.
                            var aResult = a.AttractionProfile is not null
                                ? _attractionCalculator.Calculate(
                                    a.AttractionProfile, b.PhysicalAppearance, viewB, b.Biology,
                                    observerValence: a.Snapshot.Psychology.Valence,
                                    observerArousal: a.Snapshot.Psychology.Arousal,
                                    observerAgeYears: a.Age,
                                    targetAgeYears: b.Age)
                                : AttractionResult.Neutral;

                            // B sees A.
                            var bResult = b.AttractionProfile is not null
                                ? _attractionCalculator.Calculate(
                                    b.AttractionProfile, a.PhysicalAppearance, viewA, a.Biology,
                                    observerValence: b.Snapshot.Psychology.Valence,
                                    observerArousal: b.Snapshot.Psychology.Arousal,
                                    observerAgeYears: b.Age,
                                    targetAgeYears: a.Age)
                                : AttractionResult.Neutral;

                            if (!aAlreadyKnowsB)
                                a.ReceiveEvent(new FirstImpressionFormed(now, a.Id, b.Id,
                                    aResult.FirstImpressionLike, aResult.Score,
                                    aResult.BasePhysical, aResult.PreferenceMatch));

                            if (!bAlreadyKnowsA)
                                b.ReceiveEvent(new FirstImpressionFormed(now, b.Id, a.Id,
                                    bResult.FirstImpressionLike, bResult.Score,
                                    bResult.BasePhysical, bResult.PreferenceMatch));

                            continue;
                        }

                        // ── Asymmetric perception → one-sided OneWayObservationFormed ───────────
                        // Only the perceiving side gets a weak edge — target is unaware.
                        // anyNewImpression = true prevents false saturation caching:
                        // next substep B might finally see A → bilateral must be allowed to fire.
                        if (aSeesB && !a.Snapshot.Relationships.Edges.ContainsKey(b.Id))
                        {
                            anyNewImpression = true;

                            var viewB = AppearanceProjector.Compute(
                                b.PhysicalAppearance, b.Snapshot.Physiology, b.Biology, b.Snapshot.Physiology.Aging);

                            var aResult = a.AttractionProfile is not null
                                ? _attractionCalculator.Calculate(
                                    a.AttractionProfile, b.PhysicalAppearance, viewB, b.Biology,
                                    observerValence: a.Snapshot.Psychology.Valence,
                                    observerArousal: a.Snapshot.Psychology.Arousal,
                                    observerAgeYears: a.Age,
                                    targetAgeYears: b.Age)
                                : AttractionResult.Neutral;

                            a.ReceiveEvent(new OneWayObservationFormed(
                                now, a.Id, b.Id,
                                Like: aResult.FirstImpressionLike,
                                Attraction: aResult.Score,
                                TargetBiology: b.Biology,
                                BasePhysical: aResult.BasePhysical,
                                PreferenceMatch: aResult.PreferenceMatch));
                        }

                        if (bSeesA && !b.Snapshot.Relationships.Edges.ContainsKey(a.Id))
                        {
                            anyNewImpression = true;

                            var viewA = AppearanceProjector.Compute(
                                a.PhysicalAppearance, a.Snapshot.Physiology, a.Biology, a.Snapshot.Physiology.Aging);

                            var bResult = b.AttractionProfile is not null
                                ? _attractionCalculator.Calculate(
                                    b.AttractionProfile, a.PhysicalAppearance, viewA, a.Biology,
                                    observerValence: b.Snapshot.Psychology.Valence,
                                    observerArousal: b.Snapshot.Psychology.Arousal,
                                    observerAgeYears: b.Age,
                                    targetAgeYears: a.Age)
                                : AttractionResult.Neutral;

                            b.ReceiveEvent(new OneWayObservationFormed(
                                now, b.Id, a.Id,
                                Like: bResult.FirstImpressionLike,
                                Attraction: bResult.Score,
                                TargetBiology: a.Biology,
                                BasePhysical: bResult.BasePhysical,
                                PreferenceMatch: bResult.PreferenceMatch));
                        }
                    }
                }

                // No new impression fired → all perceivable pairs in this location have met.
                // Mark as saturated; future substeps skip the perceivedBy scan here.
                if (!anyNewImpression)
                    _fullyMetLocations.Add(locationId);
            }
        }

        #endregion FireFirstImpressions

        #region RouteMoveTo

        /// <summary>
        /// Routes <c>MoveTo:*</c> actions emitted by <see cref="DefaultBehaviorEngine"/>
        /// to a concrete location via <see cref="ILocationService"/>.
        /// </summary>
        /// <remarks>
        /// Resolves a concrete destination using the world adjacency graph — adjacent
        /// locations of the requested type are preferred, ordered by travel duration at
        /// the character's current movement speed. Falls back to any registered location
        /// of the requested type when no adjacent match exists.
        /// When a character is moved, the destination is removed from
        /// <c>_fullyMetLocations</c> so <see cref="FireFirstImpressions"/> rescans it
        /// next substep — the arriving character may be a stranger to everyone there.
        /// </remarks>
        private void RouteMoveTo(WDateTime now, IReadOnlyList<IHuman> chars)
        {
            foreach (var character in chars)
            {
                var moveTo = character.LastOutbox
                    .OfType<ActionCommitted>()
                    .FirstOrDefault(a => a.ActionName.StartsWith("MoveTo:"));

                if (moveTo is null)
                    continue;

                var currentLocation = _locationService.GetLocation(character.Id);

                // ── Object-category foraging movement (MoveTo:Food, MoveTo:Drink) ──────────
                // These bypass LocationType routing and search by required object category.
                if (moveTo.ActionName is MoveToFood or MoveToDrink)
                {
                    var category = moveTo.ActionName == MoveToFood
                        ? WorldObjectCategory.Food
                        : WorldObjectCategory.Drink;

                    // Map category to PickupItemKind for memory lookup.
                    var itemKind = category == WorldObjectCategory.Food
                        ? PickupItemKind.Food
                        : PickupItemKind.Drink;

                    // Try memory-based routing first — character navigates where they remember seeing food.
                    // Falls back to omniscient provider when memory is empty or all facts are stale.
                    var destination =
                        FindLocationWithCategoryViaMemory(character, currentLocation, itemKind)
                        ?? FindLocationWithCategory(character, currentLocation, category);

                    if (destination is not null)
                    {
                        _locationService.MoveCharacter(character.Id, destination);
                        _fullyMetLocations.Remove(destination);
                    }
                    else
                    {
                        using (_log.BeginCharacterScope(character.Id.Value, "SceneOrchestrator"))
                        {
                            _log.SceneMoveToFailed(character.Id.Value.ToString(), category.ToString());
                            //_log.LogDebug(
                            //    "[MoveTo] {CharId} needs {Category} but found no location via memory or provider.",
                            //    character.Id.Value, category);
                        }
                    }

                    continue;
                }

                // ── Standard LocationType movement ─────────────────────────────────────────
                var typeName = moveTo.ActionName["MoveTo:".Length..];
                if (!Enum.TryParse<LocationType>(typeName, out var requestedType))
                    continue;

                var chosen = FindBestMoveTarget(character, currentLocation, requestedType);

                if (chosen is null)
                {
                    using (_log.BeginCharacterScope(character.Id.Value, "SceneOrchestrator"))
                    {
                        _log.SceneMoveToRequested(character.Id.Value.ToString(), requestedType.ToString(), currentLocation?? "<none>");
                        Console.WriteLine("[MoveTo] {0} requested {1} but no suitable location exists. Current location: {2}.", character.Id.Value.ToString(), requestedType.ToString(), currentLocation ?? "<none>");
                    }
                    continue;
                }

                _locationService.MoveCharacter(character.Id, chosen);
                _fullyMetLocations.Remove(chosen);
            }
        }

        /// <summary>
        /// Resolves the best move destination for a character requesting a given location type.
        /// Adjacent locations are preferred over distant ones; heavily crowded locations are avoided.
        /// </summary>
        /// <param name="character">Character requesting the move.</param>
        /// <param name="currentLocationId">Character's current location, or <c>null</c> if unplaced.</param>
        /// <param name="requestedType">The type of location the character wants to reach.</param>
        /// <returns>The chosen location id, or <c>null</c> if no suitable location exists.</returns>
        private string? FindBestMoveTarget(
            IHuman character,
            string? currentLocationId,
            LocationType requestedType)
        {
            if (currentLocationId is null)
                return null;

            var speed = _speedProvider.GetSpeedMetersPerMinute(character.Snapshot);

            // Prefer adjacent locations of the requested type, ordered by travel time.
            var adjacentTarget = _worldMap
                .GetConnections(currentLocationId)
                .Select(conn => (conn, descriptor: _worldMap.GetLocation(conn.TargetLocationId)))
                .Where(t => t.descriptor?.Type == requestedType)
                .OrderBy(t => TravelDurationComputer.ComputeMinutes(t.conn.DistanceMeters, speed))
                .Select(t => t.conn.TargetLocationId)
                .FirstOrDefault();

            if (adjacentTarget is not null)
                return adjacentTarget;

            // Fallback: any registered location of the requested type, scored by
            // noise / crowding / privacy heuristics. Excludes the current location.
            var candidates = _locationService
                .GetLocationsByType(requestedType)
                .Where(id => id != currentLocationId)
                .Select(id => new
                {
                    Id = id,
                    Descriptor = _locationService.GetDescriptor(id),
                    Occupants = _locationService.GetCharactersAt(id).Count
                })
                .Where(x => x.Descriptor is not null)
                .Select(x => new
                {
                    x.Id,
                    Noise = Math.Clamp(
                        x.Descriptor!.BaseNoise + x.Descriptor.NoisePerPerson * x.Occupants,
                        0.0, 1.0),
                    Crowding = Math.Clamp(
                        x.Descriptor!.Capacity > 0
                            ? (double)x.Occupants / x.Descriptor.Capacity
                            : 1.0,
                        0.0, 1.0),
                    Privacy = x.Descriptor!.AllowsPrivacy && x.Occupants <= 1
                })
                .ToList();

            if (candidates.Count == 0)
                return null;

            var best = candidates
                .OrderByDescending(x => ScoreMoveTarget(character, requestedType, x.Noise, x.Crowding, x.Privacy))
                .First();

            return best.Id;
        }

        /// <summary>
        /// Finds the best location that has at least one available object of the requested category.
        /// Adjacent locations are preferred over distant ones.
        /// Returns <c>null</c> when no provider is wired or no suitable location exists.
        /// </summary>
        /// <param name="character">The character that is about to move.</param>
        /// <param name="currentLocationId">Character's current location, or <c>null</c> if unplaced.</param>
        /// <param name="category">Required object category (e.g. Food, Drink).</param>
        private string? FindLocationWithCategory(
            IHuman character,
            string? currentLocationId,
            WorldObjectCategory category)
        {
            if (currentLocationId is null)
                return null;

            var speed = _speedProvider.GetSpeedMetersPerMinute(character.Snapshot);

            // Build the set of locations that have the required category, excluding the current one.
            var candidateLocations = _objectProvider
                .GetAllObjects()
                .Where(o => o.Category == category && o.IsAvailable && o.LocationId != currentLocationId)
                .Select(o => o.LocationId)
                .ToHashSet(StringComparer.Ordinal);

            if (candidateLocations.Count == 0)
                return null;

            // Prefer the nearest adjacent location that has the required objects.
            var adjacentMatch = _worldMap
                .GetConnections(currentLocationId)
                .Where(conn => candidateLocations.Contains(conn.TargetLocationId))
                .OrderBy(conn => TravelDurationComputer.ComputeMinutes(conn.DistanceMeters, speed))
                .Select(conn => conn.TargetLocationId)
                .FirstOrDefault();

            if (adjacentMatch is not null)
                return adjacentMatch;

            // Fallback: pick arbitrarily from all known locations with the required object.
            // Could be improved with distance scoring if WorldMap exposes path lengths.
            return candidateLocations.First();
        }

        /// <summary>
        /// Attempts to route a foraging character to a location they remember
        /// containing the required object category.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Reads <see cref="MemoryIndex.KnownObjects"/> from the character's snapshot.
        /// Only facts with confidence above <see cref="SceneOrchestratorOptions.MinMemoryConfidence"/> are considered —
        /// stale or nearly-forgotten memories are ignored.
        /// </para>
        /// <para>
        /// Prefers adjacent locations over distant ones (same adjacency logic as
        /// <see cref="FindLocationWithCategory"/>). Returns <c>null</c> when the character
        /// has no qualifying memory, signaling the caller to fall back to the global provider.
        /// </para>
        /// </remarks>
        /// <param name="character">The character navigating toward food/drink.</param>
        /// <param name="currentLocationId">Character's current location ID.</param>
        /// <param name="itemKind">The kind of item being sought (Food or Drink).</param>
        /// <returns>
        /// The best remembered location ID, or <c>null</c> when memory is empty or
        /// all facts are below the confidence threshold.
        /// </returns>
        private string? FindLocationWithCategoryViaMemory(
            IHuman character,
            string? currentLocationId,
            PickupItemKind itemKind)
        {
            if (currentLocationId is null)
                return null;

            var knownObjects = character.Snapshot.Memory.KnownObjects;

            if (knownObjects.Count == 0)
                return null;

            // Collect distinct locations where the character remembers seeing this kind of item,
            // filtered by minimum confidence threshold to exclude nearly-forgotten facts.
            var rememberedLocations = knownObjects
                .Where(f => f.ItemKind == itemKind
                         && f.Confidence >= _options.MinMemoryConfidence
                         && f.LocationId != currentLocationId)
                .GroupBy(f => f.LocationId)
                .Select(g => (
                    LocationId: g.Key,
                    BestConfidence: g.Max(f => f.Confidence)))
                .ToList();

            if (rememberedLocations.Count == 0)
                return null;

            var speed = _speedProvider.GetSpeedMetersPerMinute(character.Snapshot);

            // Prefer an adjacent location the character remembers — nearest + highest confidence.
            var adjacentConnections = _worldMap.GetConnections(currentLocationId);

            var adjacentMatch = adjacentConnections
                .Where(conn => rememberedLocations.Any(r => r.LocationId == conn.TargetLocationId))
                .Select(conn => (
                    conn.TargetLocationId,
                    TravelMinutes: TravelDurationComputer.ComputeMinutes(conn.DistanceMeters, speed),
                    BestConfidence: rememberedLocations.First(r => r.LocationId == conn.TargetLocationId).BestConfidence))
                .OrderBy(x => x.TravelMinutes)
                .ThenByDescending(x => x.BestConfidence)
                .Select(x => x.TargetLocationId)
                .FirstOrDefault();

            if (adjacentMatch is not null)
                return adjacentMatch;

            // Fallback within memory: non-adjacent remembered location with best confidence.
            return rememberedLocations
                .OrderByDescending(r => r.BestConfidence)
                .Select(r => r.LocationId)
                .First();
        }

        /// <summary>
        /// Scores a candidate location for a given character and requested location type.
        /// Higher score = better match for the character's current needs.
        /// </summary>
        private static double ScoreMoveTarget(
            IHuman character,
            LocationType requestedType,
            double noise,
            double crowding,
            bool privacy)
        {
            var stress = character.Snapshot.Psychology.Stress / 100.0;

            return requestedType switch
            {
                LocationType.Work =>
                    (1.0 - noise) * 0.45 +
                    (1.0 - crowding) * 0.40 +
                    (privacy ? 0.15 : 0.0),

                LocationType.Rest =>
                    (1.0 - noise) * 0.50 +
                    (1.0 - crowding) * 0.20 +
                    (privacy ? 0.30 : 0.0) +
                    stress * 0.15,

                LocationType.Private =>
                    (1.0 - noise) * 0.35 +
                    (1.0 - crowding) * 0.20 +
                    (privacy ? 0.45 : 0.0) +
                    stress * 0.20,

                LocationType.Social =>
                    crowding * 0.45 +
                    (1.0 - noise) * 0.15 +
                    (privacy ? -0.10 : 0.0),

                LocationType.Public =>
                    (1.0 - crowding) * 0.35 +
                    (1.0 - noise) * 0.15,

                _ => 0.0
            };
        }

        #endregion RouteMoveTo

        #region DynamicReachOutRouting

        /// <summary>
        /// Routes <c>ReachOut</c> actions from the previous tick to a concrete interaction target.
        /// Only called for <see cref="CognitiveResolutionLevel.Nearby"/> and
        /// <see cref="CognitiveResolutionLevel.Player"/> characters — see <see cref="OnTick"/>.
        /// </summary>
        /// <remarks>
        /// Target selection uses <see cref="SemanticTargeting.ChooseTarget"/> — characters with
        /// warmer beliefs and higher expected acceptance are preferred. The chosen <see cref="SpeechAct"/>
        /// is determined by <see cref="ReachOutSpeechActSelector"/> based on relationship depth.
        /// </remarks>
        /// <param name="now">Current simulation time.</param>
        /// <param name="chars">LOD-filtered character subset (Nearby / Player only).</param>
        private void DynamicReachOutRouting(WDateTime now, IReadOnlyList<IHuman> chars)
        {
            foreach (var character in chars)
            {
                var reachOut = character.LastOutbox
                    .OfType<ActionCommitted>()
                    .FirstOrDefault(a => a.ActionName == ReachOut);

                if (reachOut is null)
                    continue;

                var locationId = _locationService.GetLocation(character.Id);
                if (locationId is null)
                    continue;

                var candidates = CharacterPerceptionResolver.GetPerceivedCharacters(
                    character, chars, _locationService, _perceptionPolicy, _perceptionOptions);

                if (candidates.Count == 0)
                    continue;

                var targetMode = character.Snapshot.Behavior.NeedIntimacy >= 55
                    ? SocialTargetMode.Intimacy
                    : SocialTargetMode.ReachOut;

                var target = SemanticTargeting.ChooseTargetWeighted(
                    character, candidates, targetMode, _rng, _options.ReachOutExplorationTemperature);
                if (target is null)
                    continue;

                var selection = ReachOutSpeechActSelector.SelectSpeechAct(character, target, now, _rng);

                using (_log.BeginCharacterScope(
                    character.Id.Value, "SceneOrchestrator",
                    relatedPersonId: target.Id.Value, locationId: locationId))
                {
                    _log.SceneReachOutRouted(
                        character.Id.Value.ToString(), target.Id.Value.ToString(), selection.Act.ToString(),
                        selection.Familiarity, selection.Trust, selection.Comfort,
                        selection.Closeness, selection.RomanticInterest, selection.HasPrivacy);
                }

                target.ReceiveEvent(new InteractionProposed(
                    now, character.Id, target.Id, selection.Act, null, character.Biology));

                TryTouch(now, character, target);
            }
        }

        #endregion DynamicReachOutRouting

        #region OrganicMicroPositives

        /// <summary>
        /// Generates organic <see cref="MicroPositive"/> events when a character performs
        /// a creative action (<c>Create</c> or <c>Work</c>) witnessed by someone nearby.
        /// </summary>
        /// <remarks>
        /// One witness is selected at random; a 30 % chance gate keeps the events rare.
        /// Only called for Nearby / Player characters — Background NPCs have Coarse perception
        /// and typically zero co-located witnesses above threshold anyway.
        /// </remarks>
        /// <param name="now">Current simulation time.</param>
        /// <param name="chars">LOD-filtered character subset (Nearby / Player only).</param>
        private void OrganicMicroPositives(WDateTime now, IReadOnlyList<IHuman> chars)
        {
            foreach (var character in chars)
            {
                var justCreated = character.LastOutbox
                    .OfType<ActionCommitted>()
                    .Any(a => a.ActionName is Create or Work);

                if (!justCreated)
                    continue;

                var locationId = _locationService.GetLocation(character.Id);
                if (locationId is null)
                    continue;

                var witnesses = CharacterPerceptionResolver.GetPerceivedCharacters(
                    character, chars, _locationService, _perceptionPolicy, _perceptionOptions);

                if (witnesses.Count == 0)
                    continue;

                // One random witness — only one MicroPositive per creative action per substep.
                var witness = witnesses[_rng.Next(witnesses.Count)];

                if (_rng.NextDouble() < _options.OrganicMicroPositiveChance)
                {
                    character.ReceiveEvent(new MicroPositive(
                        now, witness.Id, character.Id, MemoryMicroEventKinds.Validation));
                }
            }
        }

        #endregion OrganicMicroPositives

        #region TryTouch

        /// <summary>
        /// Attempts a physical touch interaction after a reach-out, if relationship
        /// conditions and privacy context allow it.
        /// </summary>
        /// <remarks>
        /// Touch is gated on <c>Closeness</c>, <c>Comfort</c>, and <c>Attraction</c>
        /// to prevent unrealistic contact between characters who lack the necessary trust.
        /// Privacy context further modulates the probability of intimate touch.
        /// </remarks>
        /// <param name="now">Current simulation time.</param>
        /// <param name="from">Initiating character.</param>
        /// <param name="to">Receiving character.</param>
        private void TryTouch(WDateTime now, IHuman from, IHuman to)
        {
            var edge = from.Snapshot.Relationships.Edges.GetValueOrDefault(to.Id);
            var hasPrivacy = from.Snapshot.InteractionSurface.HasPrivacy;
            var level = ReachOutTouchSelector.SelectTouchLevel(edge, hasPrivacy, _rng);

            if (level is not null)
                to.ReceiveEvent(new TouchAttempted(now, from.Id, to.Id, level.Value));
        }

        #endregion TryTouch
    }
}
