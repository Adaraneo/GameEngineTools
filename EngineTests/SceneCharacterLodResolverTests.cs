// SceneCharacterLodResolverTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
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

    /// <summary>
    /// Unit tests for <see cref="SceneCharacterLodResolver"/>.
    /// </summary>
    [TestClass]
    public class SceneCharacterLodResolverTests
    {
        #region Private fields

        private DefaultLocationService _locationService = default!;

        #endregion Private fields

        #region Setup

        [TestInitialize]
        public void Setup()
        {
            _locationService = new DefaultLocationService();
            _locationService.RegisterLocation(new LocationDescriptor(
                Id: "square",
                DisplayName: "Square",
                BaseNoise: 0.2,
                NoisePerPerson: 0.1,
                Capacity: 10,
                AllowsPrivacy: false,
                Type: LocationType.Social));
            _locationService.RegisterLocation(new LocationDescriptor(
                Id: "library",
                DisplayName: "Library",
                BaseNoise: 0.1,
                NoisePerPerson: 0.1,
                Capacity: 10,
                AllowsPrivacy: true,
                Type: LocationType.Work));
        }

        #endregion Setup

        #region Tests

        [TestMethod]
        public void Resolve_FocusCharacter_ReturnsPlayer()
        {
            var focus = NewHuman();
            _locationService.MoveCharacter(focus.Id, "square");

            var lod = SceneCharacterLodResolver.Resolve(focus, focus.Id, _locationService);

            Assert.AreEqual(CognitiveResolutionLevel.Player, lod);
        }

        [TestMethod]
        public void Resolve_AlwaysPlayerCharacter_ReturnsPlayer()
        {
            var focus = NewHuman();
            var alwaysPlayer = NewHuman();
            _locationService.MoveCharacter(focus.Id, "square");
            _locationService.MoveCharacter(alwaysPlayer.Id, "library");

            var lod = SceneCharacterLodResolver.Resolve(
                alwaysPlayer,
                focus.Id,
                _locationService,
                new HashSet<HumanId> { alwaysPlayer.Id });

            Assert.AreEqual(CognitiveResolutionLevel.Player, lod);
        }

        [TestMethod]
        public void Resolve_ColocatedNonFocusCharacter_ReturnsNearby()
        {
            var focus = NewHuman();
            var nearby = NewHuman();
            _locationService.MoveCharacter(focus.Id, "square");
            _locationService.MoveCharacter(nearby.Id, "square");

            var lod = SceneCharacterLodResolver.Resolve(nearby, focus.Id, _locationService);

            Assert.AreEqual(CognitiveResolutionLevel.Nearby, lod);
        }

        [TestMethod]
        public void Resolve_NonColocatedCharacter_ReturnsBackground()
        {
            var focus = NewHuman();
            var background = NewHuman();
            _locationService.MoveCharacter(focus.Id, "square");
            _locationService.MoveCharacter(background.Id, "library");

            var lod = SceneCharacterLodResolver.Resolve(background, focus.Id, _locationService);

            Assert.AreEqual(CognitiveResolutionLevel.Background, lod);
        }

        [TestMethod]
        public void Resolve_MissingLocation_ReturnsBackground()
        {
            var focus = NewHuman();
            var candidate = NewHuman();
            _locationService.MoveCharacter(focus.Id, "square");

            var lod = SceneCharacterLodResolver.Resolve(candidate, focus.Id, _locationService);

            Assert.AreEqual(CognitiveResolutionLevel.Background, lod);
        }

        #endregion Tests

        #region Helpers

        private static FakeHuman NewHuman()
            => new(new HumanId(Guid.NewGuid()));

        #endregion Helpers

        #region Test doubles

        private sealed class FakeHuman : IHuman
        {
            public FakeHuman(HumanId id)
            {
                Id = id;
            }

            public HumanId Id { get; }

            public Identity Identity => throw new NotImplementedException();

            public SexBiology Biology => SexBiology.Unknown;

            public Personality Personality => throw new NotImplementedException();

            public PsychologicalProfile PsychologyProfile => PsychologicalProfile.Default;

            public PhysicalAppearance PhysicalAppearance => throw new NotImplementedException();

            public AttractionProfile? AttractionProfile => null;

            public EnginesSnapshot Snapshot => throw new NotImplementedException();

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
            }
        }

        #endregion Test doubles
    }
}
