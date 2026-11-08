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
    using GameEngineTools.World.Objects.Crops;
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
        private readonly CropConfig _cropConfig;

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
            ICognitiveResolutionLevelRuntime? lodRuntime = null,
            IOptions<CropConfig>? cropConfig = null)
        {
            _cropConfig = cropConfig?.Value ?? CropConfig.Default;
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
                    case ActionNames.Barter:
                        HandleBarter(committed, ctx, outbox);
                        break;
                    case ActionNames.Build:
                        HandleBuild(committed, ctx, outbox);
                        break;
                    case ActionNames.Plough:
                        HandleFieldWork(FieldTask.Plough, committed, ctx, outbox);
                        break;
                    case ActionNames.Sow:
                        HandleFieldWork(FieldTask.Sow, committed, ctx, outbox);
                        break;
                    case ActionNames.Reap:
                        HandleFieldWork(FieldTask.Reap, committed, ctx, outbox);
                        break;
                    case ActionNames.Thresh:
                        HandleFieldWork(FieldTask.Thresh, committed, ctx, outbox);
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
                    var wasConsumed = obj.IsPickable || obj.IsStock; // pickable single-use objects and finite stocks are used up
                    if (obj.IsStock)
                        GrainStores.Take(_objectProvider, obj, 1);
                    else if (wasConsumed)
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

            // A finite stock gives up one unit; a pickable one-use object is consumed whole.
            var wasConsumed = obj.IsPickable || obj.IsStock;
            if (obj.IsStock)
                GrainStores.Take(_objectProvider, obj, 1);
            else if (wasConsumed)
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
                                      .Where(FoodItemCatalog.IsEdible)
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

            var produced = _production.Produce(ctx, locationId, outputKind.Value, committed.OccurredAt, SiteYield(locationId, outputKind.Value));
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

            // Several processing sites can share a location (mill and oven): run the first one whose recipe the inputs cover.
            WorldObject? produced = null;
            foreach (var siteOutput in ResolveProcessingOutputs(locationId))
            {
                produced = _production.Process(ctx, locationId, siteOutput, committed.OccurredAt, SiteYield(locationId, siteOutput));
                if (produced is not null)
                    break;
            }

            if (produced is null)
                return; // insufficient inputs — nothing consumed

            var recipe = RecipeRegistry.FindByOutput(produced.ItemKind);
            using (_logger.BeginCharacterScope(ctx.Id.Value, nameof(DefaultObjectInteractionEngine)))
                _logger.ItemProcessed(ctx.Id.ToString(), recipe?.Id ?? "?", produced.ItemKind.ToString(), produced.Id, locationId);

            if (_economyConfig.BarterOnly)
                DeliverSurplus(produced, locationId, committed.OccurredAt, ctx, outbox);
        }

        /// <summary>
        /// Handles the field jobs (<see cref="ActionNames.Plough"/>, <see cref="ActionNames.Sow"/>, <see cref="ActionNames.Reap"/>,
        /// <see cref="ActionNames.Thresh"/>): adds the session's hours to the first field here that can take the job now, moves it to its
        /// next stage when the labor is spent, takes seed from the bin when sowing is finished and puts threshed grain into the stores.
        /// </summary>
        private void HandleFieldWork(FieldTask task, ActionCommitted committed, IHumanContext ctx, IEventCollector outbox)
        {
            var location = _locations.GetLocation(ctx.Id);
            if (location is null || ctx.Snapshot.Celestial is not { } celestial)
                return;

            var here = _objectProvider.GetAllObjectsAt(location).Where(o => o.HeldBy is null).ToList();
            var seedBin = here.FirstOrDefault(GrainStores.IsSeedBin);
            var season = new FieldSeason(celestial.TemperatureTrendPerDay, ctx.Snapshot.AmbientTemperature, committed.OccurredAt.Year, seedBin?.Quantity ?? 0);

            var fieldObject = here.FirstOrDefault(o => o.Field is { } f && FieldWork.Available(f, season, _cropConfig) == task);
            if (fieldObject?.Field is not { } field)
                return;

            var result = FieldWork.Apply(task, field, committed.Duration.TotalHours, committed.OccurredAt, season.Year, _cropConfig, seedInStore: seedBin?.Quantity ?? 0);

            _objectProvider.RemoveObject(fieldObject.LocationId, fieldObject.Id);
            _objectProvider.AddObject(fieldObject with { Field = result.Field });

            if (result.SeedConsumed > 0 && seedBin is not null)
                GrainStores.Take(_objectProvider, seedBin, result.SeedConsumed);
            if (result.ThreshedUnits > 0)
                GrainStores.Deposit(_objectProvider, location, result.ThreshedUnits, _cropConfig);
            if (result.Completed)
                outbox.Add(new FieldStageChanged(committed.OccurredAt, ctx.Id, fieldObject.Id, location, field.Stage, result.Field.Stage));
        }

        /// <summary>
        /// Handles <see cref="ActionNames.Build"/>: adds the first missing standard site (field, mill, bakery) of the chain at the
        /// builder's settlement. No-op when the place is not a settlement or already has the whole chain.
        /// </summary>
        private void HandleBuild(ActionCommitted committed, IHumanContext ctx, IEventCollector outbox)
        {
            var location = _locations.GetLocation(ctx.Id);
            if (location is null)
                return;

            var here = _objectProvider.GetAllObjectsAt(location).ToList();
            if (!SiteBuilding.IsSettlement(here) || SiteBuilding.NextSite(here) is not { } kind)
                return;

            var site = SiteBuilding.Create(kind, location);
            _objectProvider.AddObject(site);
            outbox.Add(new SiteBuilt(committed.OccurredAt, ctx.Id, site.Id, kind, location));
        }

        /// <summary>Outputs one action makes at the site producing <paramref name="kind"/> here: its Production affordance, rounded, at least 1.</summary>
        private int SiteYield(string locationId, PickupItemKind kind)
        {
            foreach (var o in _objectProvider.GetAllObjectsAt(locationId))
            {
                if (o.ItemKind != kind)
                    continue;
                foreach (var a in o.Affordances)
                {
                    if (a.Type == AffordanceType.Production)
                        return Math.Max(1, (int)Math.Round(a.Satisfaction));
                }
            }
            return 1;
        }

        /// <summary>
        /// Barter mode: a worker keeps the first food they hold and delivers further output to a co-located shop that trades its kind, earning
        /// labor credit (through <see cref="Sold"/>). Nothing happens when there is no such shop or the worker holds no other food.
        /// </summary>
        private void DeliverSurplus(WorldObject produced, string locationId, WDateTime now, IHumanContext ctx, IEventCollector outbox)
        {
            var shop = _objectProvider.GetObjectsAt(locationId).FirstOrDefault(o => o.ShopId is not null && o.ItemKind == produced.ItemKind);
            if (shop?.ShopId is not { } shopId)
                return;
            if (!_objectProvider.GetHeldBy(ctx.Id).Any(h => h.Id != produced.Id && FoodItemCatalog.IsEdible(h)))
                return;

            var askPrice = _economyLedger.GetPrice(shopId, produced.ItemKind);
            _objectProvider.RemoveObject(produced.LocationId, produced.Id);
            _objectProvider.AddObject(produced with { HeldBy = null, LocationId = locationId, Price = askPrice, ShopId = shopId, ProducedAt = null });

            outbox.Add(new Sold(now, ctx.Id, produced.Id, produced.ItemKind, shopId, askPrice * _economyConfig.ShopBuyBackFraction, 0.0));
            if (!IsBackground(ctx.Id))
                Reprice(locationId, shopId, produced.ItemKind, askPrice, stockChange: 1, ctx, now, outbox);
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
            var newStock = _objectProvider.GetObjectsAt(location).Count(o => o.ShopId == shopId && o.ItemKind == kind && ShopCounterFactory.IsStock(o));
            var oldPrice = _economyLedger.GetPrice(shopId, kind, price);
            var newPrice = _economyLedger.AdjustPriceForStockChange(shopId, kind, newStock, _economyConfig, price, newStock + 1);
            PostPrice(location, shopId, kind, newPrice);

            outbox.Add(new PriceChanged(now, ctx.Id, shopId, kind, oldPrice, newPrice, newStock));
            using (_logger.BeginCharacterScope(ctx.Id.Value, nameof(DefaultObjectInteractionEngine)))
                _logger.PriceChanged(shopId, kind.ToString(), oldPrice, newPrice, newStock);
        }

        /// <summary>
        /// Handles <see cref="ActionNames.Barter"/>: takes the cheapest shop food the character can pay for (labor credit or a held
        /// item of equal value, see <see cref="BarterRules"/>) into their hand; a given item goes into the shop's stock. No coin moves.
        /// Both kinds are re-priced for their changed stock.
        /// </summary>
        private void HandleBarter(ActionCommitted committed, IHumanContext ctx, IEventCollector outbox)
        {
            var now = committed.OccurredAt;
            var location = _locations.GetLocation(ctx.Id);
            if (location is null)
                return;

            var offer = BarterRules.FindOffer(
                _objectProvider.GetObjectsAt(location), _objectProvider.GetHeldBy(ctx.Id), ctx.Snapshot.Economy?.LaborHours ?? 0.0,
                _economyConfig, o => Spoilage.Freshness(o, now, _spoilage) > 0.0);
            if (offer is null)
                return;

            var take = offer.Take;
            var shopId = take.ShopId ?? "shop";

            _objectProvider.RemoveObject(take.LocationId, take.Id);
            _objectProvider.AddObject(take with { HeldBy = ctx.Id, LocationId = location, Price = null, ShopId = null });

            PickupItemKind? givenKind = null;
            if (offer.Give is { } give)
            {
                givenKind = give.ItemKind;
                _objectProvider.RemoveObject(give.LocationId, give.Id);
                _objectProvider.AddObject(give with { HeldBy = null, LocationId = location, Price = _economyLedger.GetPrice(shopId, give.ItemKind), ShopId = shopId });
            }

            outbox.Add(new Bartered(now, ctx.Id, take.Id, take.ItemKind, shopId, givenKind, offer.LaborHours));

            // LOD §7: background characters skip the per-shop price formation entirely.
            if (IsBackground(ctx.Id))
                return;

            Reprice(location, shopId, take.ItemKind, take.Price ?? _economyLedger.GetPrice(shopId, take.ItemKind), stockChange: -1, ctx, now, outbox);
            if (givenKind is { } given)
                Reprice(location, shopId, given, _economyLedger.GetPrice(shopId, given), stockChange: 1, ctx, now, outbox);
        }

        /// <summary>Re-prices a shop's kind against its current stock after a swap and posts the new price on the shelf.</summary>
        private void Reprice(string location, string shopId, PickupItemKind kind, double observedPrice, int stockChange, IHumanContext ctx, WDateTime now, IEventCollector outbox)
        {
            var newStock = _objectProvider.GetObjectsAt(location).Count(o => o.ShopId == shopId && o.ItemKind == kind && ShopCounterFactory.IsStock(o));
            var oldPrice = _economyLedger.GetPrice(shopId, kind, observedPrice);
            var newPrice = _economyLedger.AdjustPriceForStockChange(shopId, kind, newStock, _economyConfig, observedPrice, newStock - stockChange);
            PostPrice(location, shopId, kind, newPrice);

            outbox.Add(new PriceChanged(now, ctx.Id, shopId, kind, oldPrice, newPrice, newStock));
            using (_logger.BeginCharacterScope(ctx.Id.Value, nameof(DefaultObjectInteractionEngine)))
                _logger.PriceChanged(shopId, kind.ToString(), oldPrice, newPrice, newStock);
        }

        /// <summary>Writes the shop's new posted price onto every in-stock object of that kind, so the next Buy really charges it.</summary>
        private void PostPrice(string location, string shopId, PickupItemKind kind, double price)
        {
            foreach (var stocked in _objectProvider.GetObjectsAt(location).Where(o => o.ShopId == shopId && o.ItemKind == kind && ShopCounterFactory.IsStock(o)).ToList())
            {
                if (stocked.Price is { } current && Math.Abs(current - price) < 1e-9)
                    continue;
                _objectProvider.RemoveObject(stocked.LocationId, stocked.Id);
                _objectProvider.AddObject(stocked with { Price = price });
            }
        }

        /// <summary>
        /// Handles <see cref="ActionNames.Sell"/>: sells a held, still-fresh food/drink item back to a
        /// co-located shop that trades its kind — moving the object into the shop's stock and crediting
        /// coin (via the emitted <see cref="Sold"/> event; the shop pays <see cref="EconomyConfig.ShopBuyBackFraction"/> of its ask). Adjusts the shop's posted price for the
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
            var askPrice = shop.Price ?? _economyLedger.GetPrice(shopId, kind);
            var salePrice = askPrice * _economyConfig.ShopBuyBackFraction;
            var oldStock = _objectProvider.GetObjectsAt(location).Count(o => o.ShopId == shopId && o.ItemKind == kind && ShopCounterFactory.IsStock(o));

            // Move the item from the seller's hand into the shop's stock, relisted at the shop's ask.
            _objectProvider.RemoveObject(item.LocationId, item.Id);
            _objectProvider.AddObject(item with { HeldBy = null, LocationId = location, Price = askPrice, ShopId = shopId });

            var newWealth = (ctx.Snapshot.Economy?.Wealth ?? 0.0) + salePrice;
            outbox.Add(new Sold(now, ctx.Id, item.Id, kind, shopId, salePrice, newWealth));
            using (_logger.BeginCharacterScope(ctx.Id.Value, nameof(DefaultObjectInteractionEngine)))
                _logger.Sold(ctx.Id.ToString(), item.Id, kind.ToString(), shopId, salePrice, newWealth);

            // LOD §7: background characters skip the per-shop price formation entirely.
            if (IsBackground(ctx.Id))
                return;

            var newStock = oldStock + 1;
            var oldPrice = _economyLedger.GetPrice(shopId, kind, askPrice);
            var newPrice = _economyLedger.AdjustPriceForStockChange(shopId, kind, newStock, _economyConfig, askPrice, oldStock);
            PostPrice(location, shopId, kind, newPrice);

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
        private IEnumerable<PickupItemKind> ResolveProcessingOutputs(string locationId)
        {
            foreach (var o in _objectProvider.GetAllObjectsAt(locationId))
            {
                if (o.Affordances.Any(a => a.Type == AffordanceType.Production) && RecipeRegistry.FindByOutput(o.ItemKind) is not null)
                    yield return o.ItemKind;
            }
        }

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
