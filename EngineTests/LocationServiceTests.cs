// LocationServiceTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Unit tests for <see cref="DefaultLocationService"/>.
    /// </summary>
    /// <remarks>
    /// No DI stack, no engine — <see cref="DefaultLocationService"/> is a plain
    /// domain class and is tested in isolation.
    /// <para>
    /// Coverage:
    /// <list type="bullet">
    ///   <item>Location registration and character movement.</item>
    ///   <item>Noise and Crowding computation based on character count.</item>
    ///   <item>Privacy (HasPrivacy) derived from capacity and AllowsPrivacy flag.</item>
    ///   <item>Smart dispatch — only on location change, not every tick.</item>
    ///   <item>ForceAll — dispatch even without movement (first tick).</item>
    ///   <item>Invalid input handling (unregistered location, unknown character).</item>
    /// </list>
    /// </para>
    /// </remarks>
    [TestClass]
    public class LocationServiceTests
    {
        #region Constants

        // Calibration reference:
        //
        //   Library — BaseNoise=0.05, NoisePerPerson=0.02, Capacity=10, AllowsPrivacy=true
        //   Tavern  — BaseNoise=0.4,  NoisePerPerson=0.05, Capacity=20, AllowsPrivacy=false
        //   Square  — BaseNoise=0.3,  NoisePerPerson=0.03, Capacity=50, AllowsPrivacy=false

        private static readonly LocationDescriptor Library = new LocationDescriptor(
            Id: "library",
            DisplayName: "Library",
            BaseNoise: 0.05,
            NoisePerPerson: 0.02,
            Capacity: 10,
            AllowsPrivacy: true,
            LocationType.Work);

        private static readonly LocationDescriptor Tavern = new LocationDescriptor(
            Id: "tavern",
            DisplayName: "Tavern",
            BaseNoise: 0.4,
            NoisePerPerson: 0.05,
            Capacity: 20,
            AllowsPrivacy: false,
            LocationType.Social);

        private static readonly LocationDescriptor Square = new LocationDescriptor(
            Id: "square",
            DisplayName: "Village Square",
            BaseNoise: 0.3,
            NoisePerPerson: 0.03,
            Capacity: 50,
            AllowsPrivacy: false,
            LocationType.Social);

        /// <summary>Shared simulation time — the exact value is irrelevant, kept consistent across tests.</summary>
        private static readonly WDateTime Now = new WDateTime(0);

        #endregion Constants

        #region Private fields

        /// <summary>System under test — fresh instance before each test.</summary>
        private DefaultLocationService _sut = default!;

        #endregion Private fields

        #region Setup

        [TestInitialize]
        public void Setup()
        {
            _sut = new DefaultLocationService();
        }

        #endregion Setup

        // ════════════════════════════════════════════════════════════════════
        // Section 1 — Registration and character movement
        // ════════════════════════════════════════════════════════════════════

        #region RegisterLocation — basic behaviour

        /// <summary>
        /// Verifies that <see cref="ILocationService.MoveCharacter"/> throws
        /// <see cref="InvalidOperationException"/> when the target location has not been registered.
        /// </summary>
        [TestMethod]
        public void MoveCharacter_UnregisteredLocation_ThrowsInvalidOperationException()
        {
            // Arrange
            var id = NewId();

            // Act + Assert
            Assert.ThrowsException<InvalidOperationException>(
                () => _sut.MoveCharacter(id, "nonexistent"),
                "MoveCharacter must throw for an unregistered location.");
        }

        /// <summary>
        /// Verifies that <see cref="ILocationService.GetLocation"/> returns the correct
        /// location id after the character has been moved there.
        /// </summary>
        [TestMethod]
        public void GetLocation_AfterMoveCharacter_ReturnsCorrectLocationId()
        {
            // Arrange
            _sut.RegisterLocation(Library);
            var id = NewId();

            // Act
            _sut.MoveCharacter(id, Library.Id);

            // Assert
            Assert.AreEqual(Library.Id, _sut.GetLocation(id));
        }

        /// <summary>
        /// Verifies that <see cref="ILocationService.GetLocation"/> returns null
        /// for a character that has not been placed in any location.
        /// </summary>
        [TestMethod]
        public void GetLocation_UnplacedCharacter_ReturnsNull()
        {
            // Arrange
            var id = NewId();

            // Act
            var result = _sut.GetLocation(id);

            // Assert
            Assert.IsNull(result, "An unplaced character must return null.");
        }

        /// <summary>
        /// Verifies that moving a character to a different location overwrites the previous assignment.
        /// </summary>
        [TestMethod]
        public void MoveCharacter_ToNewLocation_OverridesPreviousLocation()
        {
            // Arrange
            _sut.RegisterLocation(Library);
            _sut.RegisterLocation(Tavern);
            var id = NewId();
            _sut.MoveCharacter(id, Library.Id);

            // Act
            _sut.MoveCharacter(id, Tavern.Id);

            // Assert
            Assert.AreEqual(Tavern.Id, _sut.GetLocation(id));
        }

        #endregion RegisterLocation — basic behaviour

        // ════════════════════════════════════════════════════════════════════
        // Section 2 — Noise computation
        //
        // Calibration (Library: BaseNoise=0.05, NoisePerPerson=0.02):
        //   1 character   → 0.05 + 0.02*1  = 0.07
        //   5 characters  → 0.05 + 0.02*5  = 0.15
        //   50 characters → clamp(0.05 + 0.02*50) = clamp(1.05) = 1.0
        // ════════════════════════════════════════════════════════════════════

        #region Noise — computation

        /// <summary>
        /// One character in the library: Noise = BaseNoise + NoisePerPerson * 1 = 0.07.
        /// </summary>
        [TestMethod]
        public void DispatchContextEvents_OneCharacterInLibrary_NoiseEqualsBasePlusOnePerson()
        {
            // Arrange
            _sut.RegisterLocation(Library);
            var character = NewFakeCharacter();
            _sut.MoveCharacter(character.Id, Library.Id);

            // Act
            _sut.DispatchContextEvents(Now, new[] { character }, forceAll: true);

            // Assert
            var ev = character.ReceivedEvents.OfType<ContextChanged>().Single();
            Assert.AreEqual(0.07, ev.Noise, delta: 0.001,
                $"1 character in library must yield Noise=0.07. Actual: {ev.Noise:F4}");
        }

        /// <summary>
        /// Five characters in the library: Noise = 0.05 + 0.02 * 5 = 0.15.
        /// </summary>
        [TestMethod]
        public void DispatchContextEvents_FiveCharactersInLibrary_NoiseScalesCorrectly()
        {
            // Arrange
            _sut.RegisterLocation(Library);
            var characters = BuildCharactersInLocation(5, Library.Id);

            // Act
            _sut.DispatchContextEvents(Now, characters, forceAll: true);

            // Assert — all characters in the same location must receive the same Noise value
            foreach (var c in characters)
            {
                var ev = c.ReceivedEvents.OfType<ContextChanged>().Single();
                Assert.AreEqual(0.15, ev.Noise, delta: 0.001,
                    $"5 characters in library must yield Noise=0.15. Actual: {ev.Noise:F4}");
            }
        }

        /// <summary>
        /// Overcrowded location: Noise is clamped to 1.0 and does not exceed the maximum.
        /// </summary>
        [TestMethod]
        public void DispatchContextEvents_OvercrowdedLocation_NoiseClampedToOne()
        {
            // Arrange — Library: BaseNoise=0.05 + NoisePerPerson=0.02 * 50 = 1.05 → clamped to 1.0
            _sut.RegisterLocation(Library);
            var characters = BuildCharactersInLocation(50, Library.Id);

            // Act
            _sut.DispatchContextEvents(Now, characters, forceAll: true);

            // Assert
            var ev = characters[0].ReceivedEvents.OfType<ContextChanged>().Single();
            Assert.AreEqual(1.0, ev.Noise, delta: 0.001,
                $"Overcrowded location must clamp Noise to 1.0. Actual: {ev.Noise:F4}");
        }

        #endregion Noise — computation

        // ════════════════════════════════════════════════════════════════════
        // Section 3 — Crowding computation
        //
        // Calibration (Library: Capacity=10):
        //   1 character   → 1/10  = 0.1
        //   5 characters  → 5/10  = 0.5
        //   10 characters → 10/10 = 1.0  (full capacity)
        //   15 characters → clamp(15/10) = 1.0
        // ════════════════════════════════════════════════════════════════════

        #region Crowding — computation

        /// <summary>
        /// One character in the library (capacity 10): Crowding = 1 / 10 = 0.1.
        /// </summary>
        [TestMethod]
        public void DispatchContextEvents_OneCharacterInLibrary_CrowdingEqualsOneOverCapacity()
        {
            // Arrange
            _sut.RegisterLocation(Library);
            var character = NewFakeCharacter();
            _sut.MoveCharacter(character.Id, Library.Id);

            // Act
            _sut.DispatchContextEvents(Now, new[] { character }, forceAll: true);

            // Assert
            var ev = character.ReceivedEvents.OfType<ContextChanged>().Single();
            Assert.AreEqual(0.1, ev.Crowding, delta: 0.001,
                $"1 character with capacity 10 must yield Crowding=0.1. Actual: {ev.Crowding:F4}");
        }

        /// <summary>
        /// Exactly full capacity: Crowding = 1.0.
        /// </summary>
        [TestMethod]
        public void DispatchContextEvents_AtCapacity_CrowdingEqualsOne()
        {
            // Arrange — Library has Capacity=10
            _sut.RegisterLocation(Library);
            var characters = BuildCharactersInLocation(10, Library.Id);

            // Act
            _sut.DispatchContextEvents(Now, characters, forceAll: true);

            // Assert
            var ev = characters[0].ReceivedEvents.OfType<ContextChanged>().Single();
            Assert.AreEqual(1.0, ev.Crowding, delta: 0.001,
                $"Full capacity must yield Crowding=1.0. Actual: {ev.Crowding:F4}");
        }

        /// <summary>
        /// Characters above capacity: Crowding is clamped to 1.0.
        /// </summary>
        [TestMethod]
        public void DispatchContextEvents_AboveCapacity_CrowdingClampedToOne()
        {
            // Arrange — 20 characters into Library with Capacity=10
            _sut.RegisterLocation(Library);
            var characters = BuildCharactersInLocation(20, Library.Id);

            // Act
            _sut.DispatchContextEvents(Now, characters, forceAll: true);

            // Assert
            var ev = characters[0].ReceivedEvents.OfType<ContextChanged>().Single();
            Assert.AreEqual(1.0, ev.Crowding, delta: 0.001,
                $"Overcrowded location must clamp Crowding to 1.0. Actual: {ev.Crowding:F4}");
        }

        #endregion Crowding — computation

        // ════════════════════════════════════════════════════════════════════
        // Section 4 — Privacy (HasPrivacy)
        //
        // Rule: HasPrivacy = AllowsPrivacy && characterCount <= 2
        //
        //   Library (AllowsPrivacy=true):
        //     1–2 characters → true
        //     3+ characters  → false
        //   Tavern (AllowsPrivacy=false):
        //     always false, regardless of character count
        // ════════════════════════════════════════════════════════════════════

        #region HasPrivacy — privacy

        /// <summary>
        /// One character in a location that allows privacy: HasPrivacy must be true.
        /// </summary>
        [TestMethod]
        public void DispatchContextEvents_OneCharacterInPrivateLocation_HasPrivacyIsTrue()
        {
            // Arrange
            _sut.RegisterLocation(Library);
            var character = NewFakeCharacter();
            _sut.MoveCharacter(character.Id, Library.Id);

            // Act
            _sut.DispatchContextEvents(Now, new[] { character }, forceAll: true);

            // Assert
            var ev = character.ReceivedEvents.OfType<ContextChanged>().Single();
            Assert.IsTrue(ev.HasPrivacy,
                "1 character in a private location must have HasPrivacy=true.");
        }

        /// <summary>
        /// Two characters in a location that allows privacy: HasPrivacy must be true (pair).
        /// </summary>
        [TestMethod]
        public void DispatchContextEvents_TwoCharactersInPrivateLocation_HasPrivacyIsTrue()
        {
            // Arrange
            _sut.RegisterLocation(Library);
            var characters = BuildCharactersInLocation(2, Library.Id);

            // Act
            _sut.DispatchContextEvents(Now, characters, forceAll: true);

            // Assert
            var ev = characters[0].ReceivedEvents.OfType<ContextChanged>().Single();
            Assert.IsTrue(ev.HasPrivacy,
                "2 characters in a private location must have HasPrivacy=true.");
        }

        /// <summary>
        /// Three or more characters: HasPrivacy must be false even when the location allows it.
        /// </summary>
        [TestMethod]
        public void DispatchContextEvents_ThreeCharactersInPrivateLocation_HasPrivacyIsFalse()
        {
            // Arrange
            _sut.RegisterLocation(Library);
            var characters = BuildCharactersInLocation(3, Library.Id);

            // Act
            _sut.DispatchContextEvents(Now, characters, forceAll: true);

            // Assert
            var ev = characters[0].ReceivedEvents.OfType<ContextChanged>().Single();
            Assert.IsFalse(ev.HasPrivacy,
                "3+ characters must cancel privacy even in a private-capable location.");
        }

        /// <summary>
        /// A location with AllowsPrivacy=false never grants privacy, even with a single character.
        /// </summary>
        [TestMethod]
        public void DispatchContextEvents_OneCharacterInPublicLocation_HasPrivacyIsFalse()
        {
            // Arrange — Tavern has AllowsPrivacy=false
            _sut.RegisterLocation(Tavern);
            var character = NewFakeCharacter();
            _sut.MoveCharacter(character.Id, Tavern.Id);

            // Act
            _sut.DispatchContextEvents(Now, new[] { character }, forceAll: true);

            // Assert
            var ev = character.ReceivedEvents.OfType<ContextChanged>().Single();
            Assert.IsFalse(ev.HasPrivacy,
                "A public location (AllowsPrivacy=false) must never produce HasPrivacy=true.");
        }

        #endregion HasPrivacy — privacy

        // ════════════════════════════════════════════════════════════════════
        // Section 5 — Smart dispatch (only on location change)
        // ════════════════════════════════════════════════════════════════════

        #region Dispatch — only on change

        /// <summary>
        /// A character that stays in the same location receives a <see cref="ContextChanged"/>
        /// only once — a second dispatch without movement must send nothing.
        /// </summary>
        [TestMethod]
        public void DispatchContextEvents_SameLocationTwice_OnlyOneEventDispatched()
        {
            // Arrange
            _sut.RegisterLocation(Library);
            var character = NewFakeCharacter();
            _sut.MoveCharacter(character.Id, Library.Id);
            var chars = new[] { character };

            // Act — first dispatch
            _sut.DispatchContextEvents(Now, chars, forceAll: false);
            // Act — second dispatch without movement
            _sut.DispatchContextEvents(Now, chars, forceAll: false);

            // Assert
            var count = character.ReceivedEvents.OfType<ContextChanged>().Count();
            Assert.AreEqual(1, count,
                $"Without movement exactly 1 event must be dispatched. Actual: {count}");
        }

        /// <summary>
        /// After moving to a new location a fresh <see cref="ContextChanged"/> must be dispatched.
        /// </summary>
        [TestMethod]
        public void DispatchContextEvents_AfterMoveToNewLocation_DispatchesAgain()
        {
            // Arrange
            _sut.RegisterLocation(Library);
            _sut.RegisterLocation(Tavern);
            var character = NewFakeCharacter();
            _sut.MoveCharacter(character.Id, Library.Id);
            var chars = new[] { character };

            // Act — first tick (library)
            _sut.DispatchContextEvents(Now, chars, forceAll: false);
            // Act — move then second tick
            _sut.MoveCharacter(character.Id, Tavern.Id);
            _sut.DispatchContextEvents(Now, chars, forceAll: false);

            // Assert — two distinct events with two distinct locations
            var events = character.ReceivedEvents.OfType<ContextChanged>().ToList();
            Assert.AreEqual(2, events.Count,
                $"After movement a new event must be dispatched. Total: {events.Count}");
            Assert.AreEqual(Library.Id, events[0].Location);
            Assert.AreEqual(Tavern.Id,  events[1].Location);
        }

        /// <summary>
        /// ForceAll=true must dispatch an event even to characters whose location has not changed.
        /// Typical use case: first simulation tick.
        /// </summary>
        [TestMethod]
        public void DispatchContextEvents_ForceAllAfterNoPrevious_DispatchesEvenWithoutMove()
        {
            // Arrange — two dispatches on the same location; second one with forceAll=true
            _sut.RegisterLocation(Library);
            var character = NewFakeCharacter();
            _sut.MoveCharacter(character.Id, Library.Id);
            var chars = new[] { character };

            _sut.DispatchContextEvents(Now, chars, forceAll: false); // normal — registers state
            _sut.DispatchContextEvents(Now, chars, forceAll: true);  // force — must dispatch again

            // Assert
            var count = character.ReceivedEvents.OfType<ContextChanged>().Count();
            Assert.AreEqual(2, count,
                $"ForceAll must dispatch even without movement. Actual: {count}");
        }

        #endregion Dispatch — only on change

        // ════════════════════════════════════════════════════════════════════
        // Section 6 — Multiple characters across multiple locations
        //
        // Key invariant: each location computes Noise/Crowding independently.
        // Characters in the Tavern must not affect Library values and vice versa.
        // ════════════════════════════════════════════════════════════════════

        #region Multiple locations at once

        /// <summary>
        /// Two characters in different locations must each receive a <see cref="ContextChanged"/>
        /// with values computed for their own location — one location must not affect the other.
        /// </summary>
        [TestMethod]
        public void DispatchContextEvents_CharactersInDifferentLocations_ReceiveCorrectSeparateContexts()
        {
            // Arrange
            _sut.RegisterLocation(Library);
            _sut.RegisterLocation(Tavern);

            var libraryChar = NewFakeCharacter();
            var tavernChar  = NewFakeCharacter();

            _sut.MoveCharacter(libraryChar.Id, Library.Id);
            _sut.MoveCharacter(tavernChar.Id,  Tavern.Id);

            var characters = new IHuman[] { libraryChar, tavernChar };

            // Act
            _sut.DispatchContextEvents(Now, characters, forceAll: true);

            // Assert — library: 1 character → Noise=0.07, Crowding=0.10
            var libEv = libraryChar.ReceivedEvents.OfType<ContextChanged>().Single();
            Assert.AreEqual(Library.Id, libEv.Location);
            Assert.AreEqual(0.07, libEv.Noise,    delta: 0.001, "Library Noise mismatch.");
            Assert.AreEqual(0.10, libEv.Crowding, delta: 0.001, "Library Crowding mismatch.");

            // Assert — tavern: 1 character → Noise = 0.4 + 0.05*1 = 0.45, Crowding = 1/20 = 0.05
            var tavEv = tavernChar.ReceivedEvents.OfType<ContextChanged>().Single();
            Assert.AreEqual(Tavern.Id, tavEv.Location);
            Assert.AreEqual(0.45, tavEv.Noise,    delta: 0.001, "Tavern Noise mismatch.");
            Assert.AreEqual(0.05, tavEv.Crowding, delta: 0.001, "Tavern Crowding mismatch.");
        }

        /// <summary>
        /// A character with no location assigned must not receive any event.
        /// </summary>
        [TestMethod]
        public void DispatchContextEvents_UnplacedCharacter_ReceivesNoEvent()
        {
            // Arrange — no location assigned
            _sut.RegisterLocation(Library);
            var unplaced = NewFakeCharacter();

            // Act
            _sut.DispatchContextEvents(Now, new[] { unplaced }, forceAll: true);

            // Assert
            var count = unplaced.ReceivedEvents.OfType<ContextChanged>().Count();
            Assert.AreEqual(0, count, "An unplaced character must not receive ContextChanged.");
        }

        #endregion Multiple locations at once

        #region Factory methods

        /// <summary>Returns a new unique <see cref="HumanId"/>.</summary>
        private static HumanId NewId() => new HumanId(Guid.NewGuid());

        /// <summary>
        /// Creates a new fake character that captures all received events.
        /// </summary>
        private static FakeHuman NewFakeCharacter() => new FakeHuman(NewId());

        /// <summary>
        /// Creates <paramref name="count"/> fake characters, all placed in the given location.
        /// </summary>
        /// <param name="count">Number of characters to create.</param>
        /// <param name="locationId">Location id to move all characters into.</param>
        private FakeHuman[] BuildCharactersInLocation(int count, string locationId)
        {
            var characters = Enumerable.Range(0, count)
                .Select(_ => NewFakeCharacter())
                .ToArray();

            foreach (var c in characters)
            {
                _sut.MoveCharacter(c.Id, locationId);
            }

            return characters;
        }

        #endregion Factory methods

        #region Fake IHuman

        // Why a hand-written fake instead of Moq?
        // DispatchContextEvents calls character.ReceiveEvent() — we want to capture
        // events in a list and then assert on them. FakeHuman is simpler and more
        // readable than a Moq mock with a Capture<> callback setup.

        /// <summary>
        /// Minimal <see cref="IHuman"/> implementation for test purposes.
        /// Captures all events delivered via <see cref="ReceiveEvent"/>.
        /// </summary>
        private sealed class FakeHuman : IHuman
        {
            #region Properties

            /// <summary>Gets the unique identifier of this character.</summary>
            public HumanId Id { get; }

            /// <summary>Gets the list of events captured via <see cref="ReceiveEvent"/>.</summary>
            public List<IDomainEvent> ReceivedEvents { get; } = new();

            /// <summary>Not used in dispatch tests — returns default.</summary>
            public EnginesSnapshot Snapshot => default!;

            /// <summary>Not used in dispatch tests — returns empty.</summary>
            public IReadOnlyList<IDomainEvent> LastOutbox => Array.Empty<IDomainEvent>();

            public Identity Identity => throw new NotImplementedException();

            public SexBiology Biology => throw new NotImplementedException();

            public Personality Personality => throw new NotImplementedException();

            public PhysicalAppearance PhysicalAppearance => throw new NotImplementedException();

            public AttractionProfile AttractionProfile => throw new NotImplementedException();

            public int Age => throw new NotImplementedException();

            #endregion Properties

            #region Constructor

            /// <summary>
            /// Initializes a new instance of <see cref="FakeHuman"/> with the given identifier.
            /// </summary>
            /// <param name="id">Unique character identifier.</param>
            public FakeHuman(HumanId id) => Id = id;

            #endregion Constructor

            #region IHuman

            /// <summary>
            /// Captures the incoming event into <see cref="ReceivedEvents"/>
            /// instead of enqueueing it to an engine.
            /// </summary>
            /// <param name="event">The domain event to capture.</param>
            public void ReceiveEvent(IDomainEvent @event)
                => ReceivedEvents.Add(@event);

            /// <summary>Not exercised in these tests.</summary>
            public void Tick(WDateTime now, WTimeSpan dt) { }

            public void RestoreSnapshot(EnginesSnapshot snapshot)
            {
                throw new NotImplementedException();
            }

            #endregion IHuman
        }

        #endregion Fake IHuman
    }
}
