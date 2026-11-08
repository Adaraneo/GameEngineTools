// FoodEconomyTier2Tests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Behavior.Needs;
    using GameEngineTools.Characters.Engines.Economy;
    using GameEngineTools.Characters.Engines.Objects;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Economy;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Objects.Crops;
    using GameEngineTools.World.Objects.Production;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging.Abstractions;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Food-economy Tier 2: wealth transitions, posted-price ledger, Buy/Sell gating + commit,
    /// and the provisioning-bridge Buy branch. See <c>food-economy-tier2-implementation-plan.md</c> §F.
    /// </summary>
    [TestClass]
    public sealed class FoodEconomyTier2Tests : TestBase
    {
        private const string Loc = "test"; // BehaviorComponentTestFactory surface location

        // ── §4 DefaultEconomyEngine — pure wealth transitions ────────────────────────

        [TestMethod]
        public void EconomyEngine_ApplyWage_IncreasesWealth()
        {
            var engine = MakeEconomyEngine();

            var result = engine.ApplyWage(new EconomyState(5.0), wagePerHour: 2.0, hoursWorked: 3.0);

            Assert.AreEqual(11.0, result.Wealth, 1e-9, "Wage adds wagePerHour × hoursWorked to wealth.");
        }

        [TestMethod]
        public void EconomyEngine_ApplyPurchase_InsufficientWealth_Throws()
        {
            var engine = MakeEconomyEngine();

            // Commit-side gating-bug guard: committing a purchase you cannot afford must throw loudly.
            Assert.Throws<InvalidOperationException>(
                () => engine.ApplyPurchase(new EconomyState(1.0), price: 5.0));
        }

        [TestMethod]
        public void EconomyEngine_ApplySale_CreditsWealth()
        {
            var engine = MakeEconomyEngine();

            var result = engine.ApplySale(new EconomyState(10.0), price: 3.5);

            Assert.AreEqual(13.5, result.Wealth, 1e-9);
        }

        [TestMethod]
        public void EconomyEngine_HandlePurchased_DeductsWealth()
        {
            var engine = MakeEconomyEngine();
            engine.RestoreState(new EconomyState(10.0));
            var self = new HumanId(Guid.NewGuid());
            var ctx = BehaviorComponentTestFactory.Context(selfId: self).HumanContext;

            engine.Handle(
                new Purchased(new WDateTime(0), self, "bread_1", PickupItemKind.Bread, "bakery", 4.0, 6.0),
                ctx, new EventCollector());

            Assert.AreEqual(6.0, engine.State.Wealth, 1e-9, "Purchased event deducts its price from wealth.");
        }

        // ── §3 EconomyLedger — posted-price stock feedback ───────────────────────────

        [TestMethod]
        public void EconomyLedger_PriceRises_WhenStockBelowTarget()
        {
            var ledger = new EconomyLedger();
            var cfg = EconomyConfig.Default; // TargetStockLevel = 8
            var before = ledger.GetPrice("bakery", PickupItemKind.Bread);

            var after = ledger.AdjustPriceForStockChange("bakery", PickupItemKind.Bread, newStockCount: 2, cfg);

            Assert.IsTrue(after > before, "Stock below target (scarcity) must raise the price.");
        }

        [TestMethod]
        public void EconomyLedger_PriceFalls_WhenStockAboveTarget()
        {
            var ledger = new EconomyLedger();
            var cfg = EconomyConfig.Default; // TargetStockLevel = 8
            var before = ledger.GetPrice("bakery", PickupItemKind.Bread);

            var after = ledger.AdjustPriceForStockChange("bakery", PickupItemKind.Bread, newStockCount: 16, cfg);

            Assert.IsTrue(after < before, "Stock above target (glut) must lower the price.");
        }

        [TestMethod]
        public void EconomyLedger_ConstantStock_DoesNotRatchetPriceToARail()
        {
            var ledger = new EconomyLedger();
            var cfg = EconomyConfig.Default;

            var first = ledger.AdjustPriceForStockChange("bakery", PickupItemKind.Bread, newStockCount: 2, cfg);
            var last = first;
            for (var i = 0; i < 50; i++)
                last = ledger.AdjustPriceForStockChange("bakery", PickupItemKind.Bread, newStockCount: 2, cfg);

            Assert.AreEqual(first, last, 1e-9, "The same stock must always yield the same price.");
            Assert.IsTrue(last < cfg.MaxPrice, "A moderate shortage must not drive the price to the ceiling.");
        }

        [TestMethod]
        public void EconomyLedger_PriceReturnsToBase_WhenStockReturnsToTarget()
        {
            var ledger = new EconomyLedger();
            var cfg = EconomyConfig.Default;
            var atTarget = ledger.AdjustPriceForStockChange("bakery", PickupItemKind.Bread, cfg.TargetStockLevel, cfg, observedPrice: 3.0, observedStock: cfg.TargetStockLevel);

            ledger.AdjustPriceForStockChange("bakery", PickupItemKind.Bread, 1, cfg);
            var back = ledger.AdjustPriceForStockChange("bakery", PickupItemKind.Bread, cfg.TargetStockLevel, cfg);

            Assert.AreEqual(3.0, atTarget, 1e-9);
            Assert.AreEqual(3.0, back, 1e-9, "Price depends on stock only, so restoring the stock restores the price.");
        }

        [TestMethod]
        public void EconomyLedger_ColdLedger_DerivesBaseFromObservedPriceAndStock()
        {
            var ledger = new EconomyLedger();
            var cfg = EconomyConfig.Default;

            // A persisted shop posted 6.0 at stock 4 (below target); at target stock it must therefore ask less than 6.0.
            var atTarget = ledger.AdjustPriceForStockChange("bakery", PickupItemKind.Bread, cfg.TargetStockLevel, cfg, observedPrice: 6.0, observedStock: 4);

            Assert.AreEqual(6.0 / Math.Exp(cfg.PriceElasticityPerUnit * (cfg.TargetStockLevel - 4)), atTarget, 1e-9);
        }

        [TestMethod]
        public void Buy_WritesTheNewPostedPriceBackToTheRemainingStock()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(PricedFood("bread_1", price: 2.0, shop: "bakery"));
            provider.AddObject(PricedFood("bread_2", price: 2.0, shop: "bakery"));
            provider.AddObject(PricedFood("bread_3", price: 2.0, shop: "bakery"));
            var engine = MakeObjectEngine(provider, lod: null);
            var outbox = new EventCollector();

            engine.Handle(new ActionCommitted(new WDateTime(0), self, Buy, WTimeSpan.FromMinutes(15)), BuyerContext(self, 30.0), outbox);

            var changed = outbox.Drain().OfType<PriceChanged>().Single();
            var remaining = provider.GetObjectsAt(Loc).Where(o => o.ShopId == "bakery").ToList();
            Assert.AreEqual(2, remaining.Count);
            Assert.IsTrue(changed.NewPrice > 2.0, "Fewer loaves left than the target stock: the ask rises.");
            Assert.IsTrue(remaining.All(o => Math.Abs(o.Price!.Value - changed.NewPrice) < 1e-9),
                "The posted price on the remaining objects must equal the ledger price, so the next Buy charges it.");
        }

        [TestMethod]
        public void BuyThenSell_BackToTheSameShop_LosesMoney()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(PricedFood("bread_1", price: 2.0, shop: "bakery"));
            provider.AddObject(PricedFood("bread_2", price: 2.0, shop: "bakery"));
            var engine = MakeObjectEngine(provider, lod: null);
            var outbox = new EventCollector();

            engine.Handle(new ActionCommitted(new WDateTime(0), self, Buy, WTimeSpan.FromMinutes(15)), BuyerContext(self, 30.0), outbox);
            engine.Handle(new ActionCommitted(new WDateTime(0), self, Sell, WTimeSpan.FromMinutes(15)), BuyerContext(self, 30.0), outbox);

            var events = outbox.Drain();
            var paid = events.OfType<Purchased>().Single().Price;
            var received = events.OfType<Sold>().Single().Price;
            Assert.IsTrue(received < paid, $"A round trip must lose money (paid {paid:F2}, got back {received:F2}).");
        }

        [TestMethod]
        public void EconomyLedger_GetPrice_ColdCacheUsesPersistedFallback_NotSeedPrice()
        {
            var ledger = new EconomyLedger();

            var price = ledger.GetPrice("bakery", PickupItemKind.Bread, persistedFallback: 7.5);

            Assert.AreEqual(7.5, price, 1e-9,
                "On a cache miss, the caller's persisted price must win over the hardcoded SeedPrice constant.");
        }

        [TestMethod]
        public void Buy_ColdLedger_PriceChangedOldPrice_ReflectsPersistedPrice_NotSeedPrice()
        {
            // SeedPrice(Bread) == 2.0 — pick a persisted price that differs from it so a regression
            // (falling back to SeedPrice) is caught.
            const double persistedPrice = 6.0;
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(PricedFood("shop_bread", price: persistedPrice, shop: "bakery"));

            var ctx = BuyerContext(self, wealth: 30.0);
            // A brand-new EconomyLedger simulates a cold cache after a process restart.
            var engine = MakeObjectEngine(provider, lod: null);
            var outbox = new EventCollector();

            engine.Handle(new ActionCommitted(new WDateTime(0), self, Buy, WTimeSpan.FromMinutes(15)), ctx, outbox);

            var priceChanged = outbox.Drain().OfType<PriceChanged>().SingleOrDefault();
            Assert.IsNotNull(priceChanged, "A fully-simulated buy adjusts the shop's posted price.");
            Assert.AreEqual(persistedPrice, priceChanged!.OldPrice, 1e-9,
                "OldPrice must reflect the object's actual persisted price, not the hardcoded SeedPrice constant.");
        }

        [TestMethod]
        public void Sell_ColdLedger_PriceChangedOldPrice_ReflectsPersistedPrice_NotSeedPrice()
        {
            const double persistedPrice = 6.0;
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());

            var held = FoodItemCatalog.Create(PickupItemKind.Bread, Loc, new WDateTime(0), self);
            provider.AddObject(held);
            provider.AddObject(PricedFood("bakery_bread", price: persistedPrice, shop: "bakery"));

            var engine = MakeObjectEngine(provider);
            var ctx = BehaviorComponentTestFactory.Context(selfId: self).HumanContext;
            var outbox = new EventCollector();

            engine.Handle(
                new ActionCommitted(new WDateTime(0), self, Sell, WTimeSpan.FromMinutes(15)),
                ctx, outbox);

            var priceChanged = outbox.Drain().OfType<PriceChanged>().SingleOrDefault();
            Assert.IsNotNull(priceChanged, "A fully-simulated sell adjusts the shop's posted price.");
            Assert.AreEqual(persistedPrice, priceChanged!.OldPrice, 1e-9,
                "OldPrice must reflect the shop's actual persisted price, not the hardcoded SeedPrice constant.");
        }

        // ── §5.1 Buy/Sell gating ─────────────────────────────────────────────────────

        [TestMethod]
        public void Buy_Gate_FailsWithoutSufficientWealth()
        {
            var gate = new ObjectAffordanceGatingEngine();
            var priced = PricedFood("shop_bread", price: 5.0, shop: "bakery");
            var candidates = new List<BehaviorCandidate> { Cand(Buy), Cand(Idle) };
            var ctx = EconContext(available: new() { priced }, held: new(), wealth: 2.0);

            gate.Modify(ctx, candidates);

            Assert.IsFalse(candidates.Any(c => c.Name == Buy), "Buy gated out when the priced object is unaffordable.");
            Assert.IsTrue(candidates.Any(c => c.Name == Idle), "Unrelated candidates survive.");
        }

        [TestMethod]
        public void Buy_Gate_FailsForNonPricedObject()
        {
            var gate = new ObjectAffordanceGatingEngine();
            // A free (unpriced) bread — nothing to buy even with plenty of coin.
            var free = FoodItemCatalog.Create(PickupItemKind.Bread, Loc, new WDateTime(0), null);
            var candidates = new List<BehaviorCandidate> { Cand(Buy), Cand(Idle) };
            var ctx = EconContext(available: new() { free }, held: new(), wealth: 50.0);

            gate.Modify(ctx, candidates);

            Assert.IsFalse(candidates.Any(c => c.Name == Buy), "Buy gated out — no priced object is present.");
        }

        // ── §5.3 Sell commit — object transfer + wealth credit ───────────────────────

        [TestMethod]
        public void Sell_TransfersObjectAndCreditsWealth()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());

            // The seller holds a bread; a co-located bakery already stocks (and thus trades) bread.
            var held = FoodItemCatalog.Create(PickupItemKind.Bread, Loc, new WDateTime(0), self);
            provider.AddObject(held);
            provider.AddObject(PricedFood("bakery_bread", price: 2.0, shop: "bakery"));

            var engine = MakeObjectEngine(provider);
            var ctx = BehaviorComponentTestFactory.Context(selfId: self).HumanContext;
            var outbox = new EventCollector();

            engine.Handle(
                new ActionCommitted(new WDateTime(0), self, Sell, WTimeSpan.FromMinutes(15)),
                ctx, outbox);

            var sold = outbox.Drain().OfType<Sold>().SingleOrDefault();
            Assert.IsNotNull(sold, "A Sold event must be emitted.");
            Assert.AreEqual(PickupItemKind.Bread, sold!.ItemKind);
            Assert.AreEqual(2.0 * EconomyConfig.Default.ShopBuyBackFraction, sold.Price, 1e-9,
                "Sale credits the buy-back fraction of the shop's posted price.");

            Assert.AreEqual(0, provider.GetHeldBy(self).Count(), "The sold item leaves the seller's hands.");
            Assert.IsTrue(
                provider.GetObjectsAt(Loc).Any(o => o.Id == held.Id && o.ShopId == "bakery"),
                "The sold item enters the shop's stock at the current location.");
        }

        [TestMethod]
        public void Sell_ToAnEmptyShelf_UsesTheCounterAndTheLedgerPrice()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(FoodItemCatalog.Create(PickupItemKind.Bread, Loc, new WDateTime(0), self));
            provider.AddObject(ShopCounterFactory.Create("counter", Loc, "bakery", PickupItemKind.Bread, "Pult"));
            var engine = MakeObjectEngine(provider);
            var outbox = new EventCollector();

            engine.Handle(new ActionCommitted(new WDateTime(0), self, Sell, WTimeSpan.FromMinutes(15)),
                BehaviorComponentTestFactory.Context(selfId: self).HumanContext, outbox);

            var sold = outbox.Drain().OfType<Sold>().SingleOrDefault();
            Assert.IsNotNull(sold, "A counter is enough for the shop to buy.");
            Assert.IsTrue(sold!.Price > 0.0);
            Assert.AreEqual(1, provider.GetObjectsAt(Loc).Count(o => o.ShopId == "bakery" && o.Price is not null), "The loaf is now on the shelf.");
        }

        [TestMethod]
        public void Process_WithSeveralSitesAtOneLocation_RunsTheOneTheInputsCover()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(ProductionSiteFactory.Create("mill", Loc, PickupItemKind.Flour, "Mlýn"));
            provider.AddObject(ProductionSiteFactory.Create("oven", Loc, PickupItemKind.Bread, "Pec"));
            provider.AddObject(FoodItemCatalog.Create(PickupItemKind.Flour, Loc, new WDateTime(0), self));
            var engine = MakeObjectEngine(provider);

            engine.Handle(new ActionCommitted(new WDateTime(0), self, Process, WTimeSpan.FromMinutes(90)),
                BehaviorComponentTestFactory.Context(selfId: self).HumanContext, new EventCollector());

            Assert.IsTrue(provider.GetHeldBy(self).Any(o => o.ItemKind == PickupItemKind.Bread),
                "Flour in hand bakes into bread even though the mill is listed first.");
        }

        // ── §6 ContingencySearch Buy branch ──────────────────────────────────────────

        [TestMethod]
        public void ContingencySearch_PrefersBuy_WhenCheaperThanProduce_AndAffordable()
        {
            var engine = new ContingencySearchEngine();
            var priced = PricedFood("shop_bread", price: 3.0, shop: "bakery");
            var field = ProductionSiteFactory.Create("field", Loc, PickupItemKind.Grain, "Pole");
            var ctx = EconContext(available: new() { priced, field }, held: new(), wealth: 30.0, hunger: 80);

            var output = engine.Evaluate(ctx);

            var buy = output.Candidates.FirstOrDefault(c => c.Name == Buy);
            var produce = output.Candidates.FirstOrDefault(c => c.Name == Produce);
            Assert.IsNotNull(buy, "Buy must be offered when a priced food is present and affordable.");
            Assert.IsNotNull(produce, "Produce must still be offered at a co-located raw site.");
            Assert.IsTrue(buy!.Utility > produce!.Utility,
                "Buying (near-instant) should outrank harvesting raw material when affordable.");
        }

        [TestMethod]
        public void ContingencySearch_SkipsBuy_WhenWealthInsufficient()
        {
            var engine = new ContingencySearchEngine();
            var gate = new ObjectAffordanceGatingEngine();
            var priced = PricedFood("shop_bread", price: 5.0, shop: "bakery");
            var ctx = EconContext(available: new() { priced }, held: new(), wealth: 1.0, hunger: 80);

            var candidates = engine.Evaluate(ctx).Candidates.ToList();
            gate.Modify(ctx, candidates);

            Assert.IsFalse(candidates.Any(c => c.Name == Buy),
                "Buy is regulated out by the affordability gate, not merely by rank.");
        }

        // ── §7 LOD: background agents skip the ledger price formation ────────────────

        [TestMethod]
        public void Buy_BackgroundLod_SkipsLedgerPriceFormation()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(PricedFood("shop_bread", price: 2.0, shop: "bakery"));

            var ctx = BuyerContext(self, wealth: 30.0);
            var background = MakeObjectEngine(provider, new AllBackgroundLod());
            var outbox = new EventCollector();

            background.Handle(new ActionCommitted(new WDateTime(0), self, Buy, WTimeSpan.FromMinutes(15)), ctx, outbox);

            var events = outbox.Drain();
            Assert.IsTrue(events.OfType<Purchased>().Any(), "Background buy still transfers the object + deducts coin.");
            Assert.IsFalse(events.OfType<PriceChanged>().Any(),
                "Background agents must NOT trigger per-shop price formation (no PriceChanged / ledger call).");
            Assert.AreEqual(1, provider.GetHeldBy(self).Count(), "The bought item still lands in the buyer's hand.");
        }

        [TestMethod]
        public void Buy_FullSimulation_DoesLedgerPriceFormation()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(PricedFood("shop_bread", price: 2.0, shop: "bakery"));

            var ctx = BuyerContext(self, wealth: 30.0);
            var full = MakeObjectEngine(provider, lod: null); // no LOD runtime ⇒ fully simulated
            var outbox = new EventCollector();

            full.Handle(new ActionCommitted(new WDateTime(0), self, Buy, WTimeSpan.FromMinutes(15)), ctx, outbox);

            Assert.IsTrue(outbox.Drain().OfType<PriceChanged>().Any(),
                "A fully-simulated buy adjusts the shop's posted price (PriceChanged emitted).");
        }

        // ── Barter ───────────────────────────────────────────────────────────────────

        private static readonly EconomyConfig Barter3 = new() { BarterOnly = true };

        [TestMethod]
        public void BarterRules_LaborCredit_PaysTheCheapestStock()
        {
            var stock = new List<WorldObject> { PricedFood("cheese_1", 3.0, "shop", PickupItemKind.Cheese), PricedFood("bread_1", 2.0, "shop") };

            var offer = BarterRules.FindOffer(stock, null, laborHours: 3.0, Barter3, _ => true);

            Assert.IsNotNull(offer);
            Assert.AreEqual("bread_1", offer.Take.Id);
            Assert.IsNull(offer.Give);
            Assert.AreEqual(3.0, offer.LaborHours, 1e-9, "A 2.0 loaf costs 2.0 x 1.5 labor hours.");
        }

        [TestMethod]
        public void BarterRules_NotEnoughLaborAndNothingHeld_NoOffer()
        {
            var stock = new List<WorldObject> { PricedFood("bread_1", 2.0, "shop") };

            Assert.IsNull(BarterRules.FindOffer(stock, new List<WorldObject>(), laborHours: 2.9, Barter3, _ => true));
        }

        [TestMethod]
        public void BarterRules_HeldItem_PaysOnlyWhenWorthTheStock()
        {
            var self = new HumanId(Guid.NewGuid());
            var cheese = FoodItemCatalog.Create(PickupItemKind.Cheese, Loc, new WDateTime(0), self);
            var cheap = new List<WorldObject> { PricedFood("bread_1", 1.5, "shop") };
            var dear = new List<WorldObject> { PricedFood("bread_1", 2.0, "shop") };

            Assert.AreEqual(cheese.Id, BarterRules.FindOffer(cheap, new[] { cheese }, 0.0, Barter3, _ => true)?.Give?.Id);
            Assert.IsNull(BarterRules.FindOffer(dear, new[] { cheese }, 0.0, Barter3, _ => true), "The shop keeps its margin: 1.8 does not cover 2.0.");
        }

        [TestMethod]
        public void BarterRules_SameKindSpoiledOrAbsentItem_IsNotSwapped()
        {
            var self = new HumanId(Guid.NewGuid());
            var bread = FoodItemCatalog.Create(PickupItemKind.Bread, Loc, new WDateTime(0), self);
            var cheese = FoodItemCatalog.Create(PickupItemKind.Cheese, Loc, new WDateTime(0), self);
            var stock = new List<WorldObject> { PricedFood("bread_1", 1.0, "shop") };

            Assert.IsNull(BarterRules.FindOffer(stock, new[] { bread }, 0.0, Barter3, _ => true), "Bread for bread is pointless.");
            Assert.IsNull(BarterRules.FindOffer(stock, new[] { cheese }, 0.0, Barter3, _ => false), "Spoiled goods are not accepted.");
            Assert.IsNull(BarterRules.FindOffer(new List<WorldObject>(), new[] { cheese }, 0.0, Barter3, _ => true), "No stock, nothing to take.");
        }

        [TestMethod]
        public void Barter_Handler_SpendsLaborHoursAndMovesStockToHand_WithoutCoin()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(PricedFood("bread_1", 2.0, "bakery"));
            provider.AddObject(PricedFood("bread_2", 2.0, "bakery"));
            var engine = MakeObjectEngine(provider, lod: null);
            var outbox = new EventCollector();

            engine.Handle(new ActionCommitted(new WDateTime(0), self, Barter, WTimeSpan.FromMinutes(15)), LaborContext(self, 4.0), outbox);

            var events = outbox.Drain();
            var bartered = events.OfType<Bartered>().Single();
            Assert.AreEqual(3.0, bartered.LaborHoursSpent, 1e-9);
            Assert.IsNull(bartered.GivenKind);
            Assert.IsFalse(events.OfType<Purchased>().Any(), "Barter moves no coin.");
            Assert.AreEqual(1, provider.GetHeldBy(self).Count(), "The taken loaf is in hand.");
            Assert.AreEqual(1, provider.GetObjectsAt(Loc).Count(o => o.ShopId == "bakery"), "One loaf is left on the shelf.");
            Assert.IsTrue(events.OfType<PriceChanged>().Any(), "The shorter stock is re-priced.");
        }

        [TestMethod]
        public void Barter_Handler_HeldItemGoesIntoTheShopsStock()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(FoodItemCatalog.Create(PickupItemKind.Cheese, Loc, new WDateTime(0), self));
            provider.AddObject(PricedFood("bread_1", 1.5, "bakery"));
            var engine = MakeObjectEngine(provider, lod: null);
            var outbox = new EventCollector();

            engine.Handle(new ActionCommitted(new WDateTime(0), self, Barter, WTimeSpan.FromMinutes(15)), LaborContext(self, 0.0), outbox);

            var bartered = outbox.Drain().OfType<Bartered>().Single();
            Assert.AreEqual(PickupItemKind.Cheese, bartered.GivenKind);
            Assert.AreEqual(PickupItemKind.Bread, provider.GetHeldBy(self).Single().ItemKind, "The character now holds the bread.");
            var shelf = provider.GetObjectsAt(Loc).Single(o => o.ShopId == "bakery");
            Assert.AreEqual(PickupItemKind.Cheese, shelf.ItemKind, "The cheese was relisted as shop stock.");
            Assert.IsNotNull(shelf.Price);
        }

        // ── Field work ───────────────────────────────────────────────────────────────

        private static WorldObject FieldObject(FieldState state, string id = "field_1", string location = Loc)
            => new() { Id = id, DisplayName = "Pole", Category = WorldObjectCategory.Ambient, LocationId = location, ItemKind = PickupItemKind.Grain, Field = state };

        /// <summary>A worker whose snapshot carries the temperature trend per day and the air temperature.</summary>
        private static IHumanContext FarmerContext(HumanId self, double trend, double temperature)
        {
            var ctx = (HumanContext)BehaviorComponentTestFactory.Context(selfId: self).HumanContext;
            ctx.Snapshot = ctx.Snapshot with
            {
                AmbientTemperature = temperature,
                Celestial = new GameEngineTools.World.Core.Astro.CelestialContext(0.5, 12.0, 6.0, 18.0, 12.0, 0.5, 0.0, true, temperature, TemperatureTrendPerDay: trend),
            };
            return ctx;
        }

        private static ActionCommitted Session(HumanId self, string action, double hours = 4.0)
            => new(new WDateTime(0), self, action, WTimeSpan.FromHours(hours));

        [TestMethod]
        public void FieldHandler_ReapFinishesTheCrop_AndLeavesSheavesToThresh()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            var t0 = new WDateTime(0);
            provider.AddObject(FieldObject(new FieldState(FieldStage.Ripe, 1.0, t0, t0, 2800.0, SeedUnits: 1000, WorkDoneHours: 158.0)));
            var engine = MakeObjectEngine(provider);
            var outbox = new EventCollector();

            engine.Handle(Session(self, Reap), FarmerContext(self, 0.1, 18.0), outbox);

            var field = provider.FindObject("field_1")!.Field!;
            Assert.AreEqual(FieldStage.Reaped, field.Stage);
            Assert.AreEqual(5000, field.PendingUnits);
            var changed = outbox.Drain().OfType<FieldStageChanged>().Single();
            Assert.AreEqual(FieldStage.Ripe, changed.From);
            Assert.AreEqual(FieldStage.Reaped, changed.To);
        }

        [TestMethod]
        public void FieldHandler_ThreshFillsTheSeedBinFirst_ThenTheFoodStore()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(FieldObject(new FieldState(FieldStage.Reaped, 1.0, SeedUnits: 1000, PendingUnits: 5000, ThreshHoursRemaining: 160.0, ReapedYear: 2025)));
            var engine = MakeObjectEngine(provider);

            for (var session = 0; session < 40; session++)
                engine.Handle(Session(self, Thresh), FarmerContext(self, 0.0, -3.0), new EventCollector());

            Assert.AreEqual(FieldStage.Fallow, provider.FindObject("field_1")!.Field!.Stage, "All the grain is out of the sheaves.");
            var seedBin = provider.FindObject(GrainStores.SeedBinId(Loc))!;
            var food = provider.FindObject(GrainStores.FoodStoreId(Loc))!;
            Assert.AreEqual(1000, seedBin.Quantity, "The seed for next year's sowing is kept back first.");
            Assert.AreEqual(4000, food.Quantity, "The rest is food.");
            Assert.IsTrue(food.IsStock && food.Category == WorldObjectCategory.Food);
        }

        [TestMethod]
        public void FieldHandler_SowTakesTheSeedFromTheBin()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(FieldObject(new FieldState(FieldStage.Ploughed, 1.0, WorkDoneHours: 28.0)));
            provider.AddObject(GrainStores.CreateSeedBin(Loc, 1500));
            var engine = MakeObjectEngine(provider);

            engine.Handle(Session(self, Sow), FarmerContext(self, -0.2, 12.0), new EventCollector());

            Assert.AreEqual(FieldStage.Sown, provider.FindObject("field_1")!.Field!.Stage);
            Assert.AreEqual(500, provider.FindObject(GrainStores.SeedBinId(Loc))!.Quantity, "A hectare takes 1000 units of seed.");
        }

        [TestMethod]
        public void FieldHandler_OutOfSeason_DoesNothing()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(FieldObject(new FieldState(FieldStage.Ploughed, 1.0)));
            provider.AddObject(GrainStores.CreateSeedBin(Loc, 1500));
            var engine = MakeObjectEngine(provider);

            engine.Handle(Session(self, Sow), FarmerContext(self, 0.1, 18.0), new EventCollector());

            Assert.AreEqual(0.0, provider.FindObject("field_1")!.Field!.WorkDoneHours, "Nobody sows in July.");
        }

        [TestMethod]
        public void EatingFromTheFoodStore_UsesOneUnitAMeal_AndTheEmptyStoreIsGone()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(GrainStores.CreateFoodStore(Loc, 2));
            var engine = MakeObjectEngine(provider);
            var ctx = LaborContext(self, 0.0);

            engine.Handle(new ActionCommitted(new WDateTime(0), self, Eat, WTimeSpan.FromMinutes(30)), ctx, new EventCollector());
            Assert.AreEqual(1, provider.FindObject(GrainStores.FoodStoreId(Loc))!.Quantity);

            engine.Handle(new ActionCommitted(new WDateTime(0), self, Eat, WTimeSpan.FromMinutes(30)), ctx, new EventCollector());
            Assert.IsNull(provider.FindObject(GrainStores.FoodStoreId(Loc)), "The last unit empties the store.");
        }

        [TestMethod]
        public void FieldNeed_OffersTheJobTheFieldNeeds_OnlyWithASeasonAndAnEmptyEnoughStomach()
        {
            var engine = new FieldWorkNeedEngine();
            var self = new HumanId(Guid.NewGuid());
            var ripe = new List<WorldObject> { FieldObject(new FieldState(FieldStage.Ripe, 1.0)) };

            BehaviorContext Ctx(double hunger, double trend, double temp, List<WorldObject> objects)
                => BehaviorComponentTestFactory.Context(selfId: self, hunger: hunger, thirst: 5, now: new WDateTime(0)) with
                {
                    AvailableObjects = objects,
                    HumanContext = FarmerContext(self, trend, temp),
                };

            Assert.AreEqual(Reap, engine.Evaluate(Ctx(20, 0.1, 18.0, ripe)).Candidates.Single().Name);
            Assert.AreEqual(0, engine.Evaluate(Ctx(85, 0.1, 18.0, ripe)).Candidates.Count, "Too hungry to work: eating comes first.");
            Assert.AreEqual(0, engine.Evaluate(Ctx(20, 0.1, 18.0, new List<WorldObject>())).Candidates.Count, "No field here.");

            var bare = new List<WorldObject> { FieldObject(new FieldState(FieldStage.Fallow, 1.0)) };
            Assert.AreEqual(Plough, engine.Evaluate(Ctx(20, 0.1, 15.0, bare)).Candidates.Single().Name);
            Assert.AreEqual(0, engine.Evaluate(Ctx(20, 0.0, -3.0, bare)).Candidates.Count, "Frozen ground.");
        }

        [TestMethod]
        public void ProductionSite_YieldMakesSeveralOutputsPerAction()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(ProductionSiteFactory.Create("field", Loc, PickupItemKind.Grain, "Pole", yield: 3.0));
            var engine = MakeObjectEngine(provider);

            engine.Handle(new ActionCommitted(new WDateTime(0), self, Produce, WTimeSpan.FromMinutes(30)), LaborContext(self, 0.0), new EventCollector());

            Assert.AreEqual(3, provider.GetHeldBy(self).Count(h => h.ItemKind == PickupItemKind.Grain), "A field with yield 3 gives three grain per harvest.");
        }

        [TestMethod]
        public void ProcessingSite_YieldMultipliesOutputsButConsumesInputsOnce()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(ProductionSiteFactory.Create("oven", Loc, PickupItemKind.Bread, "Pec", yield: 2.0));
            provider.AddObject(FoodItemCatalog.Create(PickupItemKind.Flour, Loc, new WDateTime(0), self));
            var engine = MakeObjectEngine(provider);

            engine.Handle(new ActionCommitted(new WDateTime(0), self, Process, WTimeSpan.FromMinutes(90)), LaborContext(self, 0.0), new EventCollector());

            Assert.AreEqual(2, provider.GetHeldBy(self).Count(h => h.ItemKind == PickupItemKind.Bread));
            Assert.AreEqual(0, provider.GetHeldBy(self).Count(h => h.ItemKind == PickupItemKind.Flour), "One flour went in.");
        }

        // ── Build ────────────────────────────────────────────────────────────────────

        private static WorldObject FreeFood(string id)
            => new() { Id = id, DisplayName = id, Category = WorldObjectCategory.Food, LocationId = Loc, ItemKind = PickupItemKind.Food, IsPickable = true };

        [TestMethod]
        public void SiteBuilding_NextSite_FollowsTheChainOrderUntilComplete()
        {
            var here = new List<WorldObject>();
            Assert.AreEqual(PickupItemKind.Grain, SiteBuilding.NextSite(here));

            here.Add(ProductionSiteFactory.Create("f", Loc, PickupItemKind.Grain, "Pole"));
            Assert.AreEqual(PickupItemKind.Flour, SiteBuilding.NextSite(here));

            here.Add(ProductionSiteFactory.Create("m", Loc, PickupItemKind.Flour, "Mlyn"));
            Assert.AreEqual(PickupItemKind.Bread, SiteBuilding.NextSite(here));

            here.Add(ProductionSiteFactory.Create("o", Loc, PickupItemKind.Bread, "Pec"));
            Assert.IsNull(SiteBuilding.NextSite(here), "A complete chain leaves nothing to build.");
        }

        [TestMethod]
        public void SiteBuilding_IsSettlement_NeedsFreeFoodOrASite()
        {
            Assert.IsTrue(SiteBuilding.IsSettlement(new[] { FreeFood("berries") }));
            Assert.IsTrue(SiteBuilding.IsSettlement(new[] { ProductionSiteFactory.Create("f", Loc, PickupItemKind.Grain, "Pole") }));
            Assert.IsFalse(SiteBuilding.IsSettlement(new[] { PricedFood("loaf", 2.0, "shop") }), "Shop stock alone is not a settlement.");
            Assert.IsFalse(SiteBuilding.IsSettlement(Array.Empty<WorldObject>()), "A bare house or road.");
        }

        [TestMethod]
        public void Build_Gate_KeepsBuildOnlyWhereSomethingIsMissingInASettlement()
        {
            var gate = new ObjectAffordanceGatingEngine(new EconomyConfig { BuildEnabled = true });
            var settlement = new List<WorldObject> { FreeFood("berries") };
            var complete = new List<WorldObject>
            {
                ProductionSiteFactory.Create("f", Loc, PickupItemKind.Grain, "Pole"),
                ProductionSiteFactory.Create("m", Loc, PickupItemKind.Flour, "Mlyn"),
                ProductionSiteFactory.Create("o", Loc, PickupItemKind.Bread, "Pec"),
            };

            var inSettlement = new List<BehaviorCandidate> { Cand(Build) };
            gate.Modify(EconContext(settlement, new(), 0.0), inSettlement);
            Assert.IsTrue(inSettlement.Any(c => c.Name == Build));

            var inCompleteChain = new List<BehaviorCandidate> { Cand(Build) };
            gate.Modify(EconContext(complete, new(), 0.0), inCompleteChain);
            Assert.IsFalse(inCompleteChain.Any(c => c.Name == Build), "Nothing left to build.");

            var inHouse = new List<BehaviorCandidate> { Cand(Build) };
            gate.Modify(EconContext(new(), new(), 0.0), inHouse);
            Assert.IsFalse(inHouse.Any(c => c.Name == Build), "Not a settlement.");
        }

        [TestMethod]
        public void Build_Candidate_OfferedToAModeratelyHungryCharacterInASettlement()
        {
            var engine = new ContingencySearchEngine(economy: new EconomyConfig { BuildEnabled = true });
            var settlement = new List<WorldObject> { FreeFood("berries") };

            Assert.IsFalse(new ContingencySearchEngine().Evaluate(EconContext(settlement, new(), 0.0, hunger: 40)).Candidates.Any(c => c.Name == Build), "Off by default.");
            Assert.IsTrue(engine.Evaluate(EconContext(settlement, new(), 0.0, hunger: 40)).Candidates.Any(c => c.Name == Build));
            Assert.IsFalse(engine.Evaluate(EconContext(settlement, new(), 0.0, hunger: 10)).Candidates.Any(c => c.Name == Build), "Not hungry enough to care.");
            Assert.IsFalse(engine.Evaluate(EconContext(settlement, new(), 0.0, hunger: 85)).Candidates.Any(c => c.Name == Build), "Too hungry for hours of work.");
        }

        [TestMethod]
        public void Build_Handler_AddsFieldThenMillThenBakery_ThenNothing()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(FreeFood("berries"));
            var engine = MakeObjectEngine(provider);
            var outbox = new EventCollector();

            for (var i = 0; i < 4; i++)
                engine.Handle(new ActionCommitted(new WDateTime(0), self, Build, WTimeSpan.FromHours(3)), LaborContext(self, 0.0), outbox);

            var built = outbox.Drain().OfType<SiteBuilt>().Select(e => e.OutputKind).ToList();
            CollectionAssert.AreEqual(new[] { PickupItemKind.Grain, PickupItemKind.Flour, PickupItemKind.Bread }, built, "Three builds, then the chain is complete.");
            var field = provider.GetAllObjectsAt(Loc).Single(o => o.ItemKind == PickupItemKind.Grain && o.Affordances.Any(a => a.Type == AffordanceType.Production));
            Assert.AreEqual(3.0, field.Affordances.Single(a => a.Type == AffordanceType.Production).Satisfaction, "A built field is a standard field.");
        }

        [TestMethod]
        public void BarterMode_BakerDeliversSurplusLoafToTheShop_AndKeepsTheFirst()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(ProductionSiteFactory.Create("oven", Loc, PickupItemKind.Bread, "Pec"));
            provider.AddObject(ShopCounterFactory.Create("counter", Loc, "bakery", PickupItemKind.Bread, "Pult"));
            provider.AddObject(FoodItemCatalog.Create(PickupItemKind.Bread, Loc, new WDateTime(0), self));
            provider.AddObject(FoodItemCatalog.Create(PickupItemKind.Flour, Loc, new WDateTime(0), self));
            var engine = MakeObjectEngine(provider, economy: Barter3);
            var outbox = new EventCollector();

            engine.Handle(new ActionCommitted(new WDateTime(0), self, Process, WTimeSpan.FromMinutes(90)), LaborContext(self, 0.0), outbox);

            var sold = outbox.Drain().OfType<Sold>().Single();
            Assert.IsTrue(sold.Price > 0.0);
            Assert.AreEqual(1, provider.GetObjectsAt(Loc).Count(ShopCounterFactory.IsStock), "The new loaf is on the shelf.");
            Assert.AreEqual(1, provider.GetHeldBy(self).Count(h => h.ItemKind == PickupItemKind.Bread), "The first loaf stayed with the baker.");
        }

        [TestMethod]
        public void BarterMode_BakerWithoutOwnFoodKeepsTheLoaf()
        {
            var provider = new MemProvider();
            var self = new HumanId(Guid.NewGuid());
            provider.AddObject(ProductionSiteFactory.Create("oven", Loc, PickupItemKind.Bread, "Pec"));
            provider.AddObject(ShopCounterFactory.Create("counter", Loc, "bakery", PickupItemKind.Bread, "Pult"));
            provider.AddObject(FoodItemCatalog.Create(PickupItemKind.Flour, Loc, new WDateTime(0), self));
            var engine = MakeObjectEngine(provider, economy: Barter3);
            var outbox = new EventCollector();

            engine.Handle(new ActionCommitted(new WDateTime(0), self, Process, WTimeSpan.FromMinutes(90)), LaborContext(self, 0.0), outbox);

            Assert.IsFalse(outbox.Drain().OfType<Sold>().Any());
            Assert.AreEqual(1, provider.GetHeldBy(self).Count(h => h.ItemKind == PickupItemKind.Bread), "A hungry baker eats the first loaf.");
        }

        [TestMethod]
        public void Barter_Gate_NeedsAnOffer_AndBuyIsDroppedInBarterMode()
        {
            var gate = new ObjectAffordanceGatingEngine(Barter3);
            var stock = PricedFood("bread_1", 2.0, "bakery");

            var broke = new List<BehaviorCandidate> { Cand(Barter), Cand(Buy) };
            gate.Modify(EconContext(new() { stock }, new(), wealth: 50.0) with { LaborHours = 1.0 }, broke);
            Assert.IsFalse(broke.Any(c => c.Name == Barter), "No labor credit and nothing to give: no Barter.");
            Assert.IsFalse(broke.Any(c => c.Name == Buy), "Buy is not offered in barter mode even with coin.");

            var worked = new List<BehaviorCandidate> { Cand(Barter) };
            gate.Modify(EconContext(new() { stock }, new(), wealth: 0.0) with { LaborHours = 3.0 }, worked);
            Assert.IsTrue(worked.Any(c => c.Name == Barter), "Enough labor credit for a loaf keeps Barter.");
        }

        [TestMethod]
        public void EconomyEngine_BarterMode_SaleEarnsLaborCredit_AndBarterSpendsIt()
        {
            var engine = new DefaultEconomyEngine(Options.Create(Barter3), NullLoggerFactory.Instance);
            var self = new HumanId(Guid.NewGuid());
            var ctx = BehaviorComponentTestFactory.Context(selfId: self).HumanContext;

            engine.Handle(new Sold(new WDateTime(0), self, "bread_1", PickupItemKind.Bread, "bakery", 2.0, 0.0), ctx, new EventCollector());
            Assert.AreEqual(3.0, engine.State.LaborHours, 1e-9, "Delivering a 2.0 loaf earns 3 hours of credit.");
            Assert.AreEqual(0.0, engine.State.Wealth, 1e-9, "No coin changes hands in barter mode.");

            engine.Handle(new Bartered(new WDateTime(0), self, "bread_2", PickupItemKind.Bread, "bakery", null, 3.0), ctx, new EventCollector());
            Assert.AreEqual(0.0, engine.State.LaborHours, 1e-9);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        /// <summary>An <see cref="IHumanContext"/> whose snapshot carries the given wealth (for Buy affordability).</summary>
        private static IHumanContext BuyerContext(HumanId self, double wealth)
        {
            var ctx = (HumanContext)BehaviorComponentTestFactory.Context(selfId: self).HumanContext;
            ctx.Snapshot = ctx.Snapshot with { Economy = new EconomyState(wealth) };
            return ctx;
        }

        /// <summary>An <see cref="IHumanContext"/> whose snapshot carries the given barter labor credit.</summary>
        private static IHumanContext LaborContext(HumanId self, double hours)
        {
            var ctx = (HumanContext)BehaviorComponentTestFactory.Context(selfId: self).HumanContext;
            ctx.Snapshot = ctx.Snapshot with { Economy = new EconomyState(0.0, hours) };
            return ctx;
        }

        private static DefaultEconomyEngine MakeEconomyEngine()
            => new(Options.Create(EconomyConfig.Default), NullLoggerFactory.Instance);

        private static DefaultObjectInteractionEngine MakeObjectEngine(
            MemProvider provider, ICognitiveResolutionLevelRuntime? lod = null, EconomyConfig? economy = null)
            => new(
                provider,
                new FixedLocationService(Loc),
                new AllowAllPolicy(),
                new ProductionService(provider),
                SpoilageConfig.Default,
                new GameEngineTools.World.Economy.EconomyLedger(),
                Options.Create(economy ?? EconomyConfig.Default),
                NullLogger<DefaultObjectInteractionEngine>.Instance,
                lod);

        private static BehaviorContext EconContext(
            List<WorldObject> available, List<WorldObject> held, double wealth, double hunger = 5)
        {
            var ctx = BehaviorComponentTestFactory.Context(hunger: hunger, thirst: 5, now: new WDateTime(0));
            return ctx with { AvailableObjects = available, HeldObjects = held, Wealth = wealth };
        }

        private static WorldObject PricedFood(string id, double price, string shop, PickupItemKind kind = PickupItemKind.Bread)
            => new()
            {
                Id = id,
                DisplayName = id,
                Category = WorldObjectCategory.Food,
                LocationId = Loc,
                ItemKind = kind,
                IsPickable = true,
                Price = price,
                ShopId = shop,
                ProducedAt = new WDateTime(0),
            };

        private static BehaviorCandidate Cand(string name)
            => new(name, 50.0, WTimeSpan.FromMinutes(10), BehaviorDomain.Physiological);

        /// <summary>Minimal in-memory mutable provider with real held/inventory semantics (mirrors Tier 1 tests).</summary>
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

        /// <summary>Location service stub that reports every character at a single fixed location.</summary>
        private sealed class FixedLocationService : ILocationService
        {
            private readonly string _loc;

            public FixedLocationService(string loc) => _loc = loc;

            public string? GetLocation(HumanId characterId) => _loc;

            public LocationDescriptor? GetDescriptor(string locationId)
                => new(locationId, locationId, 0.3, 0.05, 8, true, LocationType.Public);

            public void RegisterLocation(LocationDescriptor descriptor)
            { }

            public void MoveCharacter(HumanId characterId, string locationId)
            { }

            public void RemoveCharacter(HumanId characterId)
            { }

            public void DispatchContextEvents(WDateTime now, IReadOnlyList<IHuman> characters, bool forceAll = false)
            { }

            public IReadOnlyList<HumanId> GetCharactersAt(string locationId) => Array.Empty<HumanId>();

            public IReadOnlyList<string> GetLocationsByType(LocationType type) => Array.Empty<string>();
        }

        /// <summary>Permissive interaction policy stub (the Buy/Sell path does not consult it, but the ctor requires one).</summary>
        private sealed class AllowAllPolicy : IObjectInteractionPolicy
        {
            public ObjectInteractionPermission Evaluate(
                IHumanContext actor, WorldObject target, ObjectInteractionKind kind, LocationDescriptor location)
                => new(IsAllowed: true);
        }

        /// <summary>LOD runtime stub that reports every character as Background (Tier 2 §7 abstraction).</summary>
        private sealed class AllBackgroundLod : ICognitiveResolutionLevelRuntime
        {
            public CognitiveResolutionLevel Get(HumanId id) => CognitiveResolutionLevel.Background;

            public void Set(HumanId id, CognitiveResolutionLevel level)
            { }

            public void Clear(HumanId id)
            { }
        }
    }
}
