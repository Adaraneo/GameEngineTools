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
            Assert.AreEqual(2.0, sold.Price, 1e-9, "Sale credits the shop's posted price.");

            Assert.AreEqual(0, provider.GetHeldBy(self).Count(), "The sold item leaves the seller's hands.");
            Assert.IsTrue(
                provider.GetObjectsAt(Loc).Any(o => o.Id == held.Id && o.ShopId == "bakery"),
                "The sold item enters the shop's stock at the current location.");
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

        // ── Helpers ──────────────────────────────────────────────────────────────────

        /// <summary>An <see cref="IHumanContext"/> whose snapshot carries the given wealth (for Buy affordability).</summary>
        private static IHumanContext BuyerContext(HumanId self, double wealth)
        {
            var ctx = (HumanContext)BehaviorComponentTestFactory.Context(selfId: self).HumanContext;
            ctx.Snapshot = ctx.Snapshot with { Economy = new EconomyState(wealth) };
            return ctx;
        }

        private static DefaultEconomyEngine MakeEconomyEngine()
            => new(Options.Create(EconomyConfig.Default), NullLoggerFactory.Instance);

        private static DefaultObjectInteractionEngine MakeObjectEngine(
            MemProvider provider, ICognitiveResolutionLevelRuntime? lod = null)
            => new(
                provider,
                new FixedLocationService(Loc),
                new AllowAllPolicy(),
                new ProductionService(provider),
                SpoilageConfig.Default,
                new GameEngineTools.World.Economy.EconomyLedger(),
                Options.Create(EconomyConfig.Default),
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
