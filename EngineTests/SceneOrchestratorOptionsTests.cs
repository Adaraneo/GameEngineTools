// SceneOrchestratorOptionsTests.cs
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
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Tests for <see cref="SceneOrchestratorOptions"/>: default-equivalence with the legacy
    /// hard-coded literals, and verification that an option value actually drives orchestrator
    /// behavior (plumbing).
    /// </summary>
    [TestClass]
    public class SceneOrchestratorOptionsTests : TestBase
    {
        #region Constants

        private static readonly WDateTime Now = new WDateTime(0);

        /// <summary>Low-noise surface — does not block any perception fidelity tier.</summary>
        private const double LowNoise = 0.10;

        private static readonly CharacterPerceptionOptions DefaultOptions = new()
        {
            MaxLocalOnlyTargets = 2,
            MaxCoarseTargets = 1,
            LocalOnlyNoiseThreshold = 0.85,
            LocalOnlyCrowdingThreshold = 0.90,
            CoarseNoiseThreshold = 0.60,
            CoarseCrowdingThreshold = 0.70
        };

        private static readonly LocationDescriptor TestRoom = new(
            Id: "test_room",
            DisplayName: "Test Room",
            BaseNoise: 0.05,
            NoisePerPerson: 0.01,
            Capacity: 50,
            AllowsPrivacy: false,
            Type: LocationType.Social);

        #endregion Constants

        #region Private fields

        private DefaultLocationService _locationService = default!;

        #endregion Private fields

        #region Setup

        protected override void TestInit()
        {
            base.TestInit();
            _locationService = new DefaultLocationService();
            _locationService.RegisterLocation(TestRoom);
        }

        #endregion Setup

        // ══════════════════════════════════════════════════════════════════════
        // 4a — default-equivalence (protects the "no behavior change" boundary)
        // ══════════════════════════════════════════════════════════════════════

        #region Default equivalence

        [TestMethod]
        public void Defaults_MatchLegacyLiterals_PreserveBehavior()
        {
            // Arrange
            var options = new SceneOrchestratorOptions();

            // Assert
            Assert.AreEqual(0.25, options.ReachOutExplorationTemperature, 1e-9);
            Assert.AreEqual(0.30, options.OrganicMicroPositiveChance, 1e-9);
            Assert.AreEqual(0.15, options.MinMemoryConfidence, 1e-9);
        }

        #endregion Default equivalence

        // ══════════════════════════════════════════════════════════════════════
        // 4b — plumbing: OrganicMicroPositiveChance actually gates emission
        // ══════════════════════════════════════════════════════════════════════

        #region OrganicMicroPositiveChance plumbing

        [TestMethod]
        public void OrganicMicroPositives_ChanceZero_EmitsNoMicroPositive()
        {
            // Arrange — chance 0.0 must suppress every MicroPositive event.
            var orchestrator = BuildOrchestrator(
                new SceneOrchestratorOptions { OrganicMicroPositiveChance = 0.0 });
            var creator = BuildCreator();
            var witness = BuildWitness();
            Place(creator, witness);

            // Act
            orchestrator.OnTick(Now, new IHuman[] { creator, witness });

            // Assert
            Assert.AreEqual(
                0,
                creator.ReceivedEvents.OfType<MicroPositive>().Count(),
                "Chance 0.0 must emit no MicroPositive.");
        }

        [TestMethod]
        public void OrganicMicroPositives_ChanceOne_EmitsMicroPositive()
        {
            // Arrange — chance 1.0 always emits when a witness is present.
            var orchestrator = BuildOrchestrator(
                new SceneOrchestratorOptions { OrganicMicroPositiveChance = 1.0 });
            var creator = BuildCreator();
            var witness = BuildWitness();
            Place(creator, witness);

            // Act
            orchestrator.OnTick(Now, new IHuman[] { creator, witness });

            // Assert
            Assert.AreEqual(
                1,
                creator.ReceivedEvents.OfType<MicroPositive>().Count(),
                "Chance 1.0 must emit exactly one MicroPositive for one perceived witness.");
        }

        #endregion OrganicMicroPositiveChance plumbing

        // ══════════════════════════════════════════════════════════════════════
        // Factory and stubs
        // ══════════════════════════════════════════════════════════════════════

        #region Factory methods

        private DefaultSceneOrchestrator BuildOrchestrator(SceneOrchestratorOptions options)
            => new DefaultSceneOrchestrator(
                attractionCalculator: new NeutralAttractionCalculator(),
                locationService: _locationService,
                perceptionPolicy: new AllFullPerceptionPolicy(),
                perceptionOptions: DefaultOptions,
                lodRuntime: new AllNearbyLodRuntime(),
                worldMap: new WorldMap(
                    new Dictionary<string, LocationDescriptor>(),
                    new Dictionary<string, IReadOnlyList<WorldConnection>>(),
                    new Dictionary<string, IReadOnlyList<string>>()),
                speedProvider: new ConstantSpeedProvider(80.0),
                rng: new Random(42),
                log: NullLogger<DefaultSceneOrchestrator>.Instance,
                objectProvider: new EmptyWorldObjectProvider(),
                options: options);

        /// <summary>Builds a character whose last outbox contains a witnessed <c>Create</c> action.</summary>
        private static OrchestratorSpyHuman BuildCreator()
        {
            var human = BuildSpyHuman(LowNoise);
            human.SetLastOutbox(new ActionCommitted(
                Now, human.Id, ActionNames.Create, WTimeSpan.Zero));
            return human;
        }

        /// <summary>Builds a plain co-located witness with an empty outbox.</summary>
        private static OrchestratorSpyHuman BuildWitness() => BuildSpyHuman(LowNoise);

        private static OrchestratorSpyHuman BuildSpyHuman(double noise)
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
                new InteractionSurface(TestRoom.Id, false, noise, 0.1, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new OrchestratorSpyHuman(id, personality, snapshot);
        }

        private void Place(params OrchestratorSpyHuman[] chars)
        {
            foreach (var c in chars)
                _locationService.MoveCharacter(c.Id, TestRoom.Id);
        }

        #endregion Factory methods

        #region OrchestratorSpyHuman

        /// <summary>
        /// Minimal <see cref="IHuman"/> that records received events and exposes a settable
        /// <see cref="LastOutbox"/> so creative-action witnessing can be simulated.
        /// </summary>
        private sealed class OrchestratorSpyHuman : IHuman
        {
            private readonly List<IDomainEvent> _receivedEvents = new();
            private IReadOnlyList<IDomainEvent> _lastOutbox = Array.Empty<IDomainEvent>();
            private EnginesSnapshot _snapshot;
            private readonly Personality _personality;

            public OrchestratorSpyHuman(HumanId id, Personality personality, EnginesSnapshot snapshot)
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

            /// <summary>Sets the outbox the orchestrator reads to detect witnessed actions.</summary>
            public void SetLastOutbox(params IDomainEvent[] events) => _lastOutbox = events;

            public void ReceiveEvent(IDomainEvent @event) => _receivedEvents.Add(@event);

            public void Tick(WDateTime now, WTimeSpan dt) { }

            public void RestoreSnapshot(EnginesSnapshot snapshot, WDateOnly today = default)
                => _snapshot = snapshot;

            public void FlushInbox() { }

            public int CompareTo(IHuman? other) => throw new NotImplementedException();
        }

        #endregion OrchestratorSpyHuman

        #region Stubs

        private sealed class AllFullPerceptionPolicy : IPerceptionFidelityPolicy
        {
            public PerceptionFidelityLevel GetLevel(HumanId id) => PerceptionFidelityLevel.Full;
        }

        /// <summary>Returns <see cref="CognitiveResolutionLevel.Nearby"/> so social functions run.</summary>
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
            public double GetSpeedMetersPerMinute(EnginesSnapshot snapshot) => metersPerMinute;
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
