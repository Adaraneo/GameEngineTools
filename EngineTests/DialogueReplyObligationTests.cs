// DialogueReplyObligationTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
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

    /// <summary>
    /// Integration cover for the response obligation in <see cref="DefaultSceneOrchestrator"/>.
    /// </summary>
    /// <remarks>
    /// A reply used to require the answerer to independently commit a <c>ReachOut</c> action AND to
    /// independently pick the asker as its target, so turn-taking existed but fired only by
    /// coincidence. These tests assert the answerer speaks because it was spoken to — with an empty
    /// outbox of its own — and that the obligation still respects co-presence and its time window.
    /// </remarks>
    [TestClass]
    public class DialogueReplyObligationTests : TestBase
    {
        private static readonly LocationDescriptor Room = new(
            Id: "room",
            DisplayName: "Room",
            BaseNoise: 0.05,
            NoisePerPerson: 0.01,
            Capacity: 50,
            AllowsPrivacy: false,
            Type: LocationType.Social);

        private static readonly LocationDescriptor Elsewhere = new(
            Id: "elsewhere",
            DisplayName: "Elsewhere",
            BaseNoise: 0.05,
            NoisePerPerson: 0.01,
            Capacity: 50,
            AllowsPrivacy: false,
            Type: LocationType.Social);

        private static readonly CharacterPerceptionOptions Options = new()
        {
            MaxLocalOnlyTargets = 4,
            MaxCoarseTargets = 4,
            LocalOnlyNoiseThreshold = 0.85,
            LocalOnlyCrowdingThreshold = 0.90,
            CoarseNoiseThreshold = 0.60,
            CoarseCrowdingThreshold = 0.70,
        };

        private DefaultLocationService _locations = default!;
        private DefaultSceneOrchestrator _orchestrator = default!;

        protected override void TestInit()
        {
            base.TestInit();
            _locations = new DefaultLocationService();
            _locations.RegisterLocation(Room);
            _locations.RegisterLocation(Elsewhere);
            _orchestrator = BuildOrchestrator();
        }

        // ──────────────────────────────────────────────────────────────────────
        // The point of the change
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void OnTick_AfterBeingAddressed_AnswererRepliesWithoutReachingOutItself()
        {
            var asker = BuildHuman("Petr");
            var answerer = BuildHuman("Jana");
            Place(Room, asker, answerer);

            // Tick 1: only the asker acts on its own initiative.
            asker.SetLastOutbox(Committed(GameEngineTools.Characters.Engines.ActionNames.ReachOut));
            _orchestrator.OnTick(Now(0), new[] { asker, answerer });

            Assert.IsTrue(
                Proposals(answerer).Any(),
                "precondition: the asker's act must reach the answerer");

            // Tick 2: the answerer has an EMPTY outbox — it never decided to socialise.
            answerer.SetLastOutbox();
            asker.SetLastOutbox();
            _orchestrator.OnTick(Now(1), new[] { asker, answerer });

            var reply = Proposals(asker).LastOrDefault(p => p.From == answerer.Id);
            Assert.IsNotNull(reply, "the answerer must reply because it was addressed, not because it felt like talking");
        }

        [TestMethod]
        public void OnTick_ReplyIsDeliveredOnlyOnce()
        {
            var asker = BuildHuman("Petr");
            var answerer = BuildHuman("Jana");
            Place(Room, asker, answerer);

            asker.SetLastOutbox(Committed(GameEngineTools.Characters.Engines.ActionNames.ReachOut));
            _orchestrator.OnTick(Now(0), new[] { asker, answerer });

            answerer.SetLastOutbox();
            asker.SetLastOutbox();
            _orchestrator.OnTick(Now(1), new[] { asker, answerer });
            var afterFirstReply = Proposals(asker).Count(p => p.From == answerer.Id);
            Assert.AreEqual(1, afterFirstReply, "precondition: exactly one reply so far");

            // Answering flips the floor, so the obligation is discharged: further quiet ticks add nothing.
            _orchestrator.OnTick(Now(2), new[] { asker, answerer });
            _orchestrator.OnTick(Now(3), new[] { asker, answerer });

            Assert.AreEqual(
                afterFirstReply,
                Proposals(asker).Count(p => p.From == answerer.Id),
                "a discharged obligation must not keep producing replies");
        }

        [TestMethod]
        public void OnTick_PartnerLeftTheRoom_NoReply()
        {
            var asker = BuildHuman("Petr");
            var answerer = BuildHuman("Jana");
            Place(Room, asker, answerer);

            asker.SetLastOutbox(Committed(GameEngineTools.Characters.Engines.ActionNames.ReachOut));
            _orchestrator.OnTick(Now(0), new[] { asker, answerer });
            var beforeLeaving = Proposals(asker).Count(p => p.From == answerer.Id);
            Assert.IsTrue(Proposals(answerer).Any(), "precondition: the question was asked");

            // The asker walks off mid-question — nobody answers an empty room.
            _locations.MoveCharacter(asker.Id, Elsewhere.Id);
            answerer.SetLastOutbox();
            asker.SetLastOutbox();
            _orchestrator.OnTick(Now(1), new[] { asker, answerer });

            Assert.AreEqual(
                beforeLeaving,
                Proposals(asker).Count(p => p.From == answerer.Id),
                "a partner who is no longer co-present is not answered");
        }

        [TestMethod]
        public void OnTick_QuietCharactersWithNoExchange_SayNothing()
        {
            var a = BuildHuman("Petr");
            var b = BuildHuman("Jana");
            Place(Room, a, b);

            // Nobody reaches out and nothing was ever said — the obligation pass must stay silent.
            _orchestrator.OnTick(Now(0), new[] { a, b });
            _orchestrator.OnTick(Now(1), new[] { a, b });

            Assert.AreEqual(0, Proposals(a).Count, "no exchange, no speech");
            Assert.AreEqual(0, Proposals(b).Count, "no exchange, no speech");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>A tick <paramref name="minutes"/> into the scene — well inside the 15-minute reply window.</summary>
        private static WDateTime Now(int minutes)
            => new WDateTime(WDateTime.New(WDateOnly.New(100, 1, 1)).WorldTicks + WTimeSpan.FromMinutes(minutes).Ticks);

        private static ActionCommitted Committed(string action)
            => new(new WDateTime(0), new HumanId(Guid.NewGuid()), action, WTimeSpan.FromMinutes(1));

        private static IReadOnlyList<InteractionProposed> Proposals(SpyHuman human)
            => human.ReceivedEvents.OfType<InteractionProposed>().ToList();

        private void Place(LocationDescriptor where, params SpyHuman[] chars)
        {
            foreach (var c in chars)
            {
                _locations.MoveCharacter(c.Id, where.Id);
            }
        }

        private DefaultSceneOrchestrator BuildOrchestrator()
            => new(
                attractionCalculator: new NeutralAttractionCalculator(),
                locationService: _locations,
                perceptionPolicy: new FullPerceptionPolicy(),
                perceptionOptions: Options,
                lodRuntime: new AllNearbyLodRuntime(),
                worldMap: new WorldMap(
                    new Dictionary<string, LocationDescriptor>(),
                    new Dictionary<string, IReadOnlyList<WorldConnection>>(),
                    new Dictionary<string, IReadOnlyList<string>>()),
                speedProvider: new ConstantSpeedProvider(80.0),
                rng: new Random(42),
                log: NullLogger<DefaultSceneOrchestrator>.Instance,
                objectProvider: new EmptyWorldObjectProvider(),
                options: new SceneOrchestratorOptions());

        private static SpyHuman BuildHuman(string name)
        {
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
                new InteractionSurface(Room.Id, false, 0.1, 0.1, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new SpyHuman(new HumanId(Guid.NewGuid()), name, personality, snapshot);
        }

        // ── Test doubles ──────────────────────────────────────────────────────

        /// <summary>Nearby, so the orchestrator actually runs its reach-out / reply routing.</summary>
        private sealed class AllNearbyLodRuntime : ICognitiveResolutionLevelRuntime
        {
            public void Clear(HumanId id) { }

            public CognitiveResolutionLevel Get(HumanId id) => CognitiveResolutionLevel.Nearby;

            public void Set(HumanId id, CognitiveResolutionLevel level) { }
        }

        private sealed class FullPerceptionPolicy : IPerceptionFidelityPolicy
        {
            public PerceptionFidelityLevel GetLevel(HumanId id) => PerceptionFidelityLevel.Full;
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

        /// <summary>Captures received events; runs no engines.</summary>
        private sealed class SpyHuman : IHuman
        {
            private readonly List<IDomainEvent> _received = new();
            private readonly Personality _personality;
            private readonly string _name;
            private EnginesSnapshot _snapshot;
            private IReadOnlyList<IDomainEvent> _lastOutbox = Array.Empty<IDomainEvent>();

            public SpyHuman(HumanId id, string name, Personality personality, EnginesSnapshot snapshot)
            {
                Id = id;
                _name = name;
                _personality = personality;
                _snapshot = snapshot;
            }

            public HumanId Id { get; }

            public Identity Identity => new(
                new Name { Original = _name, Familiar = new[] { _name } },
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

            public GameEngineTools.StadiumType Stadium => GameEngineTools.StadiumType.Adult;

            public int CompareTo(IHuman? other) => throw new NotImplementedException();

            public IReadOnlyList<IDomainEvent> ReceivedEvents => _received;

            public void SetLastOutbox(params IDomainEvent[] events) => _lastOutbox = events;

            public void ReceiveEvent(IDomainEvent @event) => _received.Add(@event);

            public void Tick(WDateTime now, WTimeSpan dt) { }

            public void RestoreSnapshot(EnginesSnapshot snapshot, WDateOnly today = default)
                => _snapshot = snapshot;

            public void FlushInbox() { }
        }
    }
}
