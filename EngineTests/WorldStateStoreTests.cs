// WorldStateStoreTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Economy;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Reputation;
    using GameEngineTools.Characters.Engines.Status;
    using GameEngineTools.World.Economy;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Persistence;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.IO;
    using System.Linq;

    /// <summary>Round trips of the shared world state: prices, reputations, standing and placements.</summary>
    [TestClass]
    public sealed class WorldStateStoreTests : TestBase
    {
        private static readonly WDateTime Now = new(1_000_000);

        [TestMethod]
        public void EconomyLedger_RoundTrip_KeepsPricesAndBases_SoPricingContinuesWhereItStopped()
        {
            var cfg = EconomyConfig.Default;
            var ledger = new EconomyLedger();
            ledger.AdjustPriceForStockChange("bakery", PickupItemKind.Bread, 3, cfg, observedPrice: 2.0, observedStock: 8);
            var expected = ledger.AdjustPriceForStockChange("bakery", PickupItemKind.Bread, 2, cfg);

            var restored = new EconomyLedger();
            restored.Restore(ledger.Snapshot());

            Assert.AreEqual(expected, restored.GetPrice("bakery", PickupItemKind.Bread), 1e-12);
            Assert.AreEqual(
                ledger.AdjustPriceForStockChange("bakery", PickupItemKind.Bread, 6, cfg),
                restored.AdjustPriceForStockChange("bakery", PickupItemKind.Bread, 6, cfg), 1e-12,
                "The learned base must survive, otherwise the next repricing starts from the seed price.");
        }

        [TestMethod]
        public void ReputationLedger_RoundTrip_KeepsEveryReputation()
        {
            var ledger = new CommunityReputationLedger();
            var subject = new HumanId(Guid.NewGuid());
            for (var i = 0; i < 10; i++)
                ledger.Observe(subject, "square", ThirdPartyObservationType.PositiveAct, Now);

            var restored = new CommunityReputationLedger();
            restored.Restore(ledger.Snapshot());

            Assert.AreEqual(ledger.Get(subject, "square")!.Score, restored.Get(subject, "square")!.Score, 1e-12);
            Assert.AreEqual(ledger.InitialTrustPrior(subject, "square"), restored.InitialTrustPrior(subject, "square"), 1e-12);
        }

        [TestMethod]
        public void StatusLedger_RoundTrip_KeepsStandingAndStability()
        {
            var restored = new StatusLedger();
            var state = new StatusLedgerState(
                new[] { new StatusEntry(new HumanId(Guid.NewGuid()), 0.7, 0.2, 0.5) }, 0.42);

            restored.Restore(state);
            var again = restored.Snapshot();

            Assert.AreEqual(0.42, restored.HierarchyStability(), 1e-12);
            Assert.AreEqual(state.Agents[0], again.Agents.Single());
        }

        [TestMethod]
        public void WholeState_SurvivesJsonAndAFile_AndAnUnknownVersionIsRejected()
        {
            var locations = new DefaultLocationService();
            locations.RegisterLocation(new LocationDescriptor("room", "Room", 0.05, 0.01, 50, false, LocationType.Social));
            var id = new HumanId(Guid.NewGuid());
            locations.MoveCharacter(id, "room");
            var economy = new EconomyLedger();
            economy.AdjustPriceForStockChange("bakery", PickupItemKind.Bread, 4, EconomyConfig.Default);

            var saved = WorldStateStore.Capture(Now, economy, new CommunityReputationLedger(), new StatusLedger(), locations, orchestrator: null);
            var path = Path.Combine(Path.GetTempPath(), $"world_{Guid.NewGuid():N}.json");
            try
            {
                WorldStateStore.Save(path, saved);
                Assert.IsFalse(File.Exists(path + ".tmp"), "The temp file is renamed away.");
                var loaded = WorldStateStore.Load(path);

                var locations2 = new DefaultLocationService();
                var economy2 = new EconomyLedger();
                WorldStateStore.Apply(loaded, economy2, null, null, locations2, null);

                Assert.AreEqual("room", locations2.GetLocation(id));
                Assert.AreEqual(economy.GetPrice("bakery", PickupItemKind.Bread), economy2.GetPrice("bakery", PickupItemKind.Bread), 1e-12);
                Assert.AreEqual(Now, loaded.SavedAt);

                var future = WorldStateStore.ToJson(saved with { SchemaVersion = 99 });
                Assert.Throws<InvalidDataException>(() => WorldStateStore.FromJson(future));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
