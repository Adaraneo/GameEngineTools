// CharacterPerceptionResolverTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
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
    using GameEngineTools.World.Simulation;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Unit tests for <see cref="CharacterPerceptionResolver"/>.
    /// </summary>
    /// <remarks>
    /// These tests intentionally avoid the full DI stack.
    /// <see cref="CharacterPerceptionResolver"/> is a pure orchestration helper:
    /// it depends only on the observer snapshot, location service, fidelity policy
    /// and world perception options.
    /// </remarks>
    [TestClass]
    public class CharacterPerceptionResolverTests
    {
        #region Constants

        private static readonly LocationDescriptor Tavern = new(
            Id: "tavern",
            DisplayName: "Tavern",
            BaseNoise: 0.40,
            NoisePerPerson: 0.05,
            Capacity: 20,
            AllowsPrivacy: false,
            Type: LocationType.Social);

        private static readonly LocationDescriptor Library = new(
            Id: "library",
            DisplayName: "Library",
            BaseNoise: 0.05,
            NoisePerPerson: 0.02,
            Capacity: 10,
            AllowsPrivacy: true,
            Type: LocationType.Work);

        private static readonly CharacterPerceptionOptions DefaultOptions = new()
        {
            MaxLocalOnlyTargets = 2,
            MaxCoarseTargets = 1,
            LocalOnlyNoiseThreshold = 0.85,
            LocalOnlyCrowdingThreshold = 0.90,
            CoarseNoiseThreshold = 0.60,
            CoarseCrowdingThreshold = 0.70
        };

        private static readonly Personality DefaultPersonality = new(
            new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
            AttachmentProfile.Secure,
            CommunicationStyle.Direct,
            new MotivationWeights(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5),
            Sociosexuality.Intermediate,
            Chronotype.Neutral);

        #endregion Constants

        #region Private fields

        private DefaultLocationService _locationService = default!;

        #endregion Private fields

        #region Setup

        [TestInitialize]
        public void Setup()
        {
            _locationService = new DefaultLocationService();
            _locationService.RegisterLocation(Tavern);
            _locationService.RegisterLocation(Library);
        }

        #endregion Setup

        #region Full fidelity

        /// <summary>
        /// Full fidelity should return all co-located characters ordered by descending salience.
        /// </summary>
        [TestMethod]
        public void GetPerceivedCharacters_Full_ReturnsAllColocatedCharactersOrderedBySalience()
        {
            // Arrange
            var observerId = NewId();
            var strongestId = NewId();
            var mediumId = NewId();
            var weakestId = NewId();
            var otherLocationId = NewId();

            var observer = NewFakeHuman(
                observerId,
                surface: new InteractionSurface("tavern", false, 0.20, 0.20, SurfaceKind.Social),
                edges: new Dictionary<HumanId, RelationshipEdge>
                {
                    [strongestId] = NewEdge(observerId, strongestId, closeness: 90, trust: 80, familiarity: 70, romantic: 60, sexual: 50),
                    [mediumId] = NewEdge(observerId, mediumId, closeness: 55, trust: 50, familiarity: 45, romantic: 20, sexual: 15),
                    [weakestId] = NewEdge(observerId, weakestId, closeness: 10, trust: 10, familiarity: 10, romantic: 0, sexual: 0)
                });

            var strongest = NewFakeHuman(strongestId);
            var medium = NewFakeHuman(mediumId);
            var weakest = NewFakeHuman(weakestId);
            var otherLocation = NewFakeHuman(otherLocationId);

            MoveTo(Tavern.Id, observer, strongest, medium, weakest);
            MoveTo(Library.Id, otherLocation);

            var characters = new IHuman[] { observer, strongest, medium, weakest, otherLocation };

            // Act
            var perceived = CharacterPerceptionResolver.GetPerceivedCharacters(
                observer,
                characters,
                _locationService,
                new FixedPerceptionFidelityPolicy(PerceptionFidelityLevel.Full),
                DefaultOptions);

            // Assert
            Assert.AreEqual(3, perceived.Count, "Full fidelity must return all co-located non-self characters.");
            Assert.AreEqual(strongestId, perceived[0].Id, "Highest-salience target must be first.");
            Assert.AreEqual(mediumId, perceived[1].Id, "Medium-salience target must be second.");
            Assert.AreEqual(weakestId, perceived[2].Id, "Lowest-salience target must be last.");
            Assert.IsFalse(perceived.Any(x => x.Id == observerId), "Observer must never perceive self.");
            Assert.IsFalse(perceived.Any(x => x.Id == otherLocationId), "Characters in a different location must not be returned.");
        }

        #endregion Full fidelity

        #region LocalOnly fidelity

        /// <summary>
        /// LocalOnly fidelity should keep only the configured number of most salient targets.
        /// </summary>
        [TestMethod]
        public void GetPerceivedCharacters_LocalOnly_ReturnsTopSalientTargetsUpToConfiguredLimit()
        {
            // Arrange
            var observerId = NewId();
            var strongestId = NewId();
            var secondId = NewId();
            var thirdId = NewId();

            var observer = NewFakeHuman(
                observerId,
                surface: new InteractionSurface("tavern", false, 0.30, 0.30, SurfaceKind.Social),
                edges: new Dictionary<HumanId, RelationshipEdge>
                {
                    [strongestId] = NewEdge(observerId, strongestId, closeness: 85, trust: 80, familiarity: 75, romantic: 55, sexual: 40),
                    [secondId] = NewEdge(observerId, secondId, closeness: 60, trust: 55, familiarity: 45, romantic: 15, sexual: 10),
                    [thirdId] = NewEdge(observerId, thirdId, closeness: 20, trust: 20, familiarity: 15, romantic: 0, sexual: 0)
                });

            var strongest = NewFakeHuman(strongestId);
            var second = NewFakeHuman(secondId);
            var third = NewFakeHuman(thirdId);

            MoveTo(Tavern.Id, observer, strongest, second, third);

            var characters = new IHuman[] { observer, strongest, second, third };

            // Act
            var perceived = CharacterPerceptionResolver.GetPerceivedCharacters(
                observer,
                characters,
                _locationService,
                new FixedPerceptionFidelityPolicy(PerceptionFidelityLevel.LocalOnly),
                DefaultOptions);

            // Assert
            Assert.AreEqual(2, perceived.Count, "LocalOnly must respect MaxLocalOnlyTargets.");
            Assert.AreEqual(strongestId, perceived[0].Id);
            Assert.AreEqual(secondId, perceived[1].Id);
            Assert.IsFalse(perceived.Any(x => x.Id == thirdId), "Lower-salience overflow target must be filtered out.");
        }

        /// <summary>
        /// LocalOnly fidelity should collapse when the observer's local interaction surface
        /// exceeds the configured noise threshold.
        /// </summary>
        [TestMethod]
        public void GetPerceivedCharacters_LocalOnly_HighNoise_ReturnsEmpty()
        {
            // Arrange
            var observerId = NewId();
            var targetId = NewId();

            var observer = NewFakeHuman(
                observerId,
                surface: new InteractionSurface("tavern", false, 0.90, 0.20, SurfaceKind.Social),
                edges: new Dictionary<HumanId, RelationshipEdge>
                {
                    [targetId] = NewEdge(observerId, targetId, closeness: 80, trust: 80, familiarity: 80, romantic: 30, sexual: 20)
                });

            var target = NewFakeHuman(targetId);

            MoveTo(Tavern.Id, observer, target);

            var characters = new IHuman[] { observer, target };

            // Act
            var perceived = CharacterPerceptionResolver.GetPerceivedCharacters(
                observer,
                characters,
                _locationService,
                new FixedPerceptionFidelityPolicy(PerceptionFidelityLevel.LocalOnly),
                DefaultOptions);

            // Assert
            Assert.AreEqual(0, perceived.Count, "LocalOnly perception must collapse above LocalOnlyNoiseThreshold.");
        }

        /// <summary>
        /// LocalOnly fidelity should collapse when the observer's local interaction surface
        /// exceeds the configured crowding threshold.
        /// </summary>
        [TestMethod]
        public void GetPerceivedCharacters_LocalOnly_HighCrowding_ReturnsEmpty()
        {
            // Arrange
            var observerId = NewId();
            var targetId = NewId();

            var observer = NewFakeHuman(
                observerId,
                surface: new InteractionSurface("tavern", false, 0.20, 0.95, SurfaceKind.Social),
                edges: new Dictionary<HumanId, RelationshipEdge>
                {
                    [targetId] = NewEdge(observerId, targetId, closeness: 80, trust: 80, familiarity: 80, romantic: 30, sexual: 20)
                });

            var target = NewFakeHuman(targetId);

            MoveTo(Tavern.Id, observer, target);

            var characters = new IHuman[] { observer, target };

            // Act
            var perceived = CharacterPerceptionResolver.GetPerceivedCharacters(
                observer,
                characters,
                _locationService,
                new FixedPerceptionFidelityPolicy(PerceptionFidelityLevel.LocalOnly),
                DefaultOptions);

            // Assert
            Assert.AreEqual(0, perceived.Count, "LocalOnly perception must collapse above LocalOnlyCrowdingThreshold.");
        }

        #endregion LocalOnly fidelity

        #region Coarse fidelity

        /// <summary>
        /// Coarse fidelity should return only the single most salient target when the local
        /// interaction surface remains below collapse thresholds.
        /// </summary>
        [TestMethod]
        public void GetPerceivedCharacters_Coarse_ReturnsSingleMostSalientTarget()
        {
            // Arrange
            var observerId = NewId();
            var strongestId = NewId();
            var weakerId = NewId();

            var observer = NewFakeHuman(
                observerId,
                surface: new InteractionSurface("tavern", false, 0.30, 0.30, SurfaceKind.Social),
                edges: new Dictionary<HumanId, RelationshipEdge>
                {
                    [strongestId] = NewEdge(observerId, strongestId, closeness: 75, trust: 70, familiarity: 65, romantic: 35, sexual: 25),
                    [weakerId] = NewEdge(observerId, weakerId, closeness: 20, trust: 20, familiarity: 20, romantic: 0, sexual: 0)
                });

            var strongest = NewFakeHuman(strongestId);
            var weaker = NewFakeHuman(weakerId);

            MoveTo(Tavern.Id, observer, strongest, weaker);

            var characters = new IHuman[] { observer, strongest, weaker };

            // Act
            var perceived = CharacterPerceptionResolver.GetPerceivedCharacters(
                observer,
                characters,
                _locationService,
                new FixedPerceptionFidelityPolicy(PerceptionFidelityLevel.Coarse),
                DefaultOptions);

            // Assert
            Assert.AreEqual(1, perceived.Count, "Coarse fidelity must respect MaxCoarseTargets.");
            Assert.AreEqual(strongestId, perceived[0].Id, "Coarse fidelity must keep the most salient target.");
        }

        /// <summary>
        /// Coarse fidelity should collapse when noise exceeds the coarse threshold.
        /// </summary>
        [TestMethod]
        public void GetPerceivedCharacters_Coarse_HighNoise_ReturnsEmpty()
        {
            // Arrange
            var observerId = NewId();
            var targetId = NewId();

            var observer = NewFakeHuman(
                observerId,
                surface: new InteractionSurface("tavern", false, 0.65, 0.20, SurfaceKind.Social),
                edges: new Dictionary<HumanId, RelationshipEdge>
                {
                    [targetId] = NewEdge(observerId, targetId, closeness: 70, trust: 70, familiarity: 70, romantic: 20, sexual: 10)
                });

            var target = NewFakeHuman(targetId);

            MoveTo(Tavern.Id, observer, target);

            var characters = new IHuman[] { observer, target };

            // Act
            var perceived = CharacterPerceptionResolver.GetPerceivedCharacters(
                observer,
                characters,
                _locationService,
                new FixedPerceptionFidelityPolicy(PerceptionFidelityLevel.Coarse),
                DefaultOptions);

            // Assert
            Assert.AreEqual(0, perceived.Count, "Coarse perception must collapse above CoarseNoiseThreshold.");
        }

        /// <summary>
        /// Coarse fidelity should collapse when crowding exceeds the coarse threshold.
        /// </summary>
        [TestMethod]
        public void GetPerceivedCharacters_Coarse_HighCrowding_ReturnsEmpty()
        {
            // Arrange
            var observerId = NewId();
            var targetId = NewId();

            var observer = NewFakeHuman(
                observerId,
                surface: new InteractionSurface("tavern", false, 0.20, 0.75, SurfaceKind.Social),
                edges: new Dictionary<HumanId, RelationshipEdge>
                {
                    [targetId] = NewEdge(observerId, targetId, closeness: 70, trust: 70, familiarity: 70, romantic: 20, sexual: 10)
                });

            var target = NewFakeHuman(targetId);

            MoveTo(Tavern.Id, observer, target);

            var characters = new IHuman[] { observer, target };

            // Act
            var perceived = CharacterPerceptionResolver.GetPerceivedCharacters(
                observer,
                characters,
                _locationService,
                new FixedPerceptionFidelityPolicy(PerceptionFidelityLevel.Coarse),
                DefaultOptions);

            // Assert
            Assert.AreEqual(0, perceived.Count, "Coarse perception must collapse above CoarseCrowdingThreshold.");
        }

        #endregion Coarse fidelity

        #region Edge cases

        /// <summary>
        /// An unplaced observer cannot perceive anybody because co-location cannot be resolved.
        /// </summary>
        [TestMethod]
        public void GetPerceivedCharacters_ObserverWithoutLocation_ReturnsEmpty()
        {
            // Arrange
            var observerId = NewId();
            var targetId = NewId();

            var observer = NewFakeHuman(
                observerId,
                surface: new InteractionSurface("unknown", false, 0.20, 0.20, SurfaceKind.Unknown),
                edges: new Dictionary<HumanId, RelationshipEdge>
                {
                    [targetId] = NewEdge(observerId, targetId, closeness: 70, trust: 70, familiarity: 70, romantic: 20, sexual: 10)
                });

            var target = NewFakeHuman(targetId);
            MoveTo(Tavern.Id, target);

            var characters = new IHuman[] { observer, target };

            // Act
            var perceived = CharacterPerceptionResolver.GetPerceivedCharacters(
                observer,
                characters,
                _locationService,
                new FixedPerceptionFidelityPolicy(PerceptionFidelityLevel.Full),
                DefaultOptions);

            // Assert
            Assert.AreEqual(0, perceived.Count, "Observer without a current location must perceive nobody.");
        }

        /// <summary>
        /// Characters with no explicit edge still receive the baseline salience and can be
        /// perceived under Full fidelity when they are co-located.
        /// </summary>
        [TestMethod]
        public void GetPerceivedCharacters_NoRelationshipEdge_StillReturnsColocatedCharacterUnderFull()
        {
            // Arrange
            var observer = NewFakeHuman(
                NewId(),
                surface: new InteractionSurface("tavern", false, 0.20, 0.20, SurfaceKind.Social));

            var target = NewFakeHuman(NewId());

            MoveTo(Tavern.Id, observer, target);

            var characters = new IHuman[] { observer, target };

            // Act
            var perceived = CharacterPerceptionResolver.GetPerceivedCharacters(
                observer,
                characters,
                _locationService,
                new FixedPerceptionFidelityPolicy(PerceptionFidelityLevel.Full),
                DefaultOptions);

            // Assert
            Assert.AreEqual(1, perceived.Count, "Missing relationship edge must not hide a co-located target under Full fidelity.");
            Assert.AreEqual(target.Id, perceived[0].Id);
        }

        #endregion Edge cases

        #region Factory methods

        private static HumanId NewId() => new(Guid.NewGuid());

        private static RelationshipEdge NewEdge(
            HumanId observerId,
            HumanId targetId,
            double closeness,
            double trust,
            double familiarity,
            double romantic,
            double sexual)
            => new(
                observerId,
                targetId,
                Like: 50,
                Trust: trust,
                Familiarity: familiarity,
                AestheticAttraction: 50,
                PhysicalAttraction: 50,
                IntimateAffinity: romantic,
                SexualInterest: sexual,
                Closeness: closeness,
                Respect: 50,
                Comfort: 50,
                Breakdown: new DomainBreakdown(50, 50, 50, 50, 50));

        private static FakeHuman NewFakeHuman(
            HumanId id,
            InteractionSurface? surface = null,
            IReadOnlyDictionary<HumanId, RelationshipEdge>? edges = null)
        {
            var snapshot = new EnginesSnapshot(
                new PhysiologyState(100, 0, 0, 0, 0, 0, 0, Cycle: null, Pregnancy: null),
                new PsychologyState(0, 0.5, 0.5, 0, 0, DiscreteEmotion.Neutral),
                new BehaviorState(0, 0, 0, 0, 0, 0, CurrentPlan: null),
                surface ?? new InteractionSurface("unknown", false, 0, 0, SurfaceKind.Unknown),
                new RelationshipState(edges ?? new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(Array.Empty<EpisodicMemory>()),
                SemanticMemory: SemanticMemoryState.Empty);

            return new FakeHuman(id, snapshot);
        }

        private void MoveTo(string locationId, params IHuman[] characters)
        {
            foreach (var character in characters)
            {
                _locationService.MoveCharacter(character.Id, locationId);
            }
        }

        #endregion Factory methods

        #region Test doubles

        private sealed class FixedPerceptionFidelityPolicy : IPerceptionFidelityPolicy
        {
            private readonly PerceptionFidelityLevel _level;

            public FixedPerceptionFidelityPolicy(PerceptionFidelityLevel level)
            {
                _level = level;
            }

            public PerceptionFidelityLevel GetLevel(HumanId human) => _level;
        }

        /// <summary>
        /// Minimal fake <see cref="IHuman"/> carrying only the snapshot data required by the resolver.
        /// </summary>
        private sealed class FakeHuman : IHuman
        {
            public FakeHuman(HumanId id, EnginesSnapshot snapshot)
            {
                Id = id;
                Snapshot = snapshot;
            }

            public HumanId Id { get; }

            public Identity Identity => throw new NotImplementedException();

            public SexBiology Biology => SexBiology.Unknown;

            public Personality Personality => DefaultPersonality;

            public PsychologicalProfile PsychologyProfile => PsychologicalProfile.Default;

            public PhysicalAppearance PhysicalAppearance => throw new NotImplementedException();

            public AttractionProfile? AttractionProfile => null;

            public EnginesSnapshot Snapshot { get; private set; }

            public IReadOnlyList<IDomainEvent> LastOutbox => Array.Empty<IDomainEvent>();

            public int Age => 0;

            public StadiumType Stadium => StadiumType.Adult;

            public void Tick(WDateTime now, WTimeSpan dt)
            {
            }

            public void ReceiveEvent(IDomainEvent @event)
            {
            }

            public void RestoreSnapshot(EnginesSnapshot snapshot)
            {
                Snapshot = snapshot;
            }

            public void FlushInbox()
            {
            }

            public void RestoreSnapshot(EnginesSnapshot snapshot, WDateOnly today = default) => RestoreSnapshot(snapshot);

            public int CompareTo(IHuman? other)
            {
                throw new NotImplementedException();
            }
        }

        #endregion Test doubles
    }
}
