// WorldMapRuntimeTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Tests for the <see cref="WorldMap.AddLocation"/> and <see cref="WorldMap.AddConnection"/>
    /// runtime mutation methods.
    /// </summary>
    [TestClass]
    public class WorldMapRuntimeTests
    {
        #region Helpers

        private static WorldMap CreateEmptyMap()
            => new WorldMap(
                new Dictionary<string, LocationDescriptor>(),
                new Dictionary<string, IReadOnlyList<WorldConnection>>(),
                new Dictionary<string, IReadOnlyList<string>>());

        /// <summary>
        /// Creates a minimal <see cref="LocationDescriptor"/> for testing.
        /// No Region is embedded — pass region separately to <see cref="WorldMap.AddLocation"/>.
        /// </summary>
        private static LocationDescriptor MakeLocation(
            string id,
            LocationType type = LocationType.Public)
            => new LocationDescriptor(
                Id: id,
                DisplayName: id,
                BaseNoise: 0.1,
                NoisePerPerson: 0.01,
                Capacity: 10,
                AllowsPrivacy: false,
                Type: type);

        #endregion Helpers

        // ── AddLocation ──────────────────────────────────────────────────────

        [TestMethod]
        public void AddLocation_NewId_GetLocationReturnsDescriptor()
        {
            var map = CreateEmptyMap();
            var loc = MakeLocation("test_room");

            map.AddLocation(loc);

            var result = map.GetLocation("test_room");
            Assert.IsNotNull(result);
            Assert.AreEqual("test_room", result!.Id);
        }

        [TestMethod]
        public void AddLocation_NewId_AppearsInLocationsProperty()
        {
            var map = CreateEmptyMap();
            map.AddLocation(MakeLocation("hall"));

            Assert.IsTrue(map.Locations.ContainsKey("hall"));
        }

        [TestMethod]
        public void AddLocation_SameIdTwice_ThrowsInvalidOperationException()
        {
            var map = CreateEmptyMap();
            var loc = MakeLocation("duplicate");
            map.AddLocation(loc);

            Assert.ThrowsException<InvalidOperationException>(() => map.AddLocation(loc));
        }

        [TestMethod]
        public void AddLocation_WithRegion_AppearsInGetLocationsInRegion()
        {
            var map = CreateEmptyMap();

            map.AddLocation(MakeLocation("forest_cave"), region: "Wilds");

            var inRegion = map.GetLocationsInRegion("Wilds");
            CollectionAssert.Contains(inRegion.ToList(), "forest_cave");
        }

        [TestMethod]
        public void AddLocation_TwoLocationsInSameRegion_BothAppear()
        {
            var map = CreateEmptyMap();

            map.AddLocation(MakeLocation("cave_a"), region: "Underground");
            map.AddLocation(MakeLocation("cave_b"), region: "Underground");

            var inRegion = map.GetLocationsInRegion("Underground");
            Assert.AreEqual(2, inRegion.Count);
            CollectionAssert.Contains(inRegion.ToList(), "cave_a");
            CollectionAssert.Contains(inRegion.ToList(), "cave_b");
        }

        [TestMethod]
        public void AddLocation_NoRegion_GetLocationsInRegionReturnsEmpty()
        {
            var map = CreateEmptyMap();
            map.AddLocation(MakeLocation("isolated"));

            var inRegion = map.GetLocationsInRegion("Nowhere");
            Assert.AreEqual(0, inRegion.Count);
        }

        [TestMethod]
        public void AddLocation_RegistersWithLocationService()
        {
            var map = CreateEmptyMap();
            var service = new DefaultLocationService();
            var loc = MakeLocation("shop", LocationType.Work);

            map.AddLocation(loc, locationService: service);

            var descriptor = service.GetDescriptor("shop");
            Assert.IsNotNull(descriptor);
            Assert.AreEqual("shop", descriptor!.Id);
        }

        // ── AddConnection ────────────────────────────────────────────────────

        [TestMethod]
        public void AddConnection_BothLocationsExist_ConnectionReturned()
        {
            var map = CreateEmptyMap();
            map.AddLocation(MakeLocation("room_a"));
            map.AddLocation(MakeLocation("room_b"));

            map.AddConnection("room_a", "room_b", 50.0);

            var connections = map.GetConnections("room_a");
            Assert.AreEqual(1, connections.Count);
            Assert.AreEqual("room_b", connections[0].TargetLocationId);
            Assert.AreEqual(50.0, connections[0].DistanceMeters, 0.001);
        }

        [TestMethod]
        public void AddConnection_Bidirectional_BothDirectionsWork()
        {
            var map = CreateEmptyMap();
            map.AddLocation(MakeLocation("hall"));
            map.AddLocation(MakeLocation("garden"));
            map.AddConnection("hall", "garden", 40.0);
            map.AddConnection("garden", "hall", 40.0);

            Assert.AreEqual(1, map.GetConnections("hall").Count);
            Assert.AreEqual(1, map.GetConnections("garden").Count);
            Assert.AreEqual("garden", map.GetConnections("hall")[0].TargetLocationId);
            Assert.AreEqual("hall", map.GetConnections("garden")[0].TargetLocationId);
        }

        [TestMethod]
        public void AddConnection_UnknownFromId_ThrowsInvalidOperationException()
        {
            var map = CreateEmptyMap();
            map.AddLocation(MakeLocation("known"));

            Assert.ThrowsException<InvalidOperationException>(
                () => map.AddConnection("ghost", "known", 10.0));
        }

        [TestMethod]
        public void AddConnection_UnknownToId_ThrowsInvalidOperationException()
        {
            var map = CreateEmptyMap();
            map.AddLocation(MakeLocation("known"));

            Assert.ThrowsException<InvalidOperationException>(
                () => map.AddConnection("known", "ghost", 10.0));
        }

        [TestMethod]
        public void AddConnection_MultipleConnections_GetNeighborsOrderedByDistance()
        {
            var map = CreateEmptyMap();
            map.AddLocation(MakeLocation("start"));
            map.AddLocation(MakeLocation("near"));
            map.AddLocation(MakeLocation("far"));
            map.AddConnection("start", "far", 200.0);
            map.AddConnection("start", "near", 30.0);

            var neighbors = map.GetNeighbors("start").ToList();
            Assert.AreEqual(2, neighbors.Count);
            Assert.AreEqual("near", neighbors[0]);
            Assert.AreEqual("far", neighbors[1]);
        }

        [TestMethod]
        public void AddConnection_LocationWithNoConnections_GetConnectionsReturnsEmpty()
        {
            var map = CreateEmptyMap();
            map.AddLocation(MakeLocation("isolated"));

            var connections = map.GetConnections("isolated");
            Assert.AreEqual(0, connections.Count);
        }

        // ── Full round-trip ──────────────────────────────────────────────────

        [TestMethod]
        public void AddLocation_ThenConnection_FullRoundTrip()
        {
            var map = CreateEmptyMap();
            map.AddLocation(MakeLocation("base"));
            map.AddLocation(MakeLocation("secret_cave"));
            map.AddConnection("base", "secret_cave", 150.0);

            var neighbors = map.GetNeighbors("base").ToList();
            CollectionAssert.Contains(neighbors, "secret_cave");
            Assert.IsNotNull(map.GetLocation("secret_cave"));
        }

        [TestMethod]
        public void AddLocation_GetRegionOf_ReturnsCorrectRegion()
        {
            var map = CreateEmptyMap();
            map.AddLocation(MakeLocation("mine"), region: "Depths");

            Assert.AreEqual("Depths", map.GetRegionOf("mine"));
        }

        [TestMethod]
        public void AllRegions_AfterAddingLocationsWithRegions_ContainsNewRegions()
        {
            var map = CreateEmptyMap();
            map.AddLocation(MakeLocation("loc_a"), region: "Alpha");
            map.AddLocation(MakeLocation("loc_b"), region: "Beta");

            CollectionAssert.Contains(map.AllRegions.ToList(), "Alpha");
            CollectionAssert.Contains(map.AllRegions.ToList(), "Beta");
        }
    }
}
