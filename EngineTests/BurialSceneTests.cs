// BurialSceneTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines;
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
    using System;
    using System.Collections.Generic;
    using System.Linq;

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

        private static readonly LocationDescriptor Cemetery = new(
            Id: "cemetery", DisplayName: "Cemetery", BaseNoise: 0.02, NoisePerPerson: 0.0,
            Capacity: 200, AllowsPrivacy: false, Type: LocationType.Public);

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
            // The grieving mourner's behavior engine chose to inter the corpse — the scene applies it.
            mourner.SetLastOutbox(new ActionCommitted(Now, mournerId, ActionNames.Bury, WTimeSpan.FromMinutes(30)));

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
        }

        [TestMethod]
        public void Death_TakesTheCharacterOffTheMap_AndTheStaleOutboxIsNotReplayed()
        {
            var locations = new DefaultLocationService();
            locations.RegisterLocation(Room);
            var objects = new InMemoryMutableProvider();
            var orchestrator = BuildOrchestrator(locations, objects);
            var deceasedId = new HumanId(Guid.NewGuid());
            var deceased = new BurialSpy(deceasedId, edges: new());
            deceased.SetLastOutbox(new CharacterDied(Now, deceasedId, DeathCause.OldAge),
                new ActionCommitted(Now, deceasedId, ActionNames.MoveToFood, WTimeSpan.FromMinutes(20)));
            locations.MoveCharacter(deceasedId, Room.Id);

            for (var i = 0; i < 3; i++)
                orchestrator.OnTick(Now + WTimeSpan.FromMinutes(i), new IHuman[] { deceased });

            Assert.IsNull(locations.GetLocation(deceasedId), "A dead character is no longer on the map.");
            Assert.AreEqual(1, objects.GetAllObjects().Count(o => o.Category == WorldObjectCategory.Corpse), "Exactly one corpse, at the place of death.");
            Assert.AreEqual(Room.Id, objects.GetAllObjects().Single().LocationId);
        }

        [TestMethod]
        public void Reset_ForgetsDeathsSoTheSameIdCanBeRoutedAgainInANewWorld()
        {
            var locations = new DefaultLocationService();
            locations.RegisterLocation(Room);
            var objects = new InMemoryMutableProvider();
            var orchestrator = BuildOrchestrator(locations, objects);
            var id = new HumanId(Guid.NewGuid());
            var spy = new BurialSpy(id, edges: new());
            spy.SetLastOutbox(new CharacterDied(Now, id, DeathCause.OldAge));
            locations.MoveCharacter(id, Room.Id);
            orchestrator.OnTick(Now, new IHuman[] { spy });
            Assert.AreEqual(1, objects.GetAllObjects().Count(o => o.Category == WorldObjectCategory.Corpse));

            orchestrator.Reset();
            locations.MoveCharacter(id, Room.Id);
            orchestrator.OnTick(Now, new IHuman[] { spy });

            Assert.AreEqual(1, objects.GetAllObjects().Count(o => o.Category == WorldObjectCategory.Corpse), "Same id, same corpse object id: re-spawned, not duplicated.");
            Assert.IsNull(locations.GetLocation(id), "After the reset the death is processed afresh and the character is retired again.");
        }

        [TestMethod]
        public void ReloadedDeadCharacter_IsTakenOffTheMap()
        {
            var locations = new DefaultLocationService();
            locations.RegisterLocation(Room);
            var orchestrator = BuildOrchestrator(locations, new InMemoryMutableProvider());
            var id = new HumanId(Guid.NewGuid());
            var reloaded = new BurialSpy(id, edges: new());
            reloaded.SetDead();
            locations.MoveCharacter(id, Room.Id);

            orchestrator.OnTick(Now, new IHuman[] { reloaded });

            Assert.IsNull(locations.GetLocation(id), "A character loaded already dead has no death event but must not stay on the map.");
        }

        [TestMethod]
        public void ZeroMovementSpeed_DoesNotStartAnUnboundedTrip()
        {
            var home = new LocationDescriptor("home", "Home", 0.05, 0.01, 50, false, LocationType.Social);
            var tavern = new LocationDescriptor("tavern", "Tavern", 0.10, 0.02, 50, false, LocationType.Social);
            var map = new WorldMap(
                new Dictionary<string, LocationDescriptor>(),
                new Dictionary<string, IReadOnlyList<WorldConnection>> { ["home"] = new List<WorldConnection> { new("tavern", 1600.0) } },
                new Dictionary<string, IReadOnlyList<string>>());
            var locations = new DefaultLocationService();
            locations.RegisterLocation(home);
            locations.RegisterLocation(tavern);
            var orchestrator = BuildOrchestrator(locations, new InMemoryMutableProvider(), new SceneOrchestratorOptions { EnableTravelTime = true }, map, speedMetersPerMinute: 0.0);
            var id = new HumanId(Guid.NewGuid());
            var walker = new BurialSpy(id, edges: new());
            walker.SetKnownObjects(new ObjectLocationFact("bread", "tavern", Now, 0.9, PickupItemKind.Food));
            walker.SetLastOutbox(new ActionCommitted(Now, id, ActionNames.MoveToFood, WTimeSpan.FromMinutes(20)));
            locations.MoveCharacter(id, "home");

            orchestrator.OnTick(Now, new IHuman[] { walker });

            Assert.AreEqual("home", locations.GetLocation(id), "With no movement speed the character stays where it is.");
            Assert.IsNull(orchestrator.GetTravelDestination(id));
        }

        [TestMethod]
        public void TwoDeathsInTheSameTick_DoNotMournEachOther()
        {
            var locations = new DefaultLocationService();
            locations.RegisterLocation(Room);
            var orchestrator = BuildOrchestrator(locations, new InMemoryMutableProvider());
            var aId = new HumanId(Guid.NewGuid());
            var bId = new HumanId(Guid.NewGuid());
            var a = new BurialSpy(aId, edges: new() { [bId] = Edge(aId, bId, KinRole.Partner, closeness: 85) });
            var b = new BurialSpy(bId, edges: new() { [aId] = Edge(bId, aId, KinRole.Partner, closeness: 85) });
            a.SetLastOutbox(new CharacterDied(Now, aId, DeathCause.Combat));
            b.SetLastOutbox(new CharacterDied(Now, bId, DeathCause.Combat));
            locations.MoveCharacter(aId, Room.Id);
            locations.MoveCharacter(bId, Room.Id);

            orchestrator.OnTick(Now, new IHuman[] { a, b });

            Assert.IsFalse(a.ReceivedEvents.OfType<BereavementOnset>().Any() || b.ReceivedEvents.OfType<BereavementOnset>().Any(),
                "A character that is already dead cannot start grieving.");
        }

        [TestMethod]
        public void DeathInTransit_SpawnsTheCorpseAtTheNearerEndOfTheRoad_AndTheDeadNeverArrive()
        {
            var home = new LocationDescriptor("home", "Home", 0.05, 0.01, 50, false, LocationType.Social);
            var tavern = new LocationDescriptor("tavern", "Tavern", 0.10, 0.02, 50, false, LocationType.Social);
            var map = new WorldMap(
                new Dictionary<string, LocationDescriptor>(),
                new Dictionary<string, IReadOnlyList<WorldConnection>>
                {
                    ["home"] = new List<WorldConnection> { new("tavern", 1600.0) }, // 20 min at 80 m/min
                },
                new Dictionary<string, IReadOnlyList<string>>());

            string CorpseAt(double minutesIntoTrip)
            {
                var locations = new DefaultLocationService();
                locations.RegisterLocation(home);
                locations.RegisterLocation(tavern);
                var objects = new InMemoryMutableProvider();
                var orchestrator = BuildOrchestrator(locations, objects, new SceneOrchestratorOptions { EnableTravelTime = true }, map);
                var id = new HumanId(Guid.NewGuid());
                var walker = new BurialSpy(id, edges: new());
                walker.SetKnownObjects(new ObjectLocationFact("bread", "tavern", Now, 0.9, PickupItemKind.Food));
                walker.SetLastOutbox(new ActionCommitted(Now, id, ActionNames.MoveToFood, WTimeSpan.FromMinutes(20)));
                locations.MoveCharacter(id, "home");

                orchestrator.OnTick(Now, new IHuman[] { walker });
                Assert.IsNull(locations.GetLocation(id), "On the road.");

                walker.SetLastOutbox(new CharacterDied(Now + WTimeSpan.FromMinutes(minutesIntoTrip), id, DeathCause.Combat));
                orchestrator.OnTick(Now + WTimeSpan.FromMinutes(minutesIntoTrip), new IHuman[] { walker });
                orchestrator.OnTick(Now + WTimeSpan.FromMinutes(30), new IHuman[] { walker }); // past the planned arrival

                Assert.IsNull(locations.GetLocation(id), "A character who died on the road never arrives.");
                return objects.GetAllObjects().Single(o => o.Category == WorldObjectCategory.Corpse).LocationId;
            }

            Assert.AreEqual("home", CorpseAt(5), "Died in the first half: the body is where the trip began.");
            Assert.AreEqual("tavern", CorpseAt(15), "Died in the second half: the body is at the destination.");
        }

        [TestMethod]
        public void Burial_PlacesGraveAtCemetery_WhenConfigured()
        {
            var locations = new DefaultLocationService();
            locations.RegisterLocation(Room);
            locations.RegisterLocation(Cemetery);

            var objects = new InMemoryMutableProvider();
            var orchestrator = BuildOrchestrator(locations, objects,
                new SceneOrchestratorOptions { CemeteryLocationId = Cemetery.Id });

            var deceasedId = new HumanId(Guid.NewGuid());
            var mournerId = new HumanId(Guid.NewGuid());

            objects.AddObject(BurialObjects.Corpse(deceasedId, Room.Id, "Tom"));

            var mourner = new BurialSpy(mournerId,
                edges: new() { [deceasedId] = Edge(mournerId, deceasedId, KinRole.Partner, closeness: 85) });
            mourner.SetBereavement(Loss(deceasedId, buried: false));
            mourner.SetLastOutbox(new ActionCommitted(Now, mournerId, ActionNames.Bury, WTimeSpan.FromMinutes(30)));

            locations.MoveCharacter(mournerId, Room.Id);
            orchestrator.OnTick(Now, new IHuman[] { mourner });

            var grave = objects.GetAllObjects().Single(o => o.Category == WorldObjectCategory.Grave);
            Assert.AreEqual(Cemetery.Id, grave.LocationId, "With a cemetery configured, the grave is dug there, not at the death site.");
            Assert.AreEqual(Cemetery.Id, locations.GetLocation(mournerId), "The burier carries the body to the cemetery and ends up at the graveside.");
        }

        [TestMethod]
        public void GraveVisit_FiresOnCommittedMournAtGrave()
        {
            var locations = new DefaultLocationService();
            locations.RegisterLocation(Cemetery);

            var objects = new InMemoryMutableProvider();
            var orchestrator = BuildOrchestrator(locations, objects);

            var deceasedId = new HumanId(Guid.NewGuid());
            var visitorId = new HumanId(Guid.NewGuid());

            objects.AddObject(BurialObjects.Grave(deceasedId, Cemetery.Id, "Tom"));

            var visitor = new BurialSpy(visitorId,
                edges: new() { [deceasedId] = Edge(visitorId, deceasedId, KinRole.Partner, closeness: 85) });
            visitor.SetBereavement(Loss(deceasedId, buried: true));
            visitor.SetLastOutbox(new ActionCommitted(Now, visitorId, ActionNames.MournAtGrave, WTimeSpan.FromMinutes(60)));

            locations.MoveCharacter(visitorId, Cemetery.Id);
            orchestrator.OnTick(Now, new IHuman[] { visitor });

            Assert.IsTrue(visitor.ReceivedEvents.OfType<GraveVisited>().Any(e => e.Deceased == deceasedId),
                "Committing MournAtGrave at the deceased's grave fires a graveside visit.");
        }

        [TestMethod]
        public void MoveToGrave_RoutesGrieverToCemetery()
        {
            var locations = new DefaultLocationService();
            locations.RegisterLocation(Room);
            locations.RegisterLocation(Cemetery);

            var objects = new InMemoryMutableProvider();
            var orchestrator = BuildOrchestrator(locations, objects,
                new SceneOrchestratorOptions { CemeteryLocationId = Cemetery.Id });

            var deceasedId = new HumanId(Guid.NewGuid());
            var visitorId = new HumanId(Guid.NewGuid());

            objects.AddObject(BurialObjects.Grave(deceasedId, Cemetery.Id, "Tom"));

            var visitor = new BurialSpy(visitorId,
                edges: new() { [deceasedId] = Edge(visitorId, deceasedId, KinRole.Partner, closeness: 85) });
            visitor.SetBereavement(Loss(deceasedId, buried: true));
            visitor.SetLastOutbox(new ActionCommitted(Now, visitorId, ActionNames.MoveToGrave, WTimeSpan.FromMinutes(20)));

            locations.MoveCharacter(visitorId, Room.Id);
            orchestrator.OnTick(Now, new IHuman[] { visitor });

            Assert.AreEqual(Cemetery.Id, locations.GetLocation(visitorId), "MoveTo:Grave routes the griever to the cemetery.");
        }

        #region Helpers

        private static BereavementState Loss(HumanId deceasedId, bool buried)
            => new(new[]
            {
                new LossRecord(deceasedId, KinRole.Partner, 85, Now,
                    GriefTrajectory.ModerateStable, 60, 1.0, ContinuingBond.None, buried)
            });

        private static DefaultSceneOrchestrator BuildOrchestrator(
            DefaultLocationService locations, IMutableWorldObjectProvider objects, SceneOrchestratorOptions? options = null, WorldMap? map = null, double speedMetersPerMinute = 80.0)
            => new DefaultSceneOrchestrator(
                attractionCalculator: new NeutralAttractionCalculator(),
                locationService: locations,
                perceptionPolicy: new FullPerceptionPolicy(),
                perceptionOptions: new CharacterPerceptionOptions(),
                lodRuntime: new AllBackgroundLodRuntime(),
                worldMap: map ?? new WorldMap(
                    new Dictionary<string, LocationDescriptor>(),
                    new Dictionary<string, IReadOnlyList<WorldConnection>>(),
                    new Dictionary<string, IReadOnlyList<string>>()),
                speedProvider: new ConstantSpeedProvider(speedMetersPerMinute),
                rng: new Random(7),
                log: NullLogger<DefaultSceneOrchestrator>.Instance,
                objectProvider: objects,
                options: options ?? new SceneOrchestratorOptions(),
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

        #endregion Helpers

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

            public void SetDead() => _snapshot = _snapshot with { Physiology = _snapshot.Physiology with { Status = StatusType.Dead } };

            public void SetKnownObjects(params ObjectLocationFact[] facts)
                => _snapshot = _snapshot with { Memory = new MemoryIndex(new List<EpisodicMemory>()) { KnownObjects = facts } };

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

            public void Tick(WDateTime now, WTimeSpan dt)
            { }

            public void RestoreSnapshot(EnginesSnapshot snapshot, WDateOnly today = default) => _snapshot = snapshot;

            public void FlushInbox()
            { }

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
            public void Clear(HumanId id)
            { }

            public CognitiveResolutionLevel Get(HumanId id) => CognitiveResolutionLevel.Background;

            public void Set(HumanId id, CognitiveResolutionLevel level)
            { }
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

        #endregion Test doubles
    }
}
