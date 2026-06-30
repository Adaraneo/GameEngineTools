// BurialSceneTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Attraction;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Bereavement;
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

    /// <summary>
    /// Integration tests for physical burial in <see cref="DefaultSceneOrchestrator"/>: a death spawns a
    /// corpse at the place of death, a co-located mourner inters it (corpse → grave), and the grieving
    /// mourner standing at the fresh grave generates a graveside visit.
    /// </summary>
    [TestClass]
    public class BurialSceneTests : TestBase
    {
        private static readonly WDateTime Now = new WDateTime(0);

        private static readonly LocationDescriptor Room = new(
            Id: "room", DisplayName: "Room", BaseNoise: 0.05, NoisePerPerson: 0.01,
            Capacity: 50, AllowsPrivacy: false, Type: LocationType.Social);

        [TestMethod]
        public void Death_SpawnsCorpse_MournerBuries_AndVisitsGrave()
        {
            var locations = new DefaultLocationService();
            locations.RegisterLocation(Room);

            var objects = new InMemoryMutableProvider();
            var orchestrator = BuildOrchestrator(locations, objects);

            var deceasedId = new HumanId(Guid.NewGuid());
            var mournerId = new HumanId(Guid.NewGuid());

            // Mourner: partner edge to the deceased + an active grief loss for them.
            var mourner = new BurialSpy(mournerId,
                edges: new() { [deceasedId] = Edge(mournerId, deceasedId, KinRole.Partner, closeness: 85) });
            mourner.SetBereavement(new BereavementState(new[]
            {
                new LossRecord(deceasedId, KinRole.Partner, 85, Now,
                    GriefTrajectory.ModerateStable, 60, 1.0, ContinuingBond.None, false)
            }));

            // Deceased: emits CharacterDied this tick; mutual edge so first-impressions stay quiet.
            var deceased = new BurialSpy(deceasedId,
                edges: new() { [mournerId] = Edge(deceasedId, mournerId, KinRole.Partner, closeness: 85) });
            deceased.SetLastOutbox(new CharacterDied(Now, deceasedId, DeathCause.OldAge));

            locations.MoveCharacter(deceasedId, Room.Id);
            locations.MoveCharacter(mournerId, Room.Id);

            var chars = new IHuman[] { deceased, mourner };
            orchestrator.OnTick(Now, chars);

            // The corpse was spawned at the place of death and immediately interred by the co-located mourner.
            var all = objects.GetAllObjects().ToList();
            Assert.IsFalse(all.Any(o => o.Category == WorldObjectCategory.Corpse),
                "The corpse should have been interred (no corpse left).");
            var grave = all.SingleOrDefault(o => o.Category == WorldObjectCategory.Grave);
            Assert.IsNotNull(grave, "Burial produces a grave.");
            Assert.IsTrue(BurialObjects.TryGetDeceased(grave!, out var graveOf) && graveOf == deceasedId,
                "The grave carries the deceased's identity.");

            // The mourner was notified of the loss, the burial, and the graveside visit.
            Assert.IsTrue(mourner.ReceivedEvents.OfType<BereavementOnset>().Any(e => e.Deceased == deceasedId),
                "The mourner receives a bereavement onset.");
            Assert.IsTrue(mourner.ReceivedEvents.OfType<GameEngineTools.Characters.Engines.Bereavement.Buried>().Any(e => e.Deceased == deceasedId),
                "The mourner receives a Buried notification.");
            Assert.IsTrue(mourner.ReceivedEvents.OfType<FuneralHeld>().Any(e => e.Deceased == deceasedId),
                "Burial holds a graveside funeral.");
            Assert.IsTrue(mourner.ReceivedEvents.OfType<GraveVisited>().Any(e => e.Deceased == deceasedId),
                "A grieving mourner at the fresh grave makes a graveside visit.");
        }

        #region Helpers

        private static DefaultSceneOrchestrator BuildOrchestrator(
            DefaultLocationService locations, IMutableWorldObjectProvider objects)
            => new DefaultSceneOrchestrator(
                attractionCalculator: new NeutralAttractionCalculator(),
                locationService: locations,
                perceptionPolicy: new FullPerceptionPolicy(),
                perceptionOptions: new CharacterPerceptionOptions(),
                lodRuntime: new AllBackgroundLodRuntime(),
                worldMap: new WorldMap(
                    new Dictionary<string, LocationDescriptor>(),
                    new Dictionary<string, IReadOnlyList<WorldConnection>>(),
                    new Dictionary<string, IReadOnlyList<string>>()),
                speedProvider: new ConstantSpeedProvider(80.0),
                rng: new Random(7),
                log: NullLogger<DefaultSceneOrchestrator>.Instance,
                objectProvider: objects,
                options: new SceneOrchestratorOptions(),
                mutableObjects: objects);

        private static RelationshipEdge Edge(HumanId a, HumanId b, KinRole kin, double closeness)
            => new RelationshipEdge(
                a, b,
                Like: 60, Trust: 60, Familiarity: 80,
                AestheticAttraction: 50, PhysicalAttraction: 50, IntimateAffinity: 40, SexualInterest: 40,
                Closeness: closeness, Respect: 50, Comfort: 60,
                Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                CommunalStrength: 50,
                KinRole: kin);

        #endregion

        #region Test doubles

        /// <summary>Minimal <see cref="IHuman"/> with a settable LastOutbox + snapshot and captured events.</summary>
        private sealed class BurialSpy : IHuman
        {
            private readonly List<IDomainEvent> _received = new();
            private IReadOnlyList<IDomainEvent> _lastOutbox = Array.Empty<IDomainEvent>();
            private EnginesSnapshot _snapshot;

            public BurialSpy(HumanId id, Dictionary<HumanId, RelationshipEdge> edges)
            {
                Id = id;
                _snapshot = new EnginesSnapshot(
                    new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                    new PsychologyState(0.0, 0.5, 0.5, 10, 10, DiscreteEmotion.Neutral),
                    new BehaviorState(10, 5, 5, 20, 50, 30, null),
                    new InteractionSurface("room", false, 0.05, 0.1, SurfaceKind.Social),
                    new RelationshipState(edges),
                    new MemoryIndex(new List<EpisodicMemory>()));
            }

            public void SetLastOutbox(params IDomainEvent[] events) => _lastOutbox = events;
            public void SetBereavement(BereavementState state) => _snapshot = _snapshot with { Bereavement = state };
            public IReadOnlyList<IDomainEvent> ReceivedEvents => _received;

            public HumanId Id { get; }
            public Identity Identity => new(
                new Name { Original = "T", Familiar = new[] { "T" } },
                new Surname { Male = "Spy", Female = "Spy" },
                WDateOnly.New(80, 1, 1));
            public SexBiology Biology => SexBiology.Female;
            public Personality Personality => new(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);
            public PsychologicalProfile PsychologyProfile => PsychologicalProfile.FromPersonality(Personality);
            public PhysicalAppearance PhysicalAppearance => TestAppearanceFactory.Build(
                heightCm: 168, frame: BodyFrame.Medium, skinTone: SkinTone.Light, eyeColor: EyeColor.Brown,
                hairColor: HairColorNatural.Brown, hairType: HairType.Straight, faceShape: FaceShape.Oval,
                shoulderBreadthCm: 40, hipBreadthCm: 38, noseProjection: 0.5, lipFullness: 0.5);
            public AttractionProfile? AttractionProfile => null;
            public EnginesSnapshot Snapshot => _snapshot;
            public IReadOnlyList<IDomainEvent> LastOutbox => _lastOutbox;
            public int Age => 40;
            public StadiumType Stadium => StadiumType.Adult;

            public void ReceiveEvent(IDomainEvent @event) => _received.Add(@event);
            public void Tick(WDateTime now, WTimeSpan dt) { }
            public void RestoreSnapshot(EnginesSnapshot snapshot, WDateOnly today = default) => _snapshot = snapshot;
            public void FlushInbox() { }
            public int CompareTo(IHuman? other) => 0;
        }

        /// <summary>Dictionary-backed mutable provider — only the methods the burial flow uses are functional.</summary>
        private sealed class InMemoryMutableProvider : IMutableWorldObjectProvider
        {
            private readonly Dictionary<string, WorldObject> _objects = new();

            public IEnumerable<WorldObject> GetObjectsAt(string locationId)
                => _objects.Values.Where(o => o.LocationId == locationId);
            public IEnumerable<WorldObject> GetAllObjects() => _objects.Values.ToList();
            public void AddObject(WorldObject obj) => _objects[obj.Id] = obj;
            public WorldObject? FindObject(string objectId) => _objects.TryGetValue(objectId, out var o) ? o : null;

            public bool RemoveObject(string locationId, string objectId) => _objects.Remove(objectId);
            public bool ConsumeObject(string locationId, string objectId, WDateTime now) => false;
            public bool RestoreObject(string locationId, string objectId) => false;
            public bool SetHeldBy(string locationId, string objectId, HumanId? holder) => false;
            public IEnumerable<WorldObject> GetHeldBy(HumanId holder) => Enumerable.Empty<WorldObject>();
            public IEnumerable<string> GetKnownLocationIds() => _objects.Values.Select(o => o.LocationId).Distinct();
            public IEnumerable<WorldObject> GetAllObjectsAt(string locationId) => GetObjectsAt(locationId);
        }

        private sealed class FullPerceptionPolicy : IPerceptionFidelityPolicy
        {
            public PerceptionFidelityLevel GetLevel(HumanId id) => PerceptionFidelityLevel.Full;
        }

        private sealed class AllBackgroundLodRuntime : ICognitiveResolutionLevelRuntime
        {
            public void Clear(HumanId id) { }
            public CognitiveResolutionLevel Get(HumanId id) => CognitiveResolutionLevel.Background;
            public void Set(HumanId id, CognitiveResolutionLevel level) { }
        }

        private sealed class NeutralAttractionCalculator : IAttractionCalculator
        {
            public AttractionResult Calculate(
                AttractionProfile observerProfile, PhysicalAppearance targetAppearance, AppearanceView targetView,
                SexBiology targetBiology, double observerValence = 0.0, double observerArousal = 0.0,
                int? observerAgeYears = null, int? targetAgeYears = null) => AttractionResult.Neutral;
        }

        private sealed class ConstantSpeedProvider(double metersPerMinute) : IMovementSpeedProvider
        {
            public double GetSpeedMetersPerMinute(EnginesSnapshot snapshot, TerrainType terrain = TerrainType.Indoor) => metersPerMinute;
        }

        #endregion
    }
}
