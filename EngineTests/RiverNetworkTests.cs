// RiverNetworkTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.World.Data;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>Unit tests for <see cref="RiverReach"/> polyline byte packing and
    /// <see cref="SqliteWorldDatabase.SaveRiverNetwork"/>/<see cref="SqliteWorldDatabase.LoadRiverNetwork"/>.</summary>
    [TestClass]
    public class RiverNetworkTests
    {
        private static void SeedSchema(SqliteWorldDatabase db)
        {
            var schemaSql = SqlScriptLoader.Load("terrain_schema.sql");
            db.ExecuteScript(schemaSql);
        }

        private static IReadOnlyList<(double X, double Y)> MakePolyline() =>
            [(0.0, 0.0), (10.5, 3.25), (20.0, -1.0)];

        [TestMethod]
        public void PolylineToBytes_ThenFromBytes_RoundTripsExactly()
        {
            var polyline = MakePolyline();

            var bytes = RiverReach.PolylineToBytes(polyline);
            var restored = RiverReach.PolylineFromBytes(bytes);

            CollectionAssert.AreEqual((System.Collections.ICollection)polyline, (System.Collections.ICollection)restored);
        }

        [TestMethod]
        public void PolylineFromBytes_WrongLength_Throws()
        {
            Assert.Throws<ArgumentException>(() => RiverReach.PolylineFromBytes([1, 2, 3]));
        }

        private static RiverNetwork MakeNetwork(string networkId) => new(
            NetworkId: networkId,
            Nodes:
            [
                new RiverNode($"{networkId}_n1", networkId, 0.0, 0.0, RiverNodeKind.Source),
                new RiverNode($"{networkId}_n2", networkId, 20.0, -1.0, RiverNodeKind.Mouth)
            ],
            Reaches:
            [
                new RiverReach($"{networkId}_r1", networkId, $"{networkId}_n1", $"{networkId}_n2", MakePolyline(), StrahlerOrder: 1, ShreveMagnitude: 1, WidthMeters: 2.5)
            ],
            Oxbows:
            [
                new OxbowLoop($"{networkId}_o1", networkId, [(5.0, 5.0), (6.0, 6.0), (5.0, 6.0)])
            ]);

        [TestMethod]
        public void SaveRiverNetwork_ThenLoad_RoundTripsNodesReachesAndOxbows()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            var network = MakeNetwork("chunk_0_0");
            db.SaveRiverNetwork(network);

            var loaded = db.LoadRiverNetwork("chunk_0_0");

            Assert.AreEqual(2, loaded.Nodes.Count);
            Assert.AreEqual(1, loaded.Reaches.Count);
            Assert.AreEqual(1, loaded.Oxbows.Count);
            Assert.AreEqual(RiverNodeKind.Source, loaded.Nodes[0].Kind);
            Assert.AreEqual(RiverNodeKind.Mouth, loaded.Nodes[1].Kind);
            CollectionAssert.AreEqual((System.Collections.ICollection)network.Reaches[0].Polyline, (System.Collections.ICollection)loaded.Reaches[0].Polyline);
            Assert.AreEqual(network.Reaches[0].WidthMeters, loaded.Reaches[0].WidthMeters);
        }

        [TestMethod]
        public void LoadRiverNetwork_UnknownNetworkId_ReturnsEmptyNetwork()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            var loaded = db.LoadRiverNetwork("does_not_exist");

            Assert.AreEqual(0, loaded.Nodes.Count);
            Assert.AreEqual(0, loaded.Reaches.Count);
            Assert.AreEqual(0, loaded.Oxbows.Count);
        }

        [TestMethod]
        public void SaveRiverNetwork_CalledTwice_ReplacesRatherThanAppending()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            db.SaveRiverNetwork(MakeNetwork("chunk_0_0"));
            db.SaveRiverNetwork(MakeNetwork("chunk_0_0"));

            var loaded = db.LoadRiverNetwork("chunk_0_0");

            Assert.AreEqual(2, loaded.Nodes.Count);
            Assert.AreEqual(1, loaded.Reaches.Count);
            Assert.AreEqual(1, loaded.Oxbows.Count);
        }

        [TestMethod]
        public void SaveRiverNetwork_TwoDifferentNetworkIds_DoNotInterfere()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            db.SaveRiverNetwork(MakeNetwork("chunk_a"));
            db.SaveRiverNetwork(MakeNetwork("chunk_b"));

            var loadedA = db.LoadRiverNetwork("chunk_a");
            var loadedB = db.LoadRiverNetwork("chunk_b");

            Assert.AreEqual(2, loadedA.Nodes.Count);
            Assert.AreEqual(2, loadedB.Nodes.Count);
        }
    }
}
