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

    /// <summary>A character that wants to reach out and has nobody around goes looking for company.</summary>
    [TestClass]
    public class SocialRoutingTests : TestBase
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

        private const string Flat = "flat";
        private const string Pub = "pub";

        private static string RunReachOut(int othersInTheFlat)
        {
            var locationService = new DefaultLocationService();
            var map = new WorldMap(
                new Dictionary<string, LocationDescriptor>(),
                new Dictionary<string, IReadOnlyList<WorldConnection>>(),
                new Dictionary<string, IReadOnlyList<string>>());
            foreach (var d in new[] { Place(Flat, LocationType.Rest), Place(Pub, LocationType.Social) })
            {
                locationService.RegisterLocation(d);
                map.AddLocation(d);
            }

            map.AddConnection(Flat, Pub, 200.0);

            var orchestrator = new DefaultSceneOrchestrator(
                new NeutralAttractionCalculator(), locationService, new AllFullPerceptionPolicy(), DefaultOptions,
                new AllNearbyLodRuntime(), map, new ConstantSpeedProvider(80.0), new Random(42),
                NullLogger<DefaultSceneOrchestrator>.Instance, new EmptyWorldObjectProvider(), new SceneOrchestratorOptions());

            var people = new List<IHuman>();
            var seeker = BuildPerson(reachOut: true);
            people.Add(seeker);
            for (var i = 0; i < othersInTheFlat; i++)
                people.Add(BuildPerson(reachOut: false));

            foreach (var person in people)
                locationService.MoveCharacter(person.Id, Flat);

            orchestrator.OnTick(Now, people);
            return locationService.GetLocation(seeker.Id)!;
        }

        [TestMethod]
        public void ReachOutWithNobodyAround_SendsTheCharacterToASocialPlace()
            => Assert.AreEqual(Pub, RunReachOut(othersInTheFlat: 0));

        [TestMethod]
        public void ReachOutWithSomeoneAround_KeepsTheCharacterWhereItIs()
            => Assert.AreEqual(Flat, RunReachOut(othersInTheFlat: 1));

        private static LocationDescriptor Place(string id, LocationType type) => new(
            Id: id, DisplayName: id, BaseNoise: 0.05, NoisePerPerson: 0.01, Capacity: 8,
            AllowsPrivacy: type == LocationType.Rest, Type: type, Terrain: TerrainType.Indoor);

        private static MoverSpyHuman BuildPerson(bool reachOut)
        {
            var id = new HumanId(Guid.NewGuid());
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), AttachmentProfile.Secure, CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5), Sociosexuality.Intermediate, Chronotype.Neutral);
            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                new PsychologyState(0.0, 0.5, 0.5, 10, 10, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(Flat, true, 0.1, 0.1, SurfaceKind.Rest),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));
            var human = new MoverSpyHuman(id, personality, snapshot);
            if (reachOut)
                human.SetLastOutbox(new ActionCommitted(Now, id, ActionNames.ReachOut, WTimeSpan.FromHours(1)));
            return human;
        }

        /// <summary>Minimal <see cref="IHuman"/> with a settable outbox for MoveTo routing.</summary>
        private sealed class MoverSpyHuman : IHuman
        {
            private readonly List<IDomainEvent> _receivedEvents = new();
            private IReadOnlyList<IDomainEvent> _lastOutbox = Array.Empty<IDomainEvent>();
            private EnginesSnapshot _snapshot;
            private readonly Personality _personality;

            public string? Home { get; set; }

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
                WDateOnly.New(80, 1, 1)) { HomeLocationId = Home };

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

            public void Tick(WDateTime now, WTimeSpan dt)
            { }

            public void RestoreSnapshot(EnginesSnapshot snapshot, WDateOnly today = default)
                => _snapshot = snapshot;

            public void FlushInbox()
            { }

            public int CompareTo(IHuman? other) => throw new NotImplementedException();
        }


        #region Stubs

        private sealed class AllFullPerceptionPolicy : IPerceptionFidelityPolicy
        {
            public PerceptionFidelityLevel GetLevel(HumanId id) => PerceptionFidelityLevel.Full;
        }

        private sealed class AllNearbyLodRuntime : ICognitiveResolutionLevelRuntime
        {
            public void Clear(HumanId id)
            { }

            public CognitiveResolutionLevel Get(HumanId id) => CognitiveResolutionLevel.Nearby;

            public void Set(HumanId id, CognitiveResolutionLevel level)
            { }
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

            public void AddObject(WorldObject obj)
            { }

            public WorldObject? FindObject(string objectId) => null;
        }

        #endregion Stubs
    }
}
