// DefaultObjectInteractionEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Economy;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Economy;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Objects.Production;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Resolves <see cref="ActionNames.InteractWithObject"/> actions committed by the Behavior engine.
    /// Evaluates the interaction policy, mutates the world object provider state, and emits the
    /// appropriate domain events for downstream engines (Memory, Psychology, etc.) to consume.
    /// </summary>
    public sealed class DefaultObjectInteractionEngine : IObjectInteractionEngine
    {
        private readonly IMutableWorldObjectProvider _objectProvider;
        private readonly ILocationService _locations;
        private readonly IObjectInteractionPolicy _policy;
        private readonly ProductionService _production;
        private readonly SpoilageConfig _spoilage;
        private readonly EconomyLedger _economyLedger;
        private readonly EconomyConfig _economyConfig;
        private readonly ICognitiveResolutionLevelRuntime? _lodRuntime;
        private readonly ILogger<DefaultObjectInteractionEngine> _logger;

        /// <summary>Creates the engine with its world-object, location, policy, production, economy and logging dependencies.</summary>
        /// <param name="objectProvider">Provider of mutable world objects.</param>
        /// <param name="locations">Location service.</param>
        /// <param name="policy">Object-interaction permission policy.</param>
        /// <param name="production">Food-economy production/processing service (Tier 1).</param>
        /// <param name="spoilage">Food-spoilage rate configuration (Tier 1).</param>
        /// <param name="economyLedger">Scene posted-price aggregate (Tier 2).</param>
        /// <param name="economyConfig">Economy tuning configuration (Tier 2).</param>
        /// <param name="logger">Logger.</param>
        /// <param name="lodRuntime">
        /// Optional LOD registry. For <see cref="CognitiveResolutionLevel.Background"/> characters the
        /// buy/sell commit skips the per-shop <see cref="EconomyLedger"/> price formation (Gode &amp;
        /// Sunder: institutional pricing needs no per-agent simulation) — see food-economy Tier 2 §7.
        /// <c>null</c> ⇒ every character is treated as fully simulated (no LOD abstraction).
        /// </param>
        public DefaultObjectInteractionEngine(
            IMutableWorldObjectProvider objectProvider,
            ILocationService locations,
            IObjectInteractionPolicy policy,
            ProductionService production,
            SpoilageConfig spoilage,
            EconomyLedger economyLedger,
            IOptions<EconomyConfig> economyConfig,
            ILogger<DefaultObjectInteractionEngine> logger,
            ICognitiveResolutionLevelRuntime? lodRuntime = null)
        {
            _objectProvider = objectProvider;
            _locations = locations;
            _policy = policy;
            _production = production;
            _spoilage = spoilage;
            _economyLedger = economyLedger;
            _economyConfig = economyConfig.Value;
            _lodRuntime = lodRuntime;
            _logger = logger;
        }

        /// <summary>
        /// True when the character is simulated at <see cref="CognitiveResolutionLevel.Background"/> —
        /// the buy/sell commit then abstracts away the ledger price formation (Tier 2 §7 LOD discipline).
        /// </summary>
        private bool IsBackground(HumanId id)
            => _lodRuntime?.Get(id) == CognitiveResolutionLevel.Background;

        /// <inheritdoc/>
        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            if (@event is not ActionCommitted committed)
                return;

            if (committed.Human != ctx.Id)
                return;

            // Path A: explicit ObjectInteraction payload — covers InteractWithObject and all
            // affordance-driven action names (UseObject:Rest, UseObject:Work, …).
            var data = committed.ObjectInteraction;
            if (data is null)
            {
                // Path B: need-based actions without an ObjectInteraction payload.
                // Eat / Drink deplete a co-located Food/Drink object; the food-economy Tier 1
                // actions (EatStored / Produce / Process) touch the character's inventory and
                // production sites instead. Hunger/thirst reduction stays with
                // DefaultPhysiologyEngine (driven by the same ActionCommitted event).
                switch (committed.ActionName)
                {
                    case ActionNames.Eat or ActionNames.Drink:
                        HandleEatDrinkWithoutPayload(committed, ctx, outbox);
                        break;
                    case ActionNames.EatStored:
                        HandleEatStored(committed, ctx, outbox);
                        break;
                    case ActionNames.Produce:
                        HandleProduce(committed, ctx, outbox);
                        break;
                    case ActionNames.Process:
                        HandleProcess(committed, ctx, outbox);
                        break;
                    case ActionNames.Buy:
                        HandleBuy(committed, ctx, outbox);
                        break;
                    case ActionNames.Sell:
                        HandleSell(committed, ctx, outbox);
                        break;
                }
                return;
            }

            // Resolve the world object — search the committed location first,
            // then fall back to a cache-wide search (needed for Drop when the object
            // was picked up at a different location than where it is being dropped).
            var obj = _objectProvider.GetAllObjectsAt(data.LocationId)
                                     .FirstOrDefault(o => o.Id == data.ObjectId)
                      ?? (data.Kind == ObjectInteractionKind.Drop
                             ? _objectProvider.FindObject(data.ObjectId)
                             : null);

            if (obj is null)
                return;

            // Resolve the location descriptor for policy evaluation
            var descriptor = _locations.GetDescriptor(data.LocationId);
            if (descriptor is null)
                return;

            var permission = _policy.Evaluate(ctx, obj, data.Kind, descriptor);

            if (!permission.IsAllowed)
            {
                outbox.Add(new ObjectInteractionRefused(
                    committed.OccurredAt,
                    ctx.Id,
                    data.ObjectId,
                    permission.RefusalReason ?? "Interaction not permitted.",
                    permission.IsSocial));
                return;
            }

            switch (data.Kind)
            {
                case ObjectInteractionKind.Take:
                    _objectProvider.SetHeldBy(data.LocationId, data.ObjectId, ctx.Id);
                    outbox.Add(new ObjectTaken(committed.OccurredAt, ctx.Id, data.ObjectId, data.LocationId));
                    break;

                case ObjectInteractionKind.UseInPlace:
                    // Apply non-Ownership affordances; consume the object if it is a one-use type
                    AffordanceApplicationService.Apply(obj, ctx, outbox, committed.OccurredAt);
                    var wasConsumed = obj.IsPickable; // pickable single-use objects are consumed on use
                    if (wasConsumed)
                        _objectProvider.ConsumeObject(data.LocationId, data.ObjectId, committed.OccurredAt);
                    outbox.Add(new ObjectUsed(committed.OccurredAt, ctx.Id, data.ObjectId, data.LocationId, wasConsumed));

                    // Log object usage with affordance summary (EventId 1500)
                    var relevantAffordances = obj.Affordances
                        .Where(a => a.Type != AffordanceType.Ownership)
                        .ToList();
                    if (relevantAffordances.Count > 0)
                    {
                        var affordanceTypes = string.Join("+", relevantAffordances.Select(a => a.Type.ToString()));
                        var totalSatisfaction = relevantAffordances.Sum(a => a.Satisfaction);
                        using (_logger.BeginCharacterScope(ctx.Id.Value, nameof(DefaultObjectInteractionEngine)))
                            _logger.ObjectUsed(
                                ctx.Id.ToString(),
                                obj.Id,
                                obj.DisplayName,
                                data.LocationId,
                                affordanceTypes,
                                totalSatisfaction,
                                wasConsumed);
                    }
                    break;

                case ObjectInteractionKind.Drop:
                    {
                        // data.LocationId = where the character is NOW (the drop location).
                        // The object may still be cached under its original location — find it.
                        var heldObj = _objectProvider.FindObject(data.ObjectId);
                        if (heldObj is not null && heldObj.LocationId != data.LocationId)
                        {
                            // Object is moving to a new location: remove from original, add at drop location.
                            _objectProvider.RemoveObject(heldObj.LocationId, data.ObjectId);
                            _objectProvider.AddObject(heldObj with
                            {
                                LocationId = data.LocationId,
                                HeldBy = null
                            });
                        }
                        else
                        {
                            // Same location — just clear the holder.
                            _objectProvider.SetHeldBy(data.LocationId, data.ObjectId, null);
                        }
                        outbox.Add(new ObjectDropped(committed.OccurredAt, ctx.Id, data.ObjectId, data.LocationId));
                        break;
                    }
            }
        }

        /// <inheritdoc/>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            // No per-tick work needed; all logic is event-driven.
        }

        /// <summary>
        /// Handles <see cref="ActionNames.Eat"/> and <see cref="ActionNames.Drink"/> commits
        /// that arrive without an <c>ObjectInteraction</c> payload.
        /// These actions are generated by <c>PhysiologicalNeedsEngine</c> and win the utility
        /// arbitration over the <c>InteractWithObject</c> candidate produced by
        /// <c>ObjectInteractionBehaviorModifier</c>. We still want to record which specific
        /// object was consumed (EventId 1500) and remove it from the world stock.
        /// Hunger / thirst reduction is intentionally skipped — <c>DefaultPhysiologyEngine</c>
        /// already handles it from the same <c>ActionCommitted</c> event.
        /// </summary>
        private void HandleEatDrinkWithoutPayload(
            ActionCommitted committed, IHumanContext ctx, IEventCollector outbox)
        {
            var actionName = committed.ActionName;
            if (actionName is not (ActionNames.Eat or ActionNames.Drink))
                return;

            var locationId = _locations.GetLocation(ctx.Id);
            if (locationId is null)
                return;

            var requiredCategory = actionName == ActionNames.Eat
                ? WorldObjectCategory.Food
                : WorldObjectCategory.Drink;

            // Pick the first available FREE object of the matching category. Priced (shop-stock)
            // objects cannot be eaten/drunk for free — they must be bought first (Tier 2 scarcity).
            // Objects are already filtered to IsAvailable by IWorldObjectProvider.GetObjectsAt.
            var obj = _objectProvider.GetObjectsAt(locationId)
                                     .FirstOrDefault(o => o.Category == requiredCategory && o.Price is null);
            if (obj is null)
                return;

            // Consume the object if it is a one-use type.
            var wasConsumed = obj.IsPickable;
            if (wasConsumed)
                _objectProvider.ConsumeObject(locationId, obj.Id, committed.OccurredAt);

            outbox.Add(new ObjectUsed(committed.OccurredAt, ctx.Id, obj.Id, locationId, wasConsumed));

            // Log EventId 1500 with affordance summary.
            var relevantAffordances = obj.Affordances
                .Where(a => a.Type != AffordanceType.Ownership)
                .ToList();
            if (relevantAffordances.Count > 0)
            {
                var affordanceTypes = string.Join("+", relevantAffordances.Select(a => a.Type.ToString()));
                var totalSatisfaction = relevantAffordances.Sum(a => a.Satisfaction);
                using (_logger.BeginCharacterScope(ctx.Id.Value, nameof(DefaultObjectInteractionEngine)))
                    _logger.ObjectUsed(
                        ctx.Id.ToString(),
                        obj.Id,
                        obj.DisplayName,
                        locationId,
                        affordanceTypes,
                        totalSatisfaction,
                        wasConsumed);
            }
        }

        /// <summary>
        /// Handles <see cref="ActionNames.EatStored"/>: eats the freshest edible food item held by the
        /// character (food-economy Tier 1 pantry). Fully spoiled items are discarded, never eaten.
        /// Hunger reduction stays with <c>DefaultPhysiologyEngine</c> (same <c>EatStored</c> action).
        /// </summary>
        private void HandleEatStored(ActionCommitted committed, IHumanContext ctx, IEventCollector outbox)
        {
            var now = committed.OccurredAt;
            var held = _objectProvider.GetHeldBy(ctx.Id)
                                      .Where(o => o.Category == WorldObjectCategory.Food)
                                      .ToList();
            if (held.Count == 0)
                return;

            // Discard anything that has fully spoiled; eat the freshest of what remains.
            WorldObject? freshest = null;
            var bestFreshness = 0.0;
            foreach (var o in held)
            {
                var freshness = Spoilage.Freshness(o, now, _spoilage);
                if (freshness <= 0.0)
                {
                    _objectProvider.RemoveObject(o.LocationId, o.Id);
                    using (_logger.BeginCharacterScope(ctx.Id.Value, nameof(DefaultObjectInteractionEngine)))
                        _logger.ItemSpoiled(ctx.Id.ToString(), o.ItemKind.ToString(), o.Id, freshness);
                    continue;
                }
                if (freshness > bestFreshness)
                {
                    bestFreshness = freshness;
                    freshest = o;
                }
            }

            if (freshest is null)
                return; // everything held was spoiled

            _objectProvider.RemoveObject(freshest.LocationId, freshest.Id);
            outbox.Add(new ObjectUsed(now, ctx.Id, freshest.Id, freshest.LocationId, true));

            var nutrition = (freshest.NutritionalProfile?.CalorieGain ?? 1.0) * bestFreshness;
            using (_logger.BeginCharacterScope(ctx.Id.Value, nameof(DefaultObjectInteractionEngine)))
                _logger.PantryConsumed(ctx.Id.ToString(), freshest.ItemKind.ToString(), freshest.Id, bestFreshness, nutrition);
        }

        /// <summary>
        /// Handles <see cref="ActionNames.Produce"/>: harvests one raw item at the current production
        /// site (the co-located <c>Production</c>-affordance object whose output has no recipe).
        /// </summary>
        private void HandleProduce(ActionCommitted committed, IHumanContext ctx, IEventCollector outbox)
        {
            var locationId = _locations.GetLocation(ctx.Id);
            if (locationId is null)
                return;

            var outputKind = ResolveProductionOutput(locationId, wantRecipe: false);
            if (outputKind is null)
                return;

            var produced = _production.Produce(ctx, locationId, outputKind.Value, committed.OccurredAt);
            using (_logger.BeginCharacterScope(ctx.Id.Value, nameof(DefaultObjectInteractionEngine)))
                _logger.ItemProduced(ctx.Id.ToString(), produced.ItemKind.ToString(), produced.Id, locationId);
        }

        /// <summary>
        /// Handles <see cref="ActionNames.Process"/>: runs the recipe of the current processing site
        /// (the co-located <c>Production</c>-affordance object whose output has a recipe), consuming
        /// held/co-located inputs. No-op when inputs are insufficient.
        /// </summary>
        private void HandleProcess(ActionCommitted committed, IHumanContext ctx, IEventCollector outbox)
        {
            var locationId = _locations.GetLocation(ctx.Id);
            if (locationId is null)
                return;

            var outputKind = ResolveProductionOutput(locationId, wantRecipe: true);
            if (outputKind is null)
                return;

            var produced = _production.Process(ctx, locationId, outputKind.Value, committed.OccurredAt);
            if (produced is null)
                return; // insufficient inputs — nothing consumed

            var recipe = RecipeRegistry.FindByOutput(produced.ItemKind);
            using (_logger.BeginCharacterScope(ctx.Id.Value, nameof(DefaultObjectInteractionEngine)))
                _logger.ItemProcessed(ctx.Id.ToString(), recipe?.Id ?? "?", produced.ItemKind.ToString(), produced.Id, locationId);
        }

        /// <summary>
        /// Handles <see cref="ActionNames.Buy"/>: acquires the cheapest affordable co-located priced
        /// object (preferring Food) from a shop — deducting coin (via the emitted <see cref="Purchased"/>
        /// event, consumed by <c>DefaultEconomyEngine</c>) and placing the object into the buyer's hand
        /// with its shop pricing cleared. Adjusts the shop's posted price for the reduced stock.
        /// </summary>
        private void HandleBuy(ActionCommitted committed, IHumanContext ctx, IEventCollector outbox)
        {
            var now = committed.OccurredAt;
            var location = _locations.GetLocation(ctx.Id);
            if (location is null)
                return;

            var wealth = ctx.Snapshot.Economy?.Wealth ?? 0.0;

            // Cheapest affordable priced object, preferring Food (the provisioning use case).
            var affordable = _objectProvider.GetObjectsAt(location)
                                            .Where(o => o.Price is { } p && p <= wealth)
                                            .ToList();
            var obj = affordable.Where(o => o.Category == WorldObjectCategory.Food).OrderBy(o => o.Price).FirstOrDefault()
                      ?? affordable.OrderBy(o => o.Price).FirstOrDefault();
            if (obj is null || obj.Price is not { } price)
                return; // nothing affordable here (gate should have prevented Buy) — no-op

            var shopId = obj.ShopId ?? "shop";
            var kind = obj.ItemKind;

            // Transfer the object into the buyer's hand and strip its shop pricing — it is now owned.
            _objectProvider.RemoveObject(obj.LocationId, obj.Id);
            _objectProvider.AddObject(obj with { HeldBy = ctx.Id, LocationId = location, Price = null, ShopId = null });

            var newWealth = wealth - price;
            outbox.Add(new Purchased(now, ctx.Id, obj.Id, kind, shopId, price, newWealth));
            using (_logger.BeginCharacterScope(ctx.Id.Value, nameof(DefaultObjectInteractionEngine)))
                _logger.Purchased(ctx.Id.ToString(), obj.Id, kind.ToString(), shopId, price, newWealth);

            // LOD §7: background characters skip the per-shop price formation entirely.
            if (IsBackground(ctx.Id))
                return;

            // Recompute the shop's posted price against the reduced stock (excludes the now-held object).
            var newStock = _objectProvider.GetObjectsAt(location).Count(o => o.ShopId == shopId && o.ItemKind == kind);
            var oldPrice = _economyLedger.GetPrice(shopId, kind, price);
            var newPrice = _economyLedger.AdjustPriceForStockChange(shopId, kind, newStock, _economyConfig);

            outbox.Add(new PriceChanged(now, ctx.Id, shopId, kind, oldPrice, newPrice, newStock));
            using (_logger.BeginCharacterScope(ctx.Id.Value, nameof(DefaultObjectInteractionEngine)))
                _logger.PriceChanged(shopId, kind.ToString(), oldPrice, newPrice, newStock);
        }

        /// <summary>
        /// Handles <see cref="ActionNames.Sell"/>: sells a held, still-fresh food/drink item back to a
        /// co-located shop that trades its kind — moving the object into the shop's stock and crediting
        /// coin (via the emitted <see cref="Sold"/> event). Adjusts the shop's posted price for the
        /// increased stock. No-op when the character holds nothing a co-located shop will buy.
        /// </summary>
        private void HandleSell(ActionCommitted committed, IHumanContext ctx, IEventCollector outbox)
        {
            var now = committed.OccurredAt;
            var location = _locations.GetLocation(ctx.Id);
            if (location is null)
                return;

            var shopStock = _objectProvider.GetObjectsAt(location)
                                           .Where(o => o.ShopId is not null)
                                           .ToList();
            if (shopStock.Count == 0)
                return;

            // First held food/drink item that is still fresh and that a co-located shop trades.
            WorldObject? item = null;
            WorldObject? shop = null;
            foreach (var held in _objectProvider.GetHeldBy(ctx.Id))
            {
                if (held.Category is not (WorldObjectCategory.Food or WorldObjectCategory.Drink))
                    continue;
                if (Spoilage.Freshness(held, now, _spoilage) <= 0.0)
                    continue; // no one buys spoiled food back
                shop = shopStock.FirstOrDefault(s => s.ItemKind == held.ItemKind);
                if (shop is not null)
                {
                    item = held;
                    break;
                }
            }

            if (item is null || shop?.ShopId is not { } shopId)
                return;

            var kind = item.ItemKind;
            var salePrice = shop.Price ?? _economyLedger.GetPrice(shopId, kind);

            // Move the item from the seller's hand into the shop's stock.
            _objectProvider.RemoveObject(item.LocationId, item.Id);
            _objectProvider.AddObject(item with { HeldBy = null, LocationId = location, Price = salePrice, ShopId = shopId });

            var newWealth = (ctx.Snapshot.Economy?.Wealth ?? 0.0) + salePrice;
            outbox.Add(new Sold(now, ctx.Id, item.Id, kind, shopId, salePrice, newWealth));
            using (_logger.BeginCharacterScope(ctx.Id.Value, nameof(DefaultObjectInteractionEngine)))
                _logger.Sold(ctx.Id.ToString(), item.Id, kind.ToString(), shopId, salePrice, newWealth);

            // LOD §7: background characters skip the per-shop price formation entirely.
            if (IsBackground(ctx.Id))
                return;

            var newStock = _objectProvider.GetObjectsAt(location).Count(o => o.ShopId == shopId && o.ItemKind == kind);
            var oldPrice = _economyLedger.GetPrice(shopId, kind, salePrice);
            var newPrice = _economyLedger.AdjustPriceForStockChange(shopId, kind, newStock, _economyConfig);

            outbox.Add(new PriceChanged(now, ctx.Id, shopId, kind, oldPrice, newPrice, newStock));
            using (_logger.BeginCharacterScope(ctx.Id.Value, nameof(DefaultObjectInteractionEngine)))
                _logger.PriceChanged(shopId, kind.ToString(), oldPrice, newPrice, newStock);
        }

        /// <summary>
        /// Finds the output kind of the production site at <paramref name="locationId"/>: the first
        /// co-located object carrying a <see cref="AffordanceType.Production"/> affordance whose
        /// <c>ItemKind</c> either has a recipe (<paramref name="wantRecipe"/> = true → processing site)
        /// or has none (raw production site). Returns <c>null</c> when no matching site is present.
        /// </summary>
        private PickupItemKind? ResolveProductionOutput(string locationId, bool wantRecipe)
        {
            foreach (var o in _objectProvider.GetAllObjectsAt(locationId))
            {
                if (!o.Affordances.Any(a => a.Type == AffordanceType.Production))
                    continue;
                var hasRecipe = RecipeRegistry.FindByOutput(o.ItemKind) is not null;
                if (hasRecipe == wantRecipe)
                    return o.ItemKind;
            }
            return null;
        }
    }
}
