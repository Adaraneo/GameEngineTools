// MovementTerrainRoutingTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines;
    using GameEngineTools.Characters.Engines.Attraction;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Movement;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Simulation;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging.Abstractions;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Integration tests for terrain-aware movement routing in
    /// <see cref="DefaultSceneOrchestrator.RouteMoveTo"/> / <c>FindBestMoveTarget</c>.
    /// </summary>
    /// <remarks>
    /// Demonstrates the per-candidate speed change from Task 2: when two destinations of the
    /// requested type are reachable, the one with the shorter <em>travel time</em> must win,
    /// even when it is farther in straight-line distance — because slow terrain (e.g. a bog)
    /// inflates travel time on the nearer route.
    /// </remarks>
    [TestClass]
    public class MovementTerrainRoutingTests
    {
        private static readonly WDateTime Now = new WDateTime(0);

        private static readonly CharacterPerceptionOptions DefaultOptions = new()
        {
            MaxLocalOnlyTargets = 2,
            MaxCoarseTargets = 1,
            LocalOnlyNoiseThreshold = 0.85,
            LocalOnlyCrowdingThreshold = 0.90,
            CoarseNoiseThreshold = 0.60,
            CoarseCrowdingThreshold = 0.70
        };

        // Origin connects to two Social destinations:
        //   • near_bog : 100 m away, Water terrain (×0.56) → speed 44.8 → ~2.23 min
        //   • far_road : 130 m away, Indoor terrain (×1.00) → speed 80.0 → 1.625 min
        // Terrain-blind routing would pick near_bog (shorter distance); terrain-aware
        // routing must pick far_road (shorter time).
        private const string Origin = "origin";
        private const string NearBog = "near_bog";
        private const string FarRoad = "far_road";

        [TestMethod]
        public void RouteMoveTo_TerrainAware_PrefersFartherButFasterRoute()
        {
            // Arrange — real terrain-aware speed provider.
            var locationService = BuildLocationService();
            var worldMap = BuildWorldMap();
            var orchestrator = BuildOrchestrator(
                locationService, worldMap,
                new DefaultMovementSpeedProvider(Options.Create(new MovementConfig())));

            var character = BuildMover();
            locationService.MoveCharacter(character.Id, Origin);

            // Act
            orchestrator.OnTick(Now, new IHuman[] { character });

            // Assert — slow bog terrain makes the nearer route slower; the faster road wins.
            Assert.AreEqual(FarRoad, locationService.GetLocation(character.Id),
                "Terrain-aware routing must choose the farther-but-faster road over the nearer bog.");
        }

        [TestMethod]
        public void RouteMoveTo_TerrainBlind_PrefersNearerRoute_Control()
        {
            // Arrange — terrain-blind constant speed provider (control for the test above).
            var locationService = BuildLocationService();
            var worldMap = BuildWorldMap();
            var orchestrator = BuildOrchestrator(
                locationService, worldMap, new ConstantSpeedProvider(80.0));

            var character = BuildMover();
            locationService.MoveCharacter(character.Id, Origin);

            // Act
            orchestrator.OnTick(Now, new IHuman[] { character });

            // Assert — without terrain, the nearer location wins on raw distance.
            Assert.AreEqual(NearBog, locationService.GetLocation(character.Id),
                "Terrain-blind routing must choose the nearer location by raw distance.");
        }

        #region Factory methods

        private static DefaultLocationService BuildLocationService()
        {
            var svc = new DefaultLocationService();
            svc.RegisterLocation(SocialLocation(Origin, TerrainType.Indoor));
            svc.RegisterLocation(SocialLocation(NearBog, TerrainType.Water));
            svc.RegisterLocation(SocialLocation(FarRoad, TerrainType.Indoor));
            return svc;
        }

        private static WorldMap BuildWorldMap()
        {
            var map = new WorldMap(
                new Dictionary<string, LocationDescriptor>(),
                new Dictionary<string, IReadOnlyList<WorldConnection>>(),
                new Dictionary<string, IReadOnlyList<string>>());

            map.AddLocation(SocialLocation(Origin, TerrainType.Indoor));
            map.AddLocation(SocialLocation(NearBog, TerrainType.Water));
            map.AddLocation(SocialLocation(FarRoad, TerrainType.Indoor));

            map.AddConnection(Origin, NearBog, 100.0);
            map.AddConnection(Origin, FarRoad, 130.0);
            return map;
        }

        private static LocationDescriptor SocialLocation(string id, TerrainType terrain) => new(
            Id: id,
            DisplayName: id,
            BaseNoise: 0.05,
            NoisePerPerson: 0.01,
            Capacity: 50,
            AllowsPrivacy: false,
            Type: LocationType.Social,
            Terrain: terrain);

        private static DefaultSceneOrchestrator BuildOrchestrator(
            ILocationService locationService, WorldMap worldMap, IMovementSpeedProvider speedProvider)
            => new DefaultSceneOrchestrator(
                attractionCalculator: new NeutralAttractionCalculator(),
                locationService: locationService,
                perceptionPolicy: new AllFullPerceptionPolicy(),
                perceptionOptions: DefaultOptions,
                lodRuntime: new AllNearbyLodRuntime(),
                worldMap: worldMap,
                speedProvider: speedProvider,
                rng: new Random(42),
                log: NullLogger<DefaultSceneOrchestrator>.Instance,
                objectProvider: new EmptyWorldObjectProvider(),
                options: new SceneOrchestratorOptions());

        private static MoverSpyHuman BuildMover()
        {
            var id = new HumanId(Guid.NewGuid());
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                new PsychologyState(0.0, 0.5, 0.5, 10, 10, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(Origin, false, 0.1, 0.1, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            var human = new MoverSpyHuman(id, personality, snapshot);
            human.SetLastOutbox(new ActionCommitted(
                Now, id, ActionNames.MoveToSocial, WTimeSpan.Zero));
            return human;
        }

        #endregion Factory methods

        #region MoverSpyHuman

        /// <summary>Minimal <see cref="IHuman"/> with a settable outbox for MoveTo routing.</summary>
        private sealed class MoverSpyHuman : IHuman
        {
            private readonly List<IDomainEvent> _receivedEvents = new();
            private IReadOnlyList<IDomainEvent> _lastOutbox = Array.Empty<IDomainEvent>();
            private EnginesSnapshot _snapshot;
            private readonly Personality _personality;

            public MoverSpyHuman(HumanId id, Personality personality, EnginesSnapshot snapshot)
            {
                Id = id;
                _personality = personality;
                _snapshot = snapshot;
            }

            public HumanId Id { get; }

            public Identity Identity => new(
                new Name { Original = "Test", Familiar = new[] { "Test" } },
                new Surname { Male = "Spy", Female = "Spy" },
                WDateOnly.New(80, 1, 1));

            public SexBiology Biology => SexBiology.Female;

            public Personality Personality => _personality;

            public PsychologicalProfile PsychologyProfile
                => PsychologicalProfile.FromPersonality(_personality);

            public PhysicalAppearance PhysicalAppearance
                => TestAppearanceFactory.Build(
                    heightCm: 168,
                    frame: BodyFrame.Medium,
                    skinTone: SkinTone.Light,
                    eyeColor: EyeColor.Brown,
                    hairColor: HairColorNatural.Brown,
                    hairType: HairType.Straight,
                    faceShape: FaceShape.Oval,
                    shoulderBreadthCm: 40,
                    hipBreadthCm: 38,
                    noseProjection: 0.5,
                    lipFullness: 0.5);

            public AttractionProfile? AttractionProfile => null;

            public EnginesSnapshot Snapshot => _snapshot;

            public IReadOnlyList<IDomainEvent> LastOutbox => _lastOutbox;

            public int Age => 25;

            public StadiumType Stadium => StadiumType.Adult;

            public IReadOnlyList<IDomainEvent> ReceivedEvents => _receivedEvents;

            public void SetLastOutbox(params IDomainEvent[] events) => _lastOutbox = events;

            public void ReceiveEvent(IDomainEvent @event) => _receivedEvents.Add(@event);

            public void Tick(WDateTime now, WTimeSpan dt) { }

            public void RestoreSnapshot(EnginesSnapshot snapshot, WDateOnly today = default)
                => _snapshot = snapshot;

            public void FlushInbox() { }

            public int CompareTo(IHuman? other) => throw new NotImplementedException();
        }

        #endregion MoverSpyHuman

        #region Stubs

        private sealed class AllFullPerceptionPolicy : IPerceptionFidelityPolicy
        {
            public PerceptionFidelityLevel GetLevel(HumanId id) => PerceptionFidelityLevel.Full;
        }

        private sealed class AllNearbyLodRuntime : ICognitiveResolutionLevelRuntime
        {
            public void Clear(HumanId id) { }
            public CognitiveResolutionLevel Get(HumanId id) => CognitiveResolutionLevel.Nearby;
            public void Set(HumanId id, CognitiveResolutionLevel level) { }
        }

        private sealed class NeutralAttractionCalculator : IAttractionCalculator
        {
            public AttractionResult Calculate(
                AttractionProfile observerProfile,
                PhysicalAppearance targetAppearance,
                AppearanceView targetView,
                SexBiology targetBiology,
                double observerValence = 0.0,
                double observerArousal = 0.0,
                int? observerAgeYears = null,
                int? targetAgeYears = null)
                => AttractionResult.Neutral;
        }

        private sealed class ConstantSpeedProvider(double metersPerMinute) : IMovementSpeedProvider
        {
            public double GetSpeedMetersPerMinute(
                EnginesSnapshot snapshot, TerrainType terrain = TerrainType.Indoor) => metersPerMinute;
        }

        private sealed class EmptyWorldObjectProvider : IWorldObjectProvider
        {
            public IEnumerable<WorldObject> GetObjectsAt(string locationId) => Enumerable.Empty<WorldObject>();
            public IEnumerable<WorldObject> GetAllObjects() => Enumerable.Empty<WorldObject>();
            public void AddObject(WorldObject obj) { }
            public WorldObject? FindObject(string objectId) => null;
        }

        #endregion Stubs
    }
}
