// SceneOrchestratorOneWayTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Attraction;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.SemanticMemory;
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
    /// Integration tests for one-way observation logic in
    /// <see cref="DefaultSceneOrchestrator"/> / <c>FireFirstImpressions</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses <see cref="CognitiveResolutionLevel.Background"/> for all characters so only
    /// <c>FireFirstImpressions</c> and <c>RouteMoveTo</c> run per substep.
    /// </para>
    /// <para>
    /// One-way perception is modelled by giving the observer
    /// <see cref="PerceptionFidelityLevel.Full"/> and the target
    /// <see cref="PerceptionFidelityLevel.Coarse"/> while the target's
    /// <see cref="InteractionSurface.Noise"/> exceeds <c>CoarseNoiseThreshold</c>.
    /// This is the same noise read by <see cref="CharacterPerceptionResolver"/>
    /// from <c>observer.Snapshot.InteractionSurface</c> — no location dispatch needed.
    /// </para>
    /// </remarks>
    [TestClass]
    public class SceneOrchestratorOneWayTests : TestBase
    {
        #region Constants

        private static readonly WDateTime Now = new WDateTime(0);

        /// <summary>
        /// Noise threshold for the test: 0.70 &gt; <c>CoarseNoiseThreshold = 0.60</c>.
        /// A character with Coarse fidelity and Noise=0.70 perceives nobody.
        /// </summary>
        private const double HighNoise = 0.70;

        /// <summary>Low-noise surface — does not block any fidelity tier.</summary>
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
        private readonly Dictionary<HumanId, PerceptionFidelityLevel> _fidelityOverrides = new();
        private DefaultSceneOrchestrator _orchestrator = default!;

        #endregion Private fields

        #region Setup

        protected override void TestInit()
        {
            base.TestInit();
            _fidelityOverrides.Clear();
            _locationService = new DefaultLocationService();
            _locationService.RegisterLocation(TestRoom);
            _orchestrator = BuildOrchestrator();
        }

        #endregion Setup

        // ══════════════════════════════════════════════════════════════════════
        // Baseline — mutual perception produces bilateral FirstImpression
        // ══════════════════════════════════════════════════════════════════════

        #region Mutual perception — bilateral baseline

        /// <summary>
        /// Both Full-fidelity characters in the same room must each receive
        /// a <see cref="FirstImpressionFormed"/> event — no regressions.
        /// </summary>
        [TestMethod]
        public void OnTick_MutualPerception_BilateralFirstImpressionFired()
        {
            var a = BuildSpyHuman(LowNoise);
            var b = BuildSpyHuman(LowNoise);

            _fidelityOverrides[a.Id] = PerceptionFidelityLevel.Full;
            _fidelityOverrides[b.Id] = PerceptionFidelityLevel.Full;

            Place(a, b);
            _orchestrator.OnTick(Now, new[] { a, b });

            Assert.IsTrue(
                a.ReceivedEvents.OfType<FirstImpressionFormed>().Any(),
                "A must receive FirstImpressionFormed.");
            Assert.IsTrue(
                b.ReceivedEvents.OfType<FirstImpressionFormed>().Any(),
                "B must receive FirstImpressionFormed.");
        }

        #endregion Mutual perception — bilateral baseline

        // ══════════════════════════════════════════════════════════════════════
        // One-way observation — asymmetric perception
        // ══════════════════════════════════════════════════════════════════════

        #region One-way observation

        /// <summary>
        /// When A has Full fidelity and B has Coarse fidelity with blocking noise,
        /// only A must receive <see cref="OneWayObservationFormed"/>.
        /// B must receive no event — B never perceived A.
        /// </summary>
        [TestMethod]
        public void OnTick_AsymmetricPerception_OnlyObserverReceivesEvent()
        {
            // A = Full fidelity (low noise — sees everyone)
            // B = Coarse fidelity + high noise → perceives nobody
            var a = BuildSpyHuman(LowNoise);
            var b = BuildSpyHuman(HighNoise);

            _fidelityOverrides[a.Id] = PerceptionFidelityLevel.Full;
            _fidelityOverrides[b.Id] = PerceptionFidelityLevel.Coarse;

            Place(a, b);
            _orchestrator.OnTick(Now, new[] { a, b });

            // Observer gets one-way edge event
            Assert.IsTrue(
                a.ReceivedEvents.OfType<OneWayObservationFormed>().Any(),
                "Observer A must receive OneWayObservationFormed.");

            // Target receives nothing — was unaware
            Assert.IsFalse(
                b.ReceivedEvents.OfType<OneWayObservationFormed>().Any(),
                "Target B must NOT receive OneWayObservationFormed.");
            Assert.IsFalse(
                b.ReceivedEvents.OfType<FirstImpressionFormed>().Any(),
                "Target B must NOT receive FirstImpressionFormed.");
        }

        /// <summary>
        /// No bilateral <see cref="FirstImpressionFormed"/> must fire when perception is asymmetric.
        /// A mutual impression requires both characters to notice each other.
        /// </summary>
        [TestMethod]
        public void OnTick_AsymmetricPerception_NoBilateralFirstImpression()
        {
            var a = BuildSpyHuman(LowNoise);
            var b = BuildSpyHuman(HighNoise);

            _fidelityOverrides[a.Id] = PerceptionFidelityLevel.Full;
            _fidelityOverrides[b.Id] = PerceptionFidelityLevel.Coarse;

            Place(a, b);
            _orchestrator.OnTick(Now, new[] { a, b });

            Assert.IsFalse(
                a.ReceivedEvents.OfType<FirstImpressionFormed>().Any(),
                "No bilateral FirstImpressionFormed must fire for A.");
        }

        /// <summary>
        /// <see cref="OneWayObservationFormed.Observer"/> must be A
        /// and <see cref="OneWayObservationFormed.Target"/> must be B.
        /// </summary>
        [TestMethod]
        public void OnTick_OneWayEvent_CorrectObserverAndTarget()
        {
            var a = BuildSpyHuman(LowNoise);
            var b = BuildSpyHuman(HighNoise);

            _fidelityOverrides[a.Id] = PerceptionFidelityLevel.Full;
            _fidelityOverrides[b.Id] = PerceptionFidelityLevel.Coarse;

            Place(a, b);
            _orchestrator.OnTick(Now, new[] { a, b });

            var ev = a.ReceivedEvents.OfType<OneWayObservationFormed>().Single();
            Assert.AreEqual(a.Id, ev.Observer, "Observer must be A.");
            Assert.AreEqual(b.Id, ev.Target, "Target must be B.");
        }

        /// <summary>
        /// Firing does not repeat when A already has an edge toward B.
        /// The guard in <c>FireFirstImpressions</c> skips the pair.
        /// </summary>
        [TestMethod]
        public void OnTick_OneWayAlreadySeeded_DoesNotFireAgain()
        {
            var a = BuildSpyHuman(LowNoise);
            var b = BuildSpyHuman(HighNoise);

            _fidelityOverrides[a.Id] = PerceptionFidelityLevel.Full;
            _fidelityOverrides[b.Id] = PerceptionFidelityLevel.Coarse;

            Place(a, b);

            // First tick — seeds the one-way edge
            _orchestrator.OnTick(Now, new[] { a, b });
            Assert.AreEqual(1, a.ReceivedEvents.OfType<OneWayObservationFormed>().Count(),
                "Exactly one OneWayObservationFormed on first tick.");

            // Simulate edge being present in A's snapshot (as DefaultRelationshipsEngine would do)
            a.SeedEdge(b.Id);

            // Second tick — must be suppressed
            _orchestrator.OnTick(Now.AddHours(1), new[] { a, b });
            Assert.AreEqual(1, a.ReceivedEvents.OfType<OneWayObservationFormed>().Count(),
                "OneWayObservationFormed must NOT fire again on second tick.");
        }

        /// <summary>
        /// When B gains Full fidelity on a later tick, bilateral
        /// <see cref="FirstImpressionFormed"/> fires for B while A is skipped
        /// (A already has an edge toward B from the one-way observation).
        /// </summary>
        [TestMethod]
        public void OnTick_OneWayThenMutual_BilateralFiredForBOnly()
        {
            var a = BuildSpyHuman(LowNoise);
            var b = BuildSpyHuman(HighNoise);

            _fidelityOverrides[a.Id] = PerceptionFidelityLevel.Full;
            _fidelityOverrides[b.Id] = PerceptionFidelityLevel.Coarse;

            Place(a, b);

            // Tick 1 — one-way: A sees B, B does not see A
            _orchestrator.OnTick(Now, new[] { a, b });
            Assert.IsTrue(a.ReceivedEvents.OfType<OneWayObservationFormed>().Any(),
                "A must have one-way event after tick 1.");
            Assert.IsFalse(b.ReceivedEvents.OfType<FirstImpressionFormed>().Any(),
                "B must have no impression after tick 1.");

            // Simulate A's engine having processed the one-way event into snapshot
            a.SeedEdge(b.Id);

            // Tick 2 — B now has Full fidelity and low noise (e.g. stepped into a quiet room)
            b.SetNoise(LowNoise);
            _fidelityOverrides[b.Id] = PerceptionFidelityLevel.Full;

            _orchestrator.OnTick(Now.AddHours(1), new[] { a, b });

            Assert.IsTrue(b.ReceivedEvents.OfType<FirstImpressionFormed>().Any(),
                "B must receive FirstImpressionFormed on tick 2.");
            Assert.AreEqual(0, a.ReceivedEvents.OfType<FirstImpressionFormed>().Count(),
                "A must NOT receive FirstImpressionFormed — A already has an edge.");
        }

        #endregion One-way observation

        // ══════════════════════════════════════════════════════════════════════
        // Transference (Topic C) — RouteSignificantOtherImprints + FireFirstImpressions
        // ══════════════════════════════════════════════════════════════════════

        #region Transference — RouteSignificantOtherImprints

        [TestMethod]
        public void RouteSignificantOtherImprints_ResolvesOtherAppearance_FromOrchestratorRoster()
        {
            var a = BuildSpyHuman(LowNoise);
            var b = BuildSpyHuman(LowNoise);
            var orchestrator = BuildOrchestrator(new RelationshipsConfig());

            a.SetSemanticMemory(new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet>
            {
                [b.Id] = new PersonBeliefSet(b.Id, new Dictionary<PersonBeliefKind, PersonBelief>
                {
                    [PersonBeliefKind.Warm] = new PersonBelief(b.Id, PersonBeliefKind.Warm, 0.7, 0.5, 4, Now)
                })
            }));
            a.SetLastOutbox(new SignificantOtherThresholdCrossed(Now, a.Id, b.Id, Commitment: 80.0));

            orchestrator.OnTick(Now, new[] { a, b });

            var captured = a.ReceivedEvents.OfType<SignificantOtherImprintCaptured>().ToList();
            Assert.AreEqual(1, captured.Count);
            Assert.AreEqual(b.Id, captured[0].Imprint.SourcePersonId);
            Assert.AreEqual(b.PhysicalAppearance.Face, captured[0].Imprint.FaceSummary);
            Assert.AreEqual(b.Personality.BigFive, captured[0].Imprint.PersonalitySummary);
            Assert.AreEqual(PersonBeliefKind.Warm, captured[0].Imprint.DominantBeliefKind);
            Assert.AreEqual(0.7, captured[0].Imprint.DominantBeliefStrength, 0.0001);
            Assert.AreEqual(80.0, captured[0].Imprint.Significance, 0.0001);
        }

        [TestMethod]
        public void RouteSignificantOtherImprints_SkipsCapture_WhenOtherNoLongerInScene()
        {
            var a = BuildSpyHuman(LowNoise);
            var movedAwayId = new HumanId(Guid.NewGuid());
            var orchestrator = BuildOrchestrator(new RelationshipsConfig());

            a.SetSemanticMemory(new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet>
            {
                [movedAwayId] = new PersonBeliefSet(movedAwayId, new Dictionary<PersonBeliefKind, PersonBelief>
                {
                    [PersonBeliefKind.Warm] = new PersonBelief(movedAwayId, PersonBeliefKind.Warm, 0.7, 0.5, 4, Now)
                })
            }));
            a.SetLastOutbox(new SignificantOtherThresholdCrossed(Now, a.Id, movedAwayId, Commitment: 80.0));

            orchestrator.OnTick(Now, new[] { a }); // movedAwayId is not in the scene roster

            Assert.IsFalse(a.ReceivedEvents.OfType<SignificantOtherImprintCaptured>().Any(),
                "Capture must be skipped gracefully when Other is no longer present in the scene roster");
        }

        [TestMethod]
        public void RouteSignificantOtherImprints_SkipsCapture_WhenNoDominantBeliefExists()
        {
            var a = BuildSpyHuman(LowNoise);
            var b = BuildSpyHuman(LowNoise);
            var orchestrator = BuildOrchestrator(new RelationshipsConfig());

            // a has no SemanticMemory beliefs about b at all.
            a.SetLastOutbox(new SignificantOtherThresholdCrossed(Now, a.Id, b.Id, Commitment: 80.0));

            orchestrator.OnTick(Now, new[] { a, b });

            Assert.IsFalse(a.ReceivedEvents.OfType<SignificantOtherImprintCaptured>().Any(),
                "Capture must be skipped when there is no belief pattern to transfer yet");
        }

        #endregion Transference — RouteSignificantOtherImprints

        #region Transference — FireFirstImpressions resemblance check

        private static Personality PersonalityWith(BigFive bigFive)
            => new(bigFive, AttachmentProfile.Secure, CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5),
                Sociosexuality.Intermediate, Chronotype.Neutral);

        [TestMethod]
        public void FireFirstImpressions_ResemblingNewPerson_EmitsTransferenceActivated()
        {
            var matchingBigFive = new BigFive(0.5, 0.5, 0.5, 0.5, 0.5);
            var a = BuildSpyHuman(LowNoise, PersonalityWith(matchingBigFive));
            var b = BuildSpyHuman(LowNoise, PersonalityWith(matchingBigFive));
            var orchestrator = BuildOrchestrator(new RelationshipsConfig());

            _fidelityOverrides[a.Id] = PerceptionFidelityLevel.Full;
            _fidelityOverrides[b.Id] = PerceptionFidelityLevel.Full;

            // a carries an imprint whose personality matches b exactly (and whose face — like every
            // SpyHuman's fixed test appearance — trivially matches b's too), so resemblance is maximal.
            a.SetSemanticMemory(new SemanticMemoryState(
                new Dictionary<HumanId, PersonBeliefSet>(),
                new[]
                {
                    new SignificantOtherImprint(
                        new HumanId(Guid.NewGuid()), Now, b.PhysicalAppearance.Face, matchingBigFive,
                        PersonBeliefKind.Warm, 0.8, Significance: 80.0)
                }));

            Place(a, b);
            orchestrator.OnTick(Now, new[] { a, b });

            var activated = a.ReceivedEvents.OfType<TransferenceActivated>().ToList();
            Assert.AreEqual(1, activated.Count);
            Assert.AreEqual(b.Id, activated[0].NewPerson);
            Assert.AreEqual(PersonBeliefKind.Warm, activated[0].TransferredKind);
        }

        [TestMethod]
        public void FireFirstImpressions_NonResemblingNewPerson_NoTransferenceActivated()
        {
            var a = BuildSpyHuman(LowNoise, PersonalityWith(new BigFive(0.5, 0.5, 0.5, 0.5, 0.5)));
            var b = BuildSpyHuman(LowNoise, PersonalityWith(new BigFive(1.0, 1.0, 1.0, 1.0, 1.0)));
            var orchestrator = BuildOrchestrator(new RelationshipsConfig());

            _fidelityOverrides[a.Id] = PerceptionFidelityLevel.Full;
            _fidelityOverrides[b.Id] = PerceptionFidelityLevel.Full;

            // Imprint's personality is maximally different from b's actual (1,1,1,1,1) BigFive —
            // combined resemblance stays well below the activation threshold even though facial
            // resemblance is maximal (all SpyHumans share an identical fixed test appearance).
            a.SetSemanticMemory(new SemanticMemoryState(
                new Dictionary<HumanId, PersonBeliefSet>(),
                new[]
                {
                    new SignificantOtherImprint(
                        new HumanId(Guid.NewGuid()), Now, b.PhysicalAppearance.Face, new BigFive(0.0, 0.0, 0.0, 0.0, 0.0),
                        PersonBeliefKind.Rejecting, 0.8, Significance: 80.0)
                }));

            Place(a, b);
            orchestrator.OnTick(Now, new[] { a, b });

            Assert.IsFalse(a.ReceivedEvents.OfType<TransferenceActivated>().Any(),
                "A dissimilar imprint must not activate transference");
        }

        [TestMethod]
        public void FirstImpression_NoSignificantOthers_NoTransferenceAttempted()
        {
            var a = BuildSpyHuman(LowNoise);
            var b = BuildSpyHuman(LowNoise);
            var orchestrator = BuildOrchestrator(new RelationshipsConfig());

            _fidelityOverrides[a.Id] = PerceptionFidelityLevel.Full;
            _fidelityOverrides[b.Id] = PerceptionFidelityLevel.Full;

            // a has no SemanticMemory / no SignificantOthers at all (default SpyHuman state).
            Place(a, b);
            orchestrator.OnTick(Now, new[] { a, b });

            Assert.IsTrue(a.ReceivedEvents.OfType<FirstImpressionFormed>().Any(), "Sanity check: impression still fires");
            Assert.IsFalse(a.ReceivedEvents.OfType<TransferenceActivated>().Any(),
                "No stored imprints — transference must not be attempted at all");
        }

        #endregion Transference — FireFirstImpressions resemblance check

        // ══════════════════════════════════════════════════════════════════════
        // Factory and stubs
        // ══════════════════════════════════════════════════════════════════════

        #region Factory methods

        private void Place(params SpyHuman[] chars)
        {
            foreach (var c in chars)
                _locationService.MoveCharacter(c.Id, TestRoom.Id);
        }

        private DefaultSceneOrchestrator BuildOrchestrator(RelationshipsConfig? relationshipsConfig = null)
            => new DefaultSceneOrchestrator(
                attractionCalculator: new NeutralAttractionCalculator(),
                locationService: _locationService,
                perceptionPolicy: new DelegatingPerceptionPolicy(_fidelityOverrides),
                perceptionOptions: DefaultOptions,
                lodRuntime: new AllBackgroundLodRuntime(),
                worldMap: new WorldMap(
                    new Dictionary<string, LocationDescriptor>(),
                    new Dictionary<string, IReadOnlyList<WorldConnection>>(),
                    new Dictionary<string, IReadOnlyList<string>>()),
                speedProvider: new ConstantSpeedProvider(80.0),
                rng: new Random(42),
                log: NullLogger<DefaultSceneOrchestrator>.Instance,
                objectProvider: new EmptyWorldObjectProvider(),
                options: new SceneOrchestratorOptions(),
                relationshipsConfig: relationshipsConfig);

        /// <summary>
        /// Builds a <see cref="SpyHuman"/> with the given noise level in its
        /// <see cref="InteractionSurface"/> — used by <see cref="CharacterPerceptionResolver"/>
        /// to determine perception range when fidelity is Coarse or LocalOnly.
        /// </summary>
        private static SpyHuman BuildSpyHuman(double noise, Personality? personality = null)
        {
            var id = new HumanId(Guid.NewGuid());
            personality ??= new Personality(
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

            return new SpyHuman(id, personality, snapshot);
        }

        #endregion Factory methods

        #region SpyHuman

        /// <summary>
        /// Minimal <see cref="IHuman"/> that records all received events
        /// and exposes helpers for seeding relationship edges (used to simulate
        /// what <see cref="DefaultRelationshipsEngine"/> would write to snapshot).
        /// </summary>
        private sealed class SpyHuman : IHuman
        {
            #region Private state

            private readonly List<IDomainEvent> _receivedEvents = new();
            private EnginesSnapshot _snapshot;
            private readonly Personality _personality;

            #endregion Private state

            public SpyHuman(HumanId id, Personality personality, EnginesSnapshot snapshot)
            {
                Id = id;
                _personality = personality;
                _snapshot = snapshot;
            }

            // ── IHuman ────────────────────────────────────────────────────────

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

            /// <summary>Null — no attraction calculation needed in orchestrator tests.</summary>
            public AttractionProfile? AttractionProfile => null;

            public EnginesSnapshot Snapshot => _snapshot;

            private IReadOnlyList<IDomainEvent> _lastOutbox = Array.Empty<IDomainEvent>();

            public IReadOnlyList<IDomainEvent> LastOutbox => _lastOutbox;

            /// <summary>Sets this character's LastOutbox — simulates events emitted on a prior tick.</summary>
            public void SetLastOutbox(params IDomainEvent[] events) => _lastOutbox = events;

            /// <summary>Sets this character's SemanticMemory snapshot (Topic C transference tests).</summary>
            public void SetSemanticMemory(GameEngineTools.Characters.Engines.SemanticMemory.SemanticMemoryState semanticMemory)
                => _snapshot = _snapshot with { SemanticMemory = semanticMemory };

            public int Age => 25;

            public StadiumType Stadium => StadiumType.Adult;

            /// <summary>Captured events — asserted on in tests.</summary>
            public IReadOnlyList<IDomainEvent> ReceivedEvents => _receivedEvents;

            // ── Event capture ─────────────────────────────────────────────────

            /// <summary>
            /// Captures the event. Does NOT process it through any engine — the
            /// orchestrator tests assert on received events, not on engine state.
            /// </summary>
            public void ReceiveEvent(IDomainEvent @event)
                => _receivedEvents.Add(@event);

            public void Tick(WDateTime now, WTimeSpan dt)
            { }

            public void RestoreSnapshot(EnginesSnapshot snapshot, WDateOnly today = default)
                => _snapshot = snapshot;

            public void FlushInbox()
            { }

            // ── Test helpers ──────────────────────────────────────────────────

            /// <summary>
            /// Manually inserts a minimal edge toward <paramref name="targetId"/>
            /// into <see cref="Snapshot"/>. Simulates what
            /// <see cref="DefaultRelationshipsEngine"/> would write after processing
            /// a relationship event — allows subsequent-tick guard checks to work.
            /// </summary>
            public void SeedEdge(HumanId targetId)
            {
                var edges = new Dictionary<HumanId, RelationshipEdge>(_snapshot.Relationships.Edges)
                {
                    [targetId] = new RelationshipEdge(
                        Id, targetId,
                        Like: 50,
                        Trust: 50,
                        Familiarity: 5,
                        AestheticAttraction: 30,
                        PhysicalAttraction: 30,
                        IntimateAffinity: 0,
                        SexualInterest: 0,
                        Closeness: 3,
                        Respect: 50,
                        Comfort: 50,
                        Breakdown: new DomainBreakdown(50, 50, 30, 50, 30))
                };
                _snapshot = _snapshot with
                {
                    Relationships = new RelationshipState(edges)
                };
            }

            /// <summary>
            /// Updates <see cref="InteractionSurface.Noise"/> in <see cref="Snapshot"/>
            /// — simulates moving to a quieter location between ticks.
            /// </summary>
            public void SetNoise(double noise)
            {
                var surface = _snapshot.InteractionSurface;
                _snapshot = _snapshot with
                {
                    InteractionSurface = new InteractionSurface(
                        surface.Location,
                        surface.HasPrivacy,
                        noise,
                        surface.Crowding,
                        surface.Kind)
                };
            }

            public int CompareTo(IHuman? other)
            {
                throw new NotImplementedException();
            }
        }

        #endregion SpyHuman

        #region Stubs

        /// <summary>
        /// Returns the fidelity level from <paramref name="overrides"/>,
        /// defaulting to <see cref="PerceptionFidelityLevel.Full"/>.
        /// </summary>
        private sealed class DelegatingPerceptionPolicy(
            Dictionary<HumanId, PerceptionFidelityLevel> overrides)
            : IPerceptionFidelityPolicy
        {
            public PerceptionFidelityLevel GetLevel(HumanId id)
                => overrides.TryGetValue(id, out var level) ? level : PerceptionFidelityLevel.Full;
        }

        /// <summary>Returns <see cref="CognitiveResolutionLevel.Background"/> for all characters.</summary>
        private sealed class AllBackgroundLodRuntime : ICognitiveResolutionLevelRuntime
        {
            public void Clear(HumanId id)
            { }

            public CognitiveResolutionLevel Get(HumanId id) => CognitiveResolutionLevel.Background;

            public void Set(HumanId id, CognitiveResolutionLevel level)
            { }
        }

        /// <summary>Always returns <see cref="AttractionResult.Neutral"/>.</summary>
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

        /// <summary>Returns a constant speed regardless of snapshot state or terrain.</summary>
        private sealed class ConstantSpeedProvider(double metersPerMinute) : IMovementSpeedProvider
        {
            public double GetSpeedMetersPerMinute(
                EnginesSnapshot snapshot, TerrainType terrain = TerrainType.Indoor) => metersPerMinute;
        }

        /// <summary>Returns empty collections for all object queries.</summary>
        private sealed class EmptyWorldObjectProvider : IWorldObjectProvider
        {
            public IEnumerable<WorldObject> GetObjectsAt(string locationId)
                => Enumerable.Empty<WorldObject>();

            public IEnumerable<WorldObject> GetAllObjects()
                => Enumerable.Empty<WorldObject>();

            public void AddObject(WorldObject obj)
            { }

            public WorldObject? FindObject(string objectId) => null;
        }

        #endregion Stubs
    }
}
