// FoodEconomyTier1Tests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Behavior.Needs;
    using GameEngineTools.Characters.Engines.Objects;
    using GameEngineTools.Characters.Engines.Schedule;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Objects.Production;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System.Collections.Generic;
    using System.Linq;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Food-economy Tier 1: production, recipe processing, spoilage, provisioning-bridge ranking,
    /// and the pantry/production gates.
    /// </summary>
    [TestClass]
    public sealed class FoodEconomyTier1Tests : TestBase
    {
        private const string Loc = "test"; // BehaviorComponentTestFactory surface location

        // ── §2 ProductionService ────────────────────────────────────────────────────

        [TestMethod]
        public void Produce_RawItem_AddsFreshItemToHand()
        {
            var provider = new MemProvider();
            var svc = new ProductionService(provider);
            var ctx = Ctx(out var self);

            var produced = svc.Produce(ctx, Loc, PickupItemKind.Grain, new WDateTime(0));

            Assert.AreEqual(PickupItemKind.Grain, produced.ItemKind);
            Assert.AreEqual(self, produced.HeldBy);
            Assert.IsNotNull(produced.ProducedAt, "Produced item must carry a ProducedAt for spoilage.");
            CollectionAssert.Contains(provider.GetHeldBy(self).ToList(), produced);
        }

        [TestMethod]
        public void Process_WithEnoughInputs_ConsumesInputsAndYieldsOutput()
        {
            var provider = new MemProvider();
            var svc = new ProductionService(provider);
            var ctx = Ctx(out var self);
            provider.AddObject(FoodItemCatalog.Create(PickupItemKind.Grain, Loc, new WDateTime(0), self));
            provider.AddObject(FoodItemCatalog.Create(PickupItemKind.Grain, Loc, new WDateTime(0), self));

            var flour = svc.Process(ctx, Loc, PickupItemKind.Flour, new WDateTime(0));

            Assert.IsNotNull(flour);
            Assert.AreEqual(PickupItemKind.Flour, flour!.ItemKind);
            Assert.AreEqual(self, flour.HeldBy);
            Assert.AreEqual(0, provider.GetHeldBy(self).Count(o => o.ItemKind == PickupItemKind.Grain),
                "Both grain inputs must be consumed by the recipe.");
        }

        [TestMethod]
        public void Process_InsufficientInputs_ReturnsNullAndConsumesNothing()
        {
            var provider = new MemProvider();
            var svc = new ProductionService(provider);
            var ctx = Ctx(out var self);
            // Recipe needs 2 grain; only 1 held.
            provider.AddObject(FoodItemCatalog.Create(PickupItemKind.Grain, Loc, new WDateTime(0), self));

            var flour = svc.Process(ctx, Loc, PickupItemKind.Flour, new WDateTime(0));

            Assert.IsNull(flour, "Processing must fail when inputs are insufficient.");
            Assert.AreEqual(1, provider.GetHeldBy(self).Count(o => o.ItemKind == PickupItemKind.Grain),
                "All-or-nothing: the single grain must not be consumed on a failed recipe.");
        }

        // ── §1 Spoilage ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void Spoilage_FreshItem_FreshnessOne_ThenDecays()
        {
            var bread = FoodItemCatalog.Create(PickupItemKind.Bread, Loc, new WDateTime(0), null);
            var cfg = SpoilageConfig.Default;

            Assert.AreEqual(1.0, Spoilage.Freshness(bread, new WDateTime(0), cfg), 1e-9);
            // Bread rate = 1/72 per hour → after 36h freshness ≈ 0.5.
            var half = Spoilage.Freshness(bread, new WDateTime(0).AddHours(36), cfg);
            Assert.AreEqual(0.5, half, 0.02);
        }

        [TestMethod]
        public void Spoilage_OldPerishable_IsSpoiled()
        {
            var bread = FoodItemCatalog.Create(PickupItemKind.Bread, Loc, new WDateTime(0), null);
            var cfg = SpoilageConfig.Default;

            // Bread lasts ~72h; 200h later it is fully spoiled.
            Assert.IsTrue(Spoilage.IsSpoiled(bread, new WDateTime(0).AddHours(200), cfg));
        }

        // ── §4 Provisioning bridge ──────────────────────────────────────────────────

        [TestMethod]
        public void Bridge_HoldsFreshFood_EmitsEatStored()
        {
            var engine = new ContingencySearchEngine();
            var held = new List<WorldObject> { FoodItemCatalog.Create(PickupItemKind.Bread, Loc, new WDateTime(0), null) };
            var ctx = BridgeContext(hunger: 80, available: new(), held: held, now: new WDateTime(0));

            var output = engine.Evaluate(ctx);

            Assert.IsTrue(output.Candidates.Any(c => c.Name == EatStored),
                "Holding fresh food while hungry must offer EatStored.");
        }

        [TestMethod]
        public void Bridge_AtProcessingSiteWithInputs_EmitsProcess()
        {
            var engine = new ContingencySearchEngine();
            var mill = ProductionSiteFactory.Create("mill", Loc, PickupItemKind.Flour, "Mlýn");
            var held = new List<WorldObject>
            {
                FoodItemCatalog.Create(PickupItemKind.Grain, Loc, new WDateTime(0), null),
                FoodItemCatalog.Create(PickupItemKind.Grain, Loc, new WDateTime(0), null),
            };
            var ctx = BridgeContext(hunger: 80, available: new() { mill }, held: held, now: new WDateTime(0));

            var output = engine.Evaluate(ctx);

            Assert.IsTrue(output.Candidates.Any(c => c.Name == Process),
                "At a mill with two grain in hand, Process must be offered.");
        }

        [TestMethod]
        public void Bridge_AtRawSite_EmitsProduce()
        {
            var engine = new ContingencySearchEngine();
            var field = ProductionSiteFactory.Create("field", Loc, PickupItemKind.Grain, "Pole");
            var ctx = BridgeContext(hunger: 80, available: new() { field }, held: new(), now: new WDateTime(0));

            var output = engine.Evaluate(ctx);

            Assert.IsTrue(output.Candidates.Any(c => c.Name == Produce),
                "At a raw-harvest field while hungry, Produce must be offered.");
        }

        // ── §3 Gating ───────────────────────────────────────────────────────────────

        [TestMethod]
        public void Gate_RemovesEatStoredAndProduce_WhenNoPantryOrSite()
        {
            var gate = new ObjectAffordanceGatingEngine();
            var candidates = new List<BehaviorCandidate>
            {
                Cand(EatStored),
                Cand(Produce),
                Cand(Process),
                Cand(Idle),
            };
            // Empty hands, no production sites present.
            var ctx = BridgeContext(hunger: 80, available: new(), held: new(), now: new WDateTime(0));

            gate.Modify(ctx, candidates);

            Assert.IsFalse(candidates.Any(c => c.Name == EatStored), "EatStored gated out with empty hands.");
            Assert.IsFalse(candidates.Any(c => c.Name == Produce), "Produce gated out with no raw site.");
            Assert.IsFalse(candidates.Any(c => c.Name == Process), "Process gated out with no processing site.");
            Assert.IsTrue(candidates.Any(c => c.Name == Idle), "Unrelated candidates must survive.");
        }

        [TestMethod]
        public void Gate_KeepsProduce_WhenRawSitePresent()
        {
            var gate = new ObjectAffordanceGatingEngine();
            var field = ProductionSiteFactory.Create("field", Loc, PickupItemKind.Grain, "Pole");
            var candidates = new List<BehaviorCandidate> { Cand(Produce), Cand(Process) };
            var ctx = BridgeContext(hunger: 80, available: new() { field }, held: new(), now: new WDateTime(0));

            gate.Modify(ctx, candidates);

            Assert.IsTrue(candidates.Any(c => c.Name == Produce), "Produce must survive when a raw site is co-located.");
            Assert.IsFalse(candidates.Any(c => c.Name == Process),
                "Process must still be gated out — a raw site has no recipe.");
        }

        // ── Supply chain: fixtures, edibility, shifts ───────────────────────────────

        [TestMethod]
        public void Process_DoesNotConsumeAProductionSiteFixtureAsAnIngredient()
        {
            var provider = new MemProvider();
            var svc = new ProductionService(provider);
            var ctx = Ctx(out _);
            provider.AddObject(ProductionSiteFactory.Create("mill", Loc, PickupItemKind.Flour, "Mlýn"));

            var bread = svc.Process(ctx, Loc, PickupItemKind.Bread, new WDateTime(0));

            Assert.IsNull(bread, "The mill itself (ItemKind Flour) is not flour to bake with.");
            Assert.IsTrue(provider.GetObjectsAt(Loc).Any(o => o.Id == "mill"), "The mill fixture must survive.");
        }

        [TestMethod]
        public void Bridge_RawIngredientsAreNotEatable()
        {
            var engine = new ContingencySearchEngine();
            var held = new List<WorldObject>
            {
                FoodItemCatalog.Create(PickupItemKind.Grain, Loc, new WDateTime(0), null),
                FoodItemCatalog.Create(PickupItemKind.Flour, Loc, new WDateTime(0), null),
            };

            var output = engine.Evaluate(BridgeContext(hunger: 80, available: new(), held: held, now: new WDateTime(0)));

            Assert.IsFalse(output.Candidates.Any(c => c.Name == EatStored), "Grain and flour only have an ownership affordance.");
        }

        [TestMethod]
        public void Bridge_HungryAtProductionSite_ProducesInsteadOfForagingAway()
        {
            var engine = new ContingencySearchEngine();
            var field = ProductionSiteFactory.Create("field", Loc, PickupItemKind.Grain, "Pole");

            var output = engine.Evaluate(BridgeContext(hunger: 80, available: new() { field }, held: new(), now: new WDateTime(0)));

            Assert.IsTrue(output.Candidates.Any(c => c.Name == Produce));
            Assert.IsFalse(output.Candidates.Any(c => c.Name == MoveToFood), "A local way to get a meal beats walking off to forage.");
        }

        [TestMethod]
        public void Bridge_HungryWithGrainAtMill_ProcessesBeforeHarvestingMore()
        {
            var engine = new ContingencySearchEngine();
            var held = new List<WorldObject>
            {
                FoodItemCatalog.Create(PickupItemKind.Grain, Loc, new WDateTime(0), null),
                FoodItemCatalog.Create(PickupItemKind.Grain, Loc, new WDateTime(0), null),
            };
            var available = new List<WorldObject>
            {
                ProductionSiteFactory.Create("field", Loc, PickupItemKind.Grain, "Pole"),
                ProductionSiteFactory.Create("mill", Loc, PickupItemKind.Flour, "Mlýn"),
            };

            var output = engine.Evaluate(BridgeContext(hunger: 80, available: available, held: held, now: new WDateTime(0)));

            Assert.IsTrue(output.Candidates.Any(c => c.Name == Process));
            Assert.IsFalse(output.Candidates.Any(c => c.Name == Produce), "Harvest only when there is nothing to process.");
        }

        [TestMethod]
        public void Bridge_FarmerOnWorkShift_ProducesWithoutBeingHungry_ButAHealerDoesNot()
        {
            var engine = new ContingencySearchEngine();
            var field = ProductionSiteFactory.Create("field", Loc, PickupItemKind.Grain, "Pole");

            Assert.IsTrue(
                engine.Evaluate(ShiftContext(OccupationIds.Farmer, field)).Candidates.Any(c => c.Name == Produce),
                "A farmer on shift works the field even when fed.");
            Assert.IsFalse(
                engine.Evaluate(ShiftContext(OccupationIds.Healer, field)).Candidates.Any(c => c.Name == Produce),
                "Only food-producing occupations produce on shift.");
        }

        [TestMethod]
        public void Bridge_ProducerOnShiftHoldingBread_SellsToTheCounterEvenWhenTheShelfIsEmpty()
        {
            var engine = new ContingencySearchEngine();
            var counter = ShopCounterFactory.Create("counter", Loc, "bakery_shop", PickupItemKind.Bread, "Pult");
            var bread = FoodItemCatalog.Create(PickupItemKind.Bread, Loc, new WDateTime(0), null);

            var output = engine.Evaluate(ShiftContext(OccupationIds.Farmer, counter, held: new() { bread }));

            Assert.IsTrue(output.Candidates.Any(c => c.Name == Sell && c.Utility > 40.0),
                "The shop keeps trading bread after the last loaf is sold, and a working producer takes the sale.");
        }

        private static BehaviorContext ShiftContext(string occupation, WorldObject fixture, List<WorldObject>? held = null)
        {
            var ctx = BehaviorComponentTestFactory.Context(hunger: 10, thirst: 5, now: new WDateTime(0));
            var slot = new ScheduleSlot("work", 5, Work);
            ((HumanContext)ctx.HumanContext).Snapshot = ctx.HumanContext.Snapshot with
            {
                Schedule = new DailyScheduleState(new[] { slot }, slot, 0, occupation),
            };
            return ctx with { AvailableObjects = new List<WorldObject> { fixture }, HeldObjects = held ?? new List<WorldObject>() };
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────

        private static IHumanContext Ctx(out HumanId self)
        {
            self = new HumanId(System.Guid.NewGuid());
            return BehaviorComponentTestFactory.Context(hunger: 50, thirst: 10, selfId: self).HumanContext;
        }

        private static BehaviorContext BridgeContext(
            double hunger, List<WorldObject> available, List<WorldObject> held, WDateTime now)
        {
            var ctx = BehaviorComponentTestFactory.Context(hunger: hunger, thirst: 5, now: now);
            return ctx with { AvailableObjects = available, HeldObjects = held };
        }

        private static BehaviorCandidate Cand(string name)
            => new(name, 50.0, WTimeSpan.FromMinutes(10), BehaviorDomain.Physiological);

        /// <summary>Minimal in-memory mutable provider with real held/inventory semantics.</summary>
        private sealed class MemProvider : IMutableWorldObjectProvider
        {
            private readonly Dictionary<string, WorldObject> _o = new();

            public IEnumerable<WorldObject> GetObjectsAt(string loc)
                => _o.Values.Where(o => o.LocationId == loc && o.HeldBy is null && o.IsAvailable);

            public IEnumerable<WorldObject> GetAllObjectsAt(string loc) => _o.Values.Where(o => o.LocationId == loc);

            public IEnumerable<WorldObject> GetAllObjects() => _o.Values.ToList();

            public void AddObject(WorldObject o) => _o[o.Id] = o;

            public WorldObject? FindObject(string id) => _o.TryGetValue(id, out var v) ? v : null;

            public bool RemoveObject(string loc, string id) => _o.Remove(id);

            public bool ConsumeObject(string loc, string id, WDateTime now) => _o.Remove(id);

            public bool RestoreObject(string loc, string id) => false;

            public bool SetHeldBy(string loc, string id, HumanId? holder)
            {
                if (!_o.TryGetValue(id, out var v)) return false;
                _o[id] = v with { HeldBy = holder };
                return true;
            }

            public IEnumerable<WorldObject> GetHeldBy(HumanId holder)
                => _o.Values.Where(o => o.HeldBy is { } h && h.Equals(holder));

            public IEnumerable<string> GetKnownLocationIds() => _o.Values.Select(o => o.LocationId).Distinct();
        }
    }
}
