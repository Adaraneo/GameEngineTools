// DefaultSceneOrchestrator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Simulation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Attraction;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Bereavement;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Reputation;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Engines.Status;
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
    /// Default social orchestration layer for <c>SimulationScene.OnTick</c>.
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

        /// <summary>
        /// Optional scene-level community reputation aggregate. When non-null, the orchestrator
        /// folds witnessed <see cref="ThirdPartyActionObserved"/> acts into it each tick and seeds
        /// a newcomer's first impression with the locale's standing reputation of the target.
        /// <c>null</c> disables community reputation entirely (per-observer edges are unaffected).
        /// </summary>
        private readonly CommunityReputationLedger? _reputationLedger;

        /// <summary>
        /// Optional scene-level social-status aggregate. When non-null, the orchestrator folds the
        /// per-observer <c>PerceivedDominance</c>/<c>PerceivedPrestige</c> edges into an emergent
        /// per-agent <see cref="GameEngineTools.Characters.Engines.Status.SocietalStatus"/> each tick,
        /// pushes it into every character (status×stability stress in Psychology), and biases reach-out
        /// target selection by deference. <c>null</c> disables the social-hierarchy subsystem.
        /// </summary>
        private readonly Characters.Engines.Status.StatusLedger? _statusLedger;

        /// <summary>Deaths already routed to mourners — prevents re-delivering bereavement every tick.</summary>
        private readonly HashSet<HumanId> _bereavementRouted = new();

        /// <summary>
        /// Optional mutable world-object provider used for physical burial: a corpse is spawned at the
        /// place of death and converted into a grave once interred. <c>null</c> disables physical burial
        /// (the abstract funeral mechanic still runs).
        /// </summary>
        private readonly Objects.IMutableWorldObjectProvider? _mutableObjects;

        /// <summary>Deaths whose corpse has already been spawned — avoids duplicate corpses.</summary>
        private readonly HashSet<HumanId> _corpseSpawned = new();

        /// <summary>
        /// Optional Relationships configuration, read for the transference subsystem's thresholds
        /// and sex-weighting (Topic C: <see cref="RelationshipsConfig.SignificantOtherCommitmentThreshold"/>,
        /// <see cref="RelationshipsConfig.TransferenceActivationThreshold"/>, etc.). <c>null</c> disables
        /// transference entirely (default — preserves legacy behavior); significant-other imprint
        /// capture still requires this to compute resemblance weighting.
        /// </summary>
        private readonly RelationshipsConfig? _relationshipsConfig;

        /// <summary>
        /// Characters currently travelling between locations, keyed by id. Populated only when
        /// <see cref="SceneOrchestratorOptions.EnableTravelTime"/> is enabled: a <c>MoveTo:*</c>
        /// records the destination and arrival time here instead of relocating instantly, and the
        /// character is committed to that trip (further <c>MoveTo</c> actions are ignored) until
        /// <see cref="RouteMoveTo"/> sees <c>now &gt;= ArriveAt</c> and places them.
        /// </summary>
        private readonly Dictionary<HumanId, TransitState> _inTransit = new();

        /// <summary>A pending arrival: where the traveller is headed and when they get there.</summary>
        private readonly record struct TransitState(string Origin, string Destination, WDateTime DepartAt, WDateTime ArriveAt);

        /// <summary>A resolved move destination together with the distance to travel to it.</summary>
        private readonly record struct MoveTarget(string LocationId, double DistanceMeters);

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
        /// <param name="reputationLedger">
        /// Optional scene-level community reputation aggregate. When supplied, witnessed third-party
        /// acts feed it and newcomers' first impressions inherit the locale's reputation of the target.
        /// <c>null</c> disables community reputation (default — preserves legacy behavior).
        /// </param>
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
            SceneOrchestratorOptions options,
            CommunityReputationLedger? reputationLedger = null,
            Characters.Engines.Status.StatusLedger? statusLedger = null,
            Objects.IMutableWorldObjectProvider? mutableObjects = null,
            RelationshipsConfig? relationshipsConfig = null)
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
            _reputationLedger = reputationLedger;
            _statusLedger = statusLedger;
            _mutableObjects = mutableObjects;
            _relationshipsConfig = relationshipsConfig;
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

            // ── Community reputation ──────────────────────────────────────────────
            // Fold acts witnessed last tick into the scene ledger BEFORE first impressions,
            // so a newcomer met this tick already inherits the up-to-date local reputation.
            FoldReputationObservations(chars);

            // ── Social status ─────────────────────────────────────────────────────
            // Recompute the emergent Dominance/Prestige consensus and push it into every character
            // so this tick's Psychology reads an up-to-date status×stability stress signal.
            FoldAndPushStatus(chars);

            // ── Bereavement ───────────────────────────────────────────────────────
            // Route this/last tick's deaths to bonded survivors as grief onsets (+ abstract funerals).
            RouteBereavement(now, chars);

            // Physical burial: inter corpses with a co-located mourner and fire graveside visits.
            RouteBurial(now, chars);

            // ── Transference (Topic C) ────────────────────────────────────────────
            // Fold last tick's SignificantOtherThresholdCrossed events into full imprints before
            // this tick's FireFirstImpressions, so a newly-crossed threshold is available for
            // resemblance-checking against people met in a later substep.
            RouteSignificantOtherImprints(now, chars);

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

        #region Community reputation

        /// <summary>
        /// Folds every <see cref="ThirdPartyActionObserved"/> act emitted in the previous tick into
        /// the scene-level <see cref="CommunityReputationLedger"/>, scoped to the actor's location.
        /// </summary>
        /// <remarks>
        /// One witnessed act fans out to one event per observer, so a more widely-witnessed act is
        /// folded more times — exactly the behaviour the Nowak–Sigmund spread term (<c>q</c>) models:
        /// reputation that more of the community holds. <see cref="ThirdPartyObservationType.IntimateAct"/>
        /// is reputation-neutral and is dropped inside <see cref="ReputationMath.UpdateScore"/>.
        /// No-op when no ledger is configured.
        /// </remarks>
        /// <param name="chars">All characters in the scene for this substep.</param>
        private void FoldReputationObservations(IReadOnlyList<IHuman> chars)
        {
            if (_reputationLedger is null)
                return;

            foreach (var c in chars)
            {
                foreach (var observed in c.LastOutbox.OfType<ThirdPartyActionObserved>())
                {
                    // Reputation is local: it accrues at the locale where the act was witnessed.
                    var locationId = _locationService.GetLocation(observed.Actor);
                    if (locationId is null)
                        continue;

                    _reputationLedger.Observe(observed.Actor, locationId, observed.Type, observed.OccurredAt);
                }
            }
        }

        #endregion Community reputation

        #region Social status

        /// <summary>
        /// Folds the current relationship graph into the <see cref="StatusLedger"/> and injects each
        /// character's emergent status + the local hierarchy stability back into them via
        /// <see cref="IHuman.SetSocietalStatus"/>. No-op when no ledger is configured.
        /// </summary>
        private void FoldAndPushStatus(IReadOnlyList<IHuman> chars)
        {
            if (_statusLedger is null)
                return;

            _statusLedger.Fold(chars.Select(c => (c.Id, c.Snapshot.Relationships.Edges)));

            var stability = _statusLedger.HierarchyStability();
            foreach (var c in chars)
                c.SetSocietalStatus(_statusLedger.Get(c.Id), stability);
        }

        #endregion Social status

        #region Bereavement

        /// <summary>
        /// Routes each freshly-detected <see cref="CharacterDied"/> to every bonded survivor as a
        /// <see cref="BereavementOnset"/>, then emits an abstract <see cref="FuneralHeld"/> for mourners
        /// who are co-located (a mourning gathering). Each death is routed once.
        /// </summary>
        /// <remarks>
        /// A survivor is a mourner when they hold an edge to the deceased that is either kin
        /// (<see cref="KinRole"/> ≠ <see cref="KinRole.None"/>) or close
        /// (<c>Closeness ≥ <see cref="SceneOrchestratorOptions.BereavementClosenessThreshold"/></c>).
        /// The lost-bond strength is the larger of Closeness and CommunalStrength.
        /// </remarks>
        private void RouteBereavement(WDateTime now, IReadOnlyList<IHuman> chars)
        {
            foreach (var deceased in chars)
            {
                var death = deceased.LastOutbox.OfType<CharacterDied>().FirstOrDefault();
                if (death is null || _bereavementRouted.Contains(deceased.Id))
                    continue;

                _bereavementRouted.Add(deceased.Id);

                var mourners = new List<IHuman>();
                foreach (var survivor in chars)
                {
                    if (survivor.Id == deceased.Id)
                        continue;

                    if (!IsMourner(survivor, deceased.Id, out var edge))
                        continue;

                    var bondStrength = Math.Max(edge.Closeness, edge.CommunalStrength);
                    survivor.ReceiveEvent(new BereavementOnset(
                        now, survivor.Id, deceased.Id, bondStrength, death.Cause, edge.KinRole));
                    mourners.Add(survivor);
                }

                EmitFunerals(now, deceased.Id, mourners);
                SpawnCorpse(deceased);
            }
        }

        /// <summary>
        /// True when <paramref name="survivor"/> mourns <paramref name="deceasedId"/> — i.e. holds an edge
        /// toward them that is kin (any <see cref="KinRole"/>) or close enough
        /// (Closeness ≥ <see cref="SceneOrchestratorOptions.BereavementClosenessThreshold"/>).
        /// </summary>
        private bool IsMourner(IHuman survivor, HumanId deceasedId, out RelationshipEdge edge)
        {
            if (!survivor.Snapshot.Relationships.Edges.TryGetValue(deceasedId, out edge!))
                return false;

            return edge.KinRole != KinRole.None || edge.Closeness >= _options.BereavementClosenessThreshold;
        }

        /// <summary>Spawns a corpse object at the place of death (physical burial only; no-op without a mutable provider).</summary>
        private void SpawnCorpse(IHuman deceased)
        {
            if (_mutableObjects is null || _corpseSpawned.Contains(deceased.Id))
                return;

            var location = _locationService.GetLocation(deceased.Id);
            if (location is null)
                return;

            _corpseSpawned.Add(deceased.Id);
            _mutableObjects.AddObject(Objects.BurialObjects.Corpse(deceased.Id, location, deceased.ToString() ?? "Corpse"));
        }

        #endregion Bereavement

        #region Transference

        /// <summary>
        /// Folds every <see cref="SignificantOtherThresholdCrossed"/> event emitted in the previous
        /// tick into a full <see cref="SignificantOtherImprint"/>, resolving the Other person's
        /// appearance and personality via the orchestrator's full <see cref="IHuman"/> roster (the
        /// same access pattern <see cref="FireFirstImpressions"/> already uses for
        /// <see cref="IAttractionCalculator"/>), then delivers a
        /// <see cref="SignificantOtherImprintCaptured"/> event back into Self's inbox.
        /// </summary>
        private void RouteSignificantOtherImprints(WDateTime now, IReadOnlyList<IHuman> chars)
        {
            var byId = chars.ToDictionary(c => c.Id);

            foreach (var character in chars)
            {
                foreach (var evt in character.LastOutbox.OfType<SignificantOtherThresholdCrossed>())
                {
                    if (!byId.TryGetValue(evt.Other, out var other))
                        continue; // Other no longer present in the scene roster (e.g. moved away) — skip capture

                    var beliefs = character.Snapshot.SemanticMemory?.GetBeliefs(evt.Other);
                    var dominant = beliefs?.Beliefs.Values.OrderByDescending(b => b.Strength).FirstOrDefault();
                    if (dominant is null)
                        continue; // no belief pattern to transfer yet — skip rather than capture an empty imprint

                    var imprint = new SignificantOtherImprint(
                        evt.Other, now,
                        other.PhysicalAppearance.Face,
                        other.Personality.BigFive,
                        dominant.Kind,
                        dominant.Strength,
                        evt.Commitment);

                    character.ReceiveEvent(new SignificantOtherImprintCaptured(now, character.Id, imprint));
                }
            }
        }

        /// <summary>
        /// Checks <paramref name="observer"/>'s stored <see cref="SignificantOtherImprint"/>s for
        /// resemblance to a newly-met <paramref name="target"/>, emitting
        /// <see cref="TransferenceActivated"/> when the best match crosses the activation threshold.
        /// No-op without <see cref="_relationshipsConfig"/> configured (transference disabled by
        /// default) or when the observer has no stored imprints (cheap early exit — comparison stays
        /// O(imprints), never O(all known people)).
        /// </summary>
        private void CheckTransference(WDateTime now, IHuman observer, IHuman target)
        {
            if (_relationshipsConfig is null)
                return;

            var imprints = observer.Snapshot.SemanticMemory?.SignificantOthers;
            if (imprints is not { Count: > 0 })
                return;

            SignificantOtherImprint? bestMatch = null;
            var bestResemblance = 0.0;

            foreach (var imprint in imprints)
            {
                var facial = TransferenceMath.FacialResemblance(imprint.FaceSummary, target.PhysicalAppearance.Face);
                var personality = TransferenceMath.PersonalityResemblance(imprint.PersonalitySummary, target.Personality.BigFive);
                var combined = TransferenceMath.CombinedResemblance(facial, personality, observer.Biology, _relationshipsConfig);

                if (combined > bestResemblance)
                {
                    bestResemblance = combined;
                    bestMatch = imprint;
                }
            }

            if (bestMatch is not null && bestResemblance >= _relationshipsConfig.TransferenceActivationThreshold)
            {
                observer.ReceiveEvent(new TransferenceActivated(
                    now, observer.Id, target.Id, bestMatch.DominantBeliefKind,
                    bestMatch.DominantBeliefStrength, bestResemblance));
            }
        }

        #endregion Transference

        #region Burial

        /// <summary>
        /// Physical burial: when a character commits a <see cref="ActionNames.Bury"/> action, inters a
        /// co-located corpse (corpse → grave at the same place), notifying every co-located mourner
        /// (<see cref="Buried"/> + a graveside <see cref="FuneralHeld"/>), and fires a one-shot
        /// <see cref="GraveVisited"/> for each grieving mourner standing at a grave. The decision to bury
        /// is made by the burier's behavior engine (the <c>BereavementBehaviorBridge</c>); the scene only
        /// applies the world mutation. No-op without a mutable world-object provider.
        /// </summary>
        private void RouteBurial(WDateTime now, IReadOnlyList<IHuman> chars)
        {
            if (_mutableObjects is null)
                return;

            // ── Inter a corpse when a co-located character commits a Bury action ──────
            foreach (var burier in chars)
            {
                if (!burier.LastOutbox.OfType<ActionCommitted>().Any(a => a.ActionName == Bury))
                    continue;

                var location = _locationService.GetLocation(burier.Id);
                if (location is null)
                    continue;

                // Bury a corpse at the burier's location, preferring one they grieve for.
                var corpse = _mutableObjects.GetObjectsAt(location)
                    .Where(o => o.Category == WorldObjectCategory.Corpse && o.IsAvailable)
                    .OrderByDescending(o =>
                        Objects.BurialObjects.TryGetDeceased(o, out var d) && IsGrievingFor(burier, d) ? 1 : 0)
                    .FirstOrDefault();

                if (corpse is null || !Objects.BurialObjects.TryGetDeceased(corpse, out var deceasedId))
                    continue;

                // The grave goes to the cemetery when one is configured, otherwise it is dug in place.
                var funeralSite = corpse.LocationId;
                var graveLocation = ResolveGraveLocation(funeralSite);
                _mutableObjects.RemoveObject(funeralSite, corpse.Id);
                _mutableObjects.AddObject(Objects.BurialObjects.Grave(deceasedId, graveLocation, corpse.DisplayName));

                var attendees = chars
                    .Where(c => _locationService.GetLocation(c.Id) == funeralSite && IsMourner(c, deceasedId, out _))
                    .ToList();

                foreach (var mourner in attendees)
                {
                    mourner.ReceiveEvent(new Buried(now, mourner.Id, deceasedId));
                    mourner.ReceiveEvent(new FuneralHeld(now, mourner.Id, deceasedId, attendees.Count));
                }

                // The burier carries the body to its resting place — they end up at the graveside.
                // (The carry is instantaneous; travel time for the cortège is not modelled.)
                if (graveLocation != funeralSite)
                {
                    _locationService.MoveCharacter(burier.Id, graveLocation);
                    _fullyMetLocations.Remove(graveLocation);
                }
            }

            // ── Graveside visits: fire on a committed MournAtGrave at a grieved grave ──
            foreach (var visitor in chars)
            {
                if (!visitor.LastOutbox.OfType<ActionCommitted>().Any(a => a.ActionName == MournAtGrave))
                    continue;

                var location = _locationService.GetLocation(visitor.Id);
                if (location is null)
                    continue;

                foreach (var grave in _mutableObjects.GetObjectsAt(location)
                             .Where(o => o.Category == WorldObjectCategory.Grave))
                {
                    if (Objects.BurialObjects.TryGetDeceased(grave, out var deceasedId) && IsGrievingFor(visitor, deceasedId))
                    {
                        visitor.ReceiveEvent(new GraveVisited(now, visitor.Id, deceasedId));
                        break; // one grave per mourning act
                    }
                }
            }
        }

        /// <summary>
        /// The location a grave should be created at: the configured cemetery when it exists, otherwise
        /// <paramref name="deathLocation"/> (buried in place).
        /// </summary>
        private string ResolveGraveLocation(string deathLocation)
        {
            if (_options.CemeteryLocationId is { } cemetery && _locationService.GetDescriptor(cemetery) is not null)
                return cemetery;

            return deathLocation;
        }

        /// <summary>True when <paramref name="visitor"/> is actively grieving <paramref name="deceasedId"/>.</summary>
        private static bool IsGrievingFor(IHuman visitor, HumanId deceasedId)
            => visitor.Snapshot.Bereavement is { } b && b.Losses.Any(l => l.DeceasedId == deceasedId);

        /// <summary>
        /// Resolves where a grieving character should travel for a <c>MoveTo:Grave</c>: the configured
        /// cemetery when set, otherwise the location of a grave belonging to someone they grieve. Returns
        /// <c>null</c> when there is nowhere (else they are already there) to go.
        /// </summary>
        private MoveTarget? ResolveGraveDestination(IHuman character, string? currentLocation)
        {
            if (_options.CemeteryLocationId is { } cemetery
                && cemetery != currentLocation
                && _locationService.GetDescriptor(cemetery) is not null)
                return new MoveTarget(cemetery, _options.FallbackTravelDistanceMeters);

            foreach (var grave in _objectProvider.GetAllObjects())
            {
                if (grave.Category != WorldObjectCategory.Grave || grave.LocationId == currentLocation)
                    continue;

                if (Objects.BurialObjects.TryGetDeceased(grave, out var deceasedId) && IsGrievingFor(character, deceasedId))
                    return new MoveTarget(grave.LocationId, _options.FallbackTravelDistanceMeters);
            }

            return null;
        }

        /// <summary>
        /// Stage-1 abstract funeral: any group of mourners sharing a location holds a mourning gathering,
        /// delivering a <see cref="FuneralHeld"/> (grief relief + cohesion) to each co-located mourner.
        /// </summary>
        private void EmitFunerals(WDateTime now, HumanId deceasedId, IReadOnlyList<IHuman> mourners)
        {
            if (mourners.Count < 2)
                return;

            var byLocation = mourners
                .Select(m => (Mourner: m, Location: _locationService.GetLocation(m.Id)))
                .Where(x => x.Location is not null)
                .GroupBy(x => x.Location!);

            foreach (var group in byLocation)
            {
                var attendees = group.Count();
                if (attendees < 2)
                    continue;

                foreach (var (mourner, _) in group)
                    mourner.ReceiveEvent(new FuneralHeld(now, mourner.Id, deceasedId, attendees));
            }
        }

        #endregion Bereavement

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

                            // Community reputation prior: each newcomer inherits the locale's
                            // standing opinion of the other before any personal history exists.
                            var aTrustPriorOfB = _reputationLedger?.InitialTrustPrior(b.Id, locationId);
                            var bTrustPriorOfA = _reputationLedger?.InitialTrustPrior(a.Id, locationId);

                            if (!aAlreadyKnowsB)
                            {
                                a.ReceiveEvent(new FirstImpressionFormed(now, a.Id, b.Id,
                                    aResult.FirstImpressionLike, aResult.Score,
                                    aResult.BasePhysical, aResult.PreferenceMatch,
                                    TrustPrior: aTrustPriorOfB));

                                // Task C.4 — transference check, using the SAME b.PhysicalAppearance/
                                // b.Personality already resolved above for the attraction calculation.
                                // No new cross-person lookup introduced.
                                CheckTransference(now, a, b);
                            }

                            if (!bAlreadyKnowsA)
                            {
                                b.ReceiveEvent(new FirstImpressionFormed(now, b.Id, a.Id,
                                    bResult.FirstImpressionLike, bResult.Score,
                                    bResult.BasePhysical, bResult.PreferenceMatch,
                                    TrustPrior: bTrustPriorOfA));

                                CheckTransference(now, b, a);
                            }

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
            // ── Process arrivals first (travel-time mode only) ────────────────────────
            // Anyone whose trip has elapsed is placed at their destination this substep and
            // recorded so the per-character loop below does not immediately re-route them on
            // a now-stale MoveTo (they have only just arrived — let them act next substep).
            var arrivedThisSubstep = ProcessArrivals(now);

            foreach (var character in chars)
            {
                // Committed to an in-flight trip, or just arrived this substep → do not re-route.
                if (_options.EnableTravelTime &&
                    (_inTransit.ContainsKey(character.Id) || arrivedThisSubstep.Contains(character.Id)))
                    continue;

                var moveTo = character.LastOutbox
                    .OfType<ActionCommitted>()
                    .FirstOrDefault(a => a.ActionName.StartsWith("MoveTo:"));

                if (moveTo is null)
                    continue;

                var currentLocation = _locationService.GetLocation(character.Id);

                // ── Grave visit movement (MoveTo:Grave) ────────────────────────────────────
                // Routes a grieving character toward the cemetery (or the grave's location).
                if (moveTo.ActionName == MoveToGrave)
                {
                    var graveDestination = ResolveGraveDestination(character, currentLocation);
                    if (graveDestination is not null)
                        CommitMove(character, graveDestination.Value, now);

                    continue;
                }

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
                        CommitMove(character, destination.Value, now);
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
                        _log.SceneMoveToRequested(character.Id.Value.ToString(), requestedType.ToString(), currentLocation ?? "<none>");
                        Console.WriteLine("[MoveTo] {0} requested {1} but no suitable location exists. Current location: {2}.", character.Id.Value.ToString(), requestedType.ToString(), currentLocation ?? "<none>");
                    }
                    continue;
                }

                CommitMove(character, chosen.Value, now);
            }
        }

        /// <summary>
        /// Places every traveller whose arrival time has been reached and removes them from the
        /// in-transit set. No-op (returns an empty set) unless
        /// <see cref="SceneOrchestratorOptions.EnableTravelTime"/> is enabled.
        /// </summary>
        /// <returns>The ids placed this substep — callers skip re-routing them on a stale MoveTo.</returns>
        private HashSet<HumanId> ProcessArrivals(WDateTime now)
        {
            if (!_options.EnableTravelTime || _inTransit.Count == 0)
                return Empty;

            var arrived = new HashSet<HumanId>();

            // ToList(): we mutate _inTransit inside the loop.
            foreach (var kv in _inTransit.ToList())
            {
                if (now >= kv.Value.ArriveAt)
                {
                    Arrive(kv.Key, kv.Value.Destination, fromTransit: true);
                    _inTransit.Remove(kv.Key);
                    arrived.Add(kv.Key);
                }
            }

            return arrived;
        }

        /// <summary>Shared empty id set returned when no arrivals are processed (avoids allocation).</summary>
        private static readonly HashSet<HumanId> Empty = new();

        /// <summary>
        /// Realises a resolved <paramref name="target"/> move for a character. In instant mode
        /// (default) the character is placed immediately. In travel-time mode the character is held
        /// in transit for the computed travel duration and placed later by <see cref="ProcessArrivals"/>;
        /// a zero-or-negative duration (e.g. a co-located destination) is treated as instant.
        /// </summary>
        private void CommitMove(IHuman character, MoveTarget target, WDateTime now)
        {
            if (!_options.EnableTravelTime)
            {
                Arrive(character.Id, target.LocationId);
                return;
            }

            var terrain = _locationService.GetDescriptor(target.LocationId)?.Terrain ?? TerrainType.Indoor;
            var speed = _speedProvider.GetSpeedMetersPerMinute(character.Snapshot, terrain);
            var minutes = TravelDurationComputer.ComputeMinutes(target.DistanceMeters, speed);

            if (minutes <= 0.0)
            {
                Arrive(character.Id, target.LocationId);
                return;
            }

            // The character is genuinely on the road now: unplace it so it is not counted at, nor
            // perceived/social at, its origin while travelling. The scene's post-movement context
            // refresh then announces the "on the road" surface; ProcessArrivals re-places it on arrival.
            var origin = _locationService.GetLocation(character.Id) ?? string.Empty;
            _locationService.RemoveCharacter(character.Id);
            var arriveAt = now + WTimeSpan.FromMinutes(minutes);
            _inTransit[character.Id] = new TransitState(origin, target.LocationId, now, arriveAt);

            using (_log.BeginCharacterScope(character.Id.Value, "SceneOrchestrator"))
            {
                _log.TravelDeparted(character.Id.Value.ToString(), target.LocationId, minutes, arriveAt.ToString());
            }
        }

        /// <summary>Places a character at a destination and rescans it for first impressions.</summary>
        /// <param name="fromTransit">
        /// <c>true</c> when this arrival completes a tracked travel-time transit (logged as a
        /// <c>TravelArrived</c> event); <c>false</c> for instant placements (no travel to report).
        /// </param>
        private void Arrive(HumanId characterId, string destination, bool fromTransit = false)
        {
            _locationService.MoveCharacter(characterId, destination);
            _fullyMetLocations.Remove(destination);

            if (fromTransit)
            {
                using (_log.BeginCharacterScope(characterId.Value, "SceneOrchestrator"))
                {
                    _log.TravelArrived(characterId.Value.ToString(), destination);
                }
            }
        }

        /// <summary>
        /// The destination a character is currently travelling to, or <c>null</c> when they are
        /// not in transit. Exposed so observers can surface "on the way to X" without the
        /// orchestrator owning any presentation concern.
        /// </summary>
        /// <param name="characterId">Character to query.</param>
        /// <returns>Destination location id, or <c>null</c> if the character is not travelling.</returns>
        public string? GetTravelDestination(HumanId characterId)
            => _inTransit.TryGetValue(characterId, out var t) ? t.Destination : null;

        /// <summary>
        /// The character's in-progress trip at <paramref name="now"/>: origin and destination location
        /// ids plus fractional progress [0..1], or <c>null</c> when not travelling. Lets an observer
        /// animate the character along the road in step with real travel time.
        /// </summary>
        public (string Origin, string Destination, double Progress)? GetTransit(HumanId characterId, WDateTime now)
        {
            if (!_inTransit.TryGetValue(characterId, out var t))
                return null;

            var total = (t.ArriveAt - t.DepartAt).Ticks;
            var elapsed = (now - t.DepartAt).Ticks;
            var progress = total <= 0 ? 1.0 : Math.Clamp((double)elapsed / total, 0.0, 1.0);
            return (t.Origin, t.Destination, progress);
        }

        /// <summary>
        /// Resolves the best move destination for a character requesting a given location type.
        /// Adjacent locations are preferred over distant ones; heavily crowded locations are avoided.
        /// </summary>
        /// <param name="character">Character requesting the move.</param>
        /// <param name="currentLocationId">Character's current location, or <c>null</c> if unplaced.</param>
        /// <param name="requestedType">The type of location the character wants to reach.</param>
        /// <returns>The chosen location and its travel distance, or <c>null</c> if none exists.</returns>
        private MoveTarget? FindBestMoveTarget(
            IHuman character,
            string? currentLocationId,
            LocationType requestedType)
        {
            if (currentLocationId is null)
                return null;

            // Prefer adjacent locations of the requested type, ordered by travel time.
            // Speed is computed per-candidate because each connection may lead into a
            // different terrain, which affects travel duration.
            var adjacentTarget = _worldMap
                .GetConnections(currentLocationId)
                .Select(conn => (conn, descriptor: _worldMap.GetLocation(conn.TargetLocationId)))
                .Where(t => t.descriptor?.Type == requestedType)
                .Select(t => (t.conn, speed: _speedProvider.GetSpeedMetersPerMinute(
                    character.Snapshot, t.descriptor?.Terrain ?? TerrainType.Indoor)))
                .OrderBy(t => TravelDurationComputer.ComputeMinutes(t.conn.DistanceMeters, t.speed))
                .Select(t => (MoveTarget?)new MoveTarget(t.conn.TargetLocationId, t.conn.DistanceMeters))
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

            // Non-adjacent fallback: no graph edge, so assume the configured cross-locale distance.
            return new MoveTarget(best.Id, _options.FallbackTravelDistanceMeters);
        }

        /// <summary>
        /// Finds the best location that has at least one available object of the requested category.
        /// Adjacent locations are preferred over distant ones.
        /// Returns <c>null</c> when no provider is wired or no suitable location exists.
        /// </summary>
        /// <param name="character">The character that is about to move.</param>
        /// <param name="currentLocationId">Character's current location, or <c>null</c> if unplaced.</param>
        /// <param name="category">Required object category (e.g. Food, Drink).</param>
        private MoveTarget? FindLocationWithCategory(
            IHuman character,
            string? currentLocationId,
            WorldObjectCategory category)
        {
            if (currentLocationId is null)
                return null;

            // Build the set of locations that have the required category, excluding the current one.
            var candidateLocations = _objectProvider
                .GetAllObjects()
                .Where(o => o.Category == category && o.IsAvailable && o.LocationId != currentLocationId)
                .Select(o => o.LocationId)
                .ToHashSet(StringComparer.Ordinal);

            if (candidateLocations.Count == 0)
                return null;

            // Prefer the nearest adjacent location that has the required objects.
            // Speed is per-candidate because terrain (resolved per destination) affects duration.
            var adjacentMatch = _worldMap
                .GetConnections(currentLocationId)
                .Where(conn => candidateLocations.Contains(conn.TargetLocationId))
                .Select(conn => (conn, speed: _speedProvider.GetSpeedMetersPerMinute(
                    character.Snapshot,
                    _locationService.GetDescriptor(conn.TargetLocationId)?.Terrain ?? TerrainType.Indoor)))
                .OrderBy(t => TravelDurationComputer.ComputeMinutes(t.conn.DistanceMeters, t.speed))
                .Select(t => (MoveTarget?)new MoveTarget(t.conn.TargetLocationId, t.conn.DistanceMeters))
                .FirstOrDefault();

            if (adjacentMatch is not null)
                return adjacentMatch;

            // Fallback: pick arbitrarily from all known locations with the required object.
            // No graph edge, so assume the configured cross-locale distance.
            return new MoveTarget(candidateLocations.First(), _options.FallbackTravelDistanceMeters);
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
        private MoveTarget? FindLocationWithCategoryViaMemory(
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

            // Prefer an adjacent location the character remembers — nearest + highest confidence.
            // Speed is per-candidate because terrain (resolved per destination) affects duration.
            var adjacentConnections = _worldMap.GetConnections(currentLocationId);

            var adjacentMatch = adjacentConnections
                .Where(conn => rememberedLocations.Any(r => r.LocationId == conn.TargetLocationId))
                .Select(conn => (
                    conn.TargetLocationId,
                    conn.DistanceMeters,
                    TravelMinutes: TravelDurationComputer.ComputeMinutes(
                        conn.DistanceMeters,
                        _speedProvider.GetSpeedMetersPerMinute(
                            character.Snapshot,
                            _locationService.GetDescriptor(conn.TargetLocationId)?.Terrain ?? TerrainType.Indoor)),
                    BestConfidence: rememberedLocations.First(r => r.LocationId == conn.TargetLocationId).BestConfidence))
                .OrderBy(x => x.TravelMinutes)
                .ThenByDescending(x => x.BestConfidence)
                .Select(x => (MoveTarget?)new MoveTarget(x.TargetLocationId, x.DistanceMeters))
                .FirstOrDefault();

            if (adjacentMatch is not null)
                return adjacentMatch;

            // Fallback within memory: non-adjacent remembered location with best confidence.
            // No graph edge, so assume the configured cross-locale distance.
            var bestRemembered = rememberedLocations
                .OrderByDescending(r => r.BestConfidence)
                .First();

            return new MoveTarget(bestRemembered.LocationId, _options.FallbackTravelDistanceMeters);
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

                // Status deference: freely-conferred admiration draws reach-out up the prestige ladder,
                // while coercive dominance is avoided (Henrich & Gil-White 2001; Cheng et al. 2013).
                // Only active when the hierarchy subsystem is wired; preserves legacy targeting otherwise.
                target = ApplyDeference(character, candidates, target);

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

        /// <summary>
        /// Applies status deference to a chosen reach-out target. When the status subsystem is wired and
        /// a clearly higher-deference candidate exists (more prestigious, not more coercively dominant),
        /// that candidate is preferred; otherwise the semantically-chosen <paramref name="semanticTarget"/>
        /// is kept unchanged. A no-op (returns <paramref name="semanticTarget"/>) when no ledger is wired.
        /// </summary>
        private IHuman ApplyDeference(IHuman actor, IReadOnlyList<IHuman> candidates, IHuman semanticTarget)
        {
            if (_statusLedger is null)
                return semanticTarget;

            var self = _statusLedger.Get(actor.Id);
            var cfg = _statusLedger.Config;

            IHuman best = semanticTarget;
            var bestBias = StatusMath.DeferenceBias(self, _statusLedger.Get(semanticTarget.Id), cfg);

            foreach (var candidate in candidates)
            {
                if (candidate.Id == actor.Id)
                    continue;

                var bias = StatusMath.DeferenceBias(self, _statusLedger.Get(candidate.Id), cfg);
                if (bias > bestBias)
                {
                    bestBias = bias;
                    best = candidate;
                }
            }

            return best;
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
