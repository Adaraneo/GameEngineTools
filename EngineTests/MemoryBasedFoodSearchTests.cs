// MemoryBasedFoodSearchTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Attraction;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Objects;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Movement;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Simulation;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging.Abstractions;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Tests for memory-based food/drink routing in <see cref="DefaultSceneOrchestrator"/>.
    /// Verifies that characters navigate toward remembered food locations (via
    /// <see cref="MemoryIndex.KnownObjects"/>) before falling back to the omniscient provider.
    /// </summary>
    [TestClass]
    public class MemoryBasedFoodSearchTests : TestBase
    {
        #region Constants

        private static readonly WDateTime Now = new WDateTime(0);

        /// <summary>Home location — where the character starts.</summary>
        private const string HomeId = "home";

        /// <summary>Adjacent location with food (remembered).</summary>
        private const string TavernId = "tavern";

        /// <summary>Non-adjacent location with food (remembered, higher confidence).</summary>
        private const string ForestId = "forest";

        /// <summary>Location only known to the omniscient provider (not in memory).</summary>
        private const string MarketId = "market";

        private static readonly LocationDescriptor HomeDescriptor = new(
            Id: HomeId, DisplayName: "Home", BaseNoise: 0.05, NoisePerPerson: 0.01,
            Capacity: 10, AllowsPrivacy: true, Type: LocationType.Private);

        private static readonly LocationDescriptor TavernDescriptor = new(
            Id: TavernId, DisplayName: "Tavern", BaseNoise: 0.10, NoisePerPerson: 0.02,
            Capacity: 20, AllowsPrivacy: false, Type: LocationType.Social);

        private static readonly LocationDescriptor ForestDescriptor = new(
            Id: ForestId, DisplayName: "Forest", BaseNoise: 0.02, NoisePerPerson: 0.00,
            Capacity: 50, AllowsPrivacy: true, Type: LocationType.Public);

        private static readonly LocationDescriptor MarketDescriptor = new(
            Id: MarketId, DisplayName: "Market", BaseNoise: 0.20, NoisePerPerson: 0.05,
            Capacity: 30, AllowsPrivacy: false, Type: LocationType.Social);

        #endregion Constants

        #region Private fields

        private DefaultLocationService _locationService = default!;

        #endregion Private fields

        #region Setup

        protected override void TestInit()
        {
            base.TestInit();
            _locationService = new DefaultLocationService();
            _locationService.RegisterLocation(HomeDescriptor);
            _locationService.RegisterLocation(TavernDescriptor);
            _locationService.RegisterLocation(ForestDescriptor);
            _locationService.RegisterLocation(MarketDescriptor);
        }

        #endregion Setup

        // ══════════════════════════════════════════════════════════════════════
        // AskToBorrow — a Request to the owner of a tool, who lends or refuses
        // ══════════════════════════════════════════════════════════════════════

        private (ForagingHuman Asker, ForagingHuman Owner, ConsumingWorldObjectProvider Provider, DefaultSceneOrchestrator Orchestrator) ToolScene(
            double trust, double like, double closeness, bool ownerOnShift = false, bool ownerHere = true, bool noEdge = false)
        {
            var askerId = new HumanId(Guid.NewGuid());
            var ownerId = new HumanId(Guid.NewGuid());
            var asker = BuildPerson(HomeId, new ActionCommitted(Now, askerId, AskToBorrow, WTimeSpan.FromMinutes(30), TargetTypeId: "spade"));
            var edges = noEdge ? null : new Dictionary<HumanId, RelationshipEdge> { [askerId] = Edge(ownerId, askerId, trust, like, closeness) };
            var owner = BuildPerson(ownerHere ? HomeId : TavernId, new ActionCommitted(Now, ownerId, Idle, WTimeSpan.FromMinutes(5)), edges, ownerOnShift);
            var spade = GameEngineTools.World.Work.WorkObjects.Spawn(EngineTests.Utils.TestWork.Catalog, "spade", "spade_1", HomeId, ownerId);
            var provider = new ConsumingWorldObjectProvider(new[] { spade }, Array.Empty<string>());
            var orchestrator = BuildOrchestrator(BuildMap(new[] { (HomeId, TavernId, 50.0) }), provider);
            return (asker, owner, provider, orchestrator);
        }

        [TestMethod]
        public void AskToBorrow_ATrustedOwnerLends_AndBothAreTold()
        {
            var (asker, owner, provider, orchestrator) = ToolScene(trust: 80, like: 70, closeness: 50);

            orchestrator.OnTick(Now, new IHuman[] { asker, owner });

            Assert.AreEqual(asker.Id, provider.FindObject("spade_1")!.LentTo);
            Assert.IsTrue(asker.Received.OfType<ToolLent>().Any() && owner.Received.OfType<ToolLent>().Any());
        }

        [TestMethod]
        public void AskToBorrow_AStrangerIsRefused()
        {
            var (asker, owner, provider, orchestrator) = ToolScene(0, 0, 0, noEdge: true);

            orchestrator.OnTick(Now, new IHuman[] { asker, owner });

            Assert.IsNull(provider.FindObject("spade_1")!.LentTo);
            Assert.IsTrue(asker.Received.OfType<ToolRequestRefused>().Any());
        }

        [TestMethod]
        public void AskToBorrow_AnOwnerWhoNeedsTheToolNowRefuses_EvenATrustedAsker()
        {
            var (asker, owner, provider, orchestrator) = ToolScene(90, 90, 90, ownerOnShift: true);

            orchestrator.OnTick(Now, new IHuman[] { asker, owner });

            Assert.IsNull(provider.FindObject("spade_1")!.LentTo);
            Assert.IsTrue(asker.Received.OfType<ToolRequestRefused>().Any(r => r.Reason.Contains("needs")));
        }

        [TestMethod]
        public void AskToBorrow_NobodyWhoOwnsOneIsHere_IsRefused()
        {
            var (asker, owner, provider, orchestrator) = ToolScene(90, 90, 90, ownerHere: false);

            orchestrator.OnTick(Now, new IHuman[] { asker, owner });

            Assert.IsNull(provider.FindObject("spade_1")!.LentTo);
            Assert.IsTrue(asker.Received.OfType<ToolRequestRefused>().Any());
        }

        [TestMethod]
        public void ALoanKeptPastTheGracePeriod_CostsTheBorrowerTheOwnersTrust_OnceAPeriod()
        {
            var (asker, owner, provider, orchestrator) = ToolScene(90, 90, 90);
            var idle = BuildPerson(HomeId, new ActionCommitted(Now, asker.Id, Idle, WTimeSpan.FromMinutes(5)));
            GameEngineTools.World.Work.ToolLending.Lend(provider, "spade_1", owner.Id, idle.Id, Now);
            var chars = new IHuman[] { idle, owner };

            orchestrator.OnTick(Now + WTimeSpan.FromDays(1), chars);
            Assert.IsFalse(owner.Received.OfType<MicroNegative>().Any(), "Within the grace period nothing is held against the borrower.");

            orchestrator.OnTick(Now + WTimeSpan.FromDays(4), chars);
            var penalty = owner.Received.OfType<MicroNegative>().Single();
            Assert.AreEqual((owner.Id, idle.Id), (penalty.A, penalty.B));

            orchestrator.OnTick(Now + WTimeSpan.FromDays(5), chars);
            Assert.AreEqual(1, owner.Received.OfType<MicroNegative>().Count(), "The loan clock restarted: not reported again the next day.");
        }

        // ══════════════════════════════════════════════════════════════════════
        // MoveTo:Supply — walk to the workstation a craft task needs
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void RouteMoveTo_Supply_WalksToTheNearestPlaceWithTheNamedWorkstation()
        {
            var worldMap = BuildMap(new[] { (HomeId, TavernId, 60.0), (HomeId, MarketId, 40.0) });
            var character = BuildForagingHuman(HomeId, MoveToSupply, Array.Empty<ObjectLocationFact>(), targetTypeId: "mill");
            var provider = new FixedWorldObjectProvider(new[]
            {
                GameEngineTools.World.Work.WorkObjects.Spawn(EngineTests.Utils.TestWork.Catalog, "mill", "tavern_mill", TavernId),
                GameEngineTools.World.Work.WorkObjects.Spawn(EngineTests.Utils.TestWork.Catalog, "mill", "market_mill", MarketId),
                GameEngineTools.World.Work.WorkObjects.Spawn(EngineTests.Utils.TestWork.Catalog, "oven", "home_oven", HomeId),
            });

            BuildOrchestrator(worldMap, provider).OnTick(Now, new[] { character });

            Assert.AreEqual(MarketId, _locationService.GetLocation(character.Id), "The mill at the nearer place; the oven at home is not a mill.");
        }

        [TestMethod]
        public void RouteMoveTo_Supply_WithNoSuchWorkstationAnywhere_StaysPut()
        {
            var worldMap = BuildMap(new[] { (HomeId, TavernId, 60.0) });
            var character = BuildForagingHuman(HomeId, MoveToSupply, Array.Empty<ObjectLocationFact>(), targetTypeId: "mill");
            var provider = new FixedWorldObjectProvider(new[] { GameEngineTools.World.Work.WorkObjects.Spawn(EngineTests.Utils.TestWork.Catalog, "oven", "tavern_oven", TavernId) });

            BuildOrchestrator(worldMap, provider).OnTick(Now, new[] { character });

            Assert.AreEqual(HomeId, _locationService.GetLocation(character.Id));
        }

        // ══════════════════════════════════════════════════════════════════════
        // Test 1 — Memory has food location → uses it
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Character remembers food at "tavern" (confidence 0.85, adjacent).
        /// Provider is empty. Must navigate to "tavern".
        /// </summary>
        [TestMethod]
        public void RouteMoveTo_MemoryHasFoodLocation_UsesMemory()
        {
            var knownObjects = new[]
            {
                new ObjectLocationFact("bread_01", TavernId, Now, 0.85, WorldObjectCategory.Food)
            };

            // Adjacent: home → tavern
            var worldMap = BuildMap(new[]
            {
                (HomeId, TavernId, 50.0)
            });

            var character = BuildForagingHuman(
                currentLocation: HomeId,
                action: MoveToFood,
                knownObjects: knownObjects);

            var orchestrator = BuildOrchestrator(worldMap, provider: new EmptyWorldObjectProvider());
            orchestrator.OnTick(Now, new[] { character });

            Assert.AreEqual(TavernId, _locationService.GetLocation(character.Id),
                "Character must navigate to the remembered food location (tavern).");
        }

        // ══════════════════════════════════════════════════════════════════════
        // Test 2 — Memory below threshold → falls back to provider
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Character remembers food at "forest" (confidence 0.10, below MinMemoryConfidence 0.15).
        /// Provider knows food at "market". Must use provider fallback → "market".
        /// </summary>
        [TestMethod]
        public void RouteMoveTo_MemoryBelowThreshold_FallsBackToProvider()
        {
            var knownObjects = new[]
            {
                new ObjectLocationFact("old_mushroom", ForestId, Now, 0.10, WorldObjectCategory.Food)
            };

            var worldMap = BuildMap(new[]
            {
                (HomeId, ForestId, 200.0),
                (HomeId, MarketId, 100.0)
            });

            var character = BuildForagingHuman(
                currentLocation: HomeId,
                action: MoveToFood,
                knownObjects: knownObjects);

            // Provider has food only at market
            var provider = new FixedWorldObjectProvider(new[]
            {
                MakeFoodObject("market_bread", MarketId)
            });

            var orchestrator = BuildOrchestrator(worldMap, provider);
            orchestrator.OnTick(Now, new[] { character });

            Assert.AreEqual(MarketId, _locationService.GetLocation(character.Id),
                "Memory below threshold must be ignored — character must fall back to provider (market).");
        }

        // ══════════════════════════════════════════════════════════════════════
        // Test 3 — Memory empty → falls back to provider
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Character has no object memories at all.
        /// Provider knows food at "tavern". Must use provider fallback → "tavern".
        /// </summary>
        [TestMethod]
        public void RouteMoveTo_MemoryEmpty_FallsBackToProvider()
        {
            var worldMap = BuildMap(new[]
            {
                (HomeId, TavernId, 60.0)
            });

            var character = BuildForagingHuman(
                currentLocation: HomeId,
                action: MoveToFood,
                knownObjects: Array.Empty<ObjectLocationFact>());

            var provider = new FixedWorldObjectProvider(new[]
            {
                MakeFoodObject("tavern_bread", TavernId)
            });

            var orchestrator = BuildOrchestrator(worldMap, provider);
            orchestrator.OnTick(Now, new[] { character });

            Assert.AreEqual(TavernId, _locationService.GetLocation(character.Id),
                "Empty memory must trigger provider fallback — character must move to tavern.");
        }

        // ══════════════════════════════════════════════════════════════════════
        // Test 4 — Adjacent beats non-adjacent even at lower confidence
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Memory: "tavern" (adjacent, confidence 0.80) and "forest" (non-adjacent, confidence 0.95).
        /// Adjacent location must win — travel cost matters more than raw confidence.
        /// </summary>
        [TestMethod]
        public void RouteMoveTo_AdjacentMemory_WinsOverHigherConfidenceNonAdjacent()
        {
            var knownObjects = new[]
            {
                new ObjectLocationFact("tavern_bread",   TavernId, Now, 0.80, WorldObjectCategory.Food),
                new ObjectLocationFact("forest_berries", ForestId, Now, 0.95, WorldObjectCategory.Food)
            };

            // Only home → tavern is adjacent; home → forest is not in the adjacency graph.
            var worldMap = BuildMap(new[]
            {
                (HomeId, TavernId, 50.0)
            });

            var character = BuildForagingHuman(
                currentLocation: HomeId,
                action: MoveToFood,
                knownObjects: knownObjects);

            var orchestrator = BuildOrchestrator(worldMap, provider: new EmptyWorldObjectProvider());
            orchestrator.OnTick(Now, new[] { character });

            Assert.AreEqual(TavernId, _locationService.GetLocation(character.Id),
                "Adjacent remembered location must win over a non-adjacent higher-confidence one.");
        }

        // ══════════════════════════════════════════════════════════════════════
        // Test 5 — No memory, no provider → character stays in place, no crash
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// No object memories and provider returns nothing.
        /// Character must remain in place without throwing.
        /// </summary>
        [TestMethod]
        public void RouteMoveTo_NoMemoryNoProvider_CharacterStaysInPlace()
        {
            var worldMap = BuildMap(Array.Empty<(string, string, double)>());

            var character = BuildForagingHuman(
                currentLocation: HomeId,
                action: MoveToFood,
                knownObjects: Array.Empty<ObjectLocationFact>());

            var orchestrator = BuildOrchestrator(worldMap, provider: new EmptyWorldObjectProvider());
            orchestrator.OnTick(Now, new[] { character });

            Assert.AreEqual(HomeId, _locationService.GetLocation(character.Id),
                "When no memory and no provider, character must stay at home.");
        }

        // ══════════════════════════════════════════════════════════════════════
        // Provider fallback must not route to food that is already consumed
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The nearest location holds only consumed food (awaiting respawn); another location has a ready meal.
        /// The character must go to the one with food actually there.
        /// </summary>
        [TestMethod]
        public void RouteMoveTo_ProviderFoodAlreadyConsumed_SkipsThatLocation()
        {
            var worldMap = BuildMap(new[] { (HomeId, TavernId, 50.0), (HomeId, MarketId, 100.0) });
            var character = BuildForagingHuman(HomeId, MoveToFood, Array.Empty<ObjectLocationFact>());
            var provider = new ConsumingWorldObjectProvider(
                new[] { MakeFoodObject("tavern_bread", TavernId), MakeFoodObject("market_bread", MarketId) },
                consumedIds: new[] { "tavern_bread" });

            BuildOrchestrator(worldMap, provider).OnTick(Now, new[] { character });

            Assert.AreEqual(MarketId, _locationService.GetLocation(character.Id),
                "A location whose food is consumed must not be chosen while another one has ready food.");
        }

        /// <summary>No adjacent food: the character walks to the closer of two distant meals, not to the first one listed.</summary>
        [TestMethod]
        public void RouteMoveTo_NoAdjacentFood_ChoosesNearestByPathLength()
        {
            var worldMap = BuildMap(new[] { (HomeId, TavernId, 50.0), (TavernId, MarketId, 50.0), (TavernId, ForestId, 500.0) });
            var character = BuildForagingHuman(HomeId, MoveToFood, Array.Empty<ObjectLocationFact>());
            var provider = new ConsumingWorldObjectProvider(
                new[] { MakeFoodObject("forest_berries", ForestId), MakeFoodObject("market_bread", MarketId) }, consumedIds: Array.Empty<string>());

            BuildOrchestrator(worldMap, provider).OnTick(Now, new[] { character });

            Assert.AreEqual(MarketId, _locationService.GetLocation(character.Id),
                "With no adjacent food the nearest reachable meal (market, 100 m) must beat the farther one (forest, 550 m).");
        }

        /// <summary>All food in the world is consumed: there is nowhere to go, so the character stays put.</summary>
        [TestMethod]
        public void RouteMoveTo_AllProviderFoodConsumed_CharacterStaysInPlace()
        {
            var worldMap = BuildMap(new[] { (HomeId, TavernId, 50.0) });
            var character = BuildForagingHuman(HomeId, MoveToFood, Array.Empty<ObjectLocationFact>());
            var provider = new ConsumingWorldObjectProvider(
                new[] { MakeFoodObject("tavern_bread", TavernId) }, consumedIds: new[] { "tavern_bread" });

            BuildOrchestrator(worldMap, provider).OnTick(Now, new[] { character });

            Assert.AreEqual(HomeId, _locationService.GetLocation(character.Id),
                "With every food object consumed the character must not walk to an empty location.");
        }

        // ══════════════════════════════════════════════════════════════════════
        // Test 6 — Drink routing uses memory
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Character remembers drink at "tavern" (confidence 0.80).
        /// MoveTo:Drink must navigate there via memory.
        /// </summary>
        [TestMethod]
        public void RouteMoveTo_DrinkMemory_UsesCorrectItemKind()
        {
            var knownObjects = new[]
            {
                new ObjectLocationFact("well_01", TavernId, Now, 0.80, WorldObjectCategory.Drink)
            };

            var worldMap = BuildMap(new[]
            {
                (HomeId, TavernId, 50.0)
            });

            var character = BuildForagingHuman(
                currentLocation: HomeId,
                action: MoveToDrink,
                knownObjects: knownObjects);

            var orchestrator = BuildOrchestrator(worldMap, provider: new EmptyWorldObjectProvider());
            orchestrator.OnTick(Now, new[] { character });

            Assert.AreEqual(TavernId, _locationService.GetLocation(character.Id),
                "MoveTo:Drink must use Drink memory, not Food.");
        }

        /// <summary>A jug in a neighbour's house is not a place to go for water; the same jug in the character's own house is.</summary>
        [TestMethod]
        public void RouteMoveTo_DrinkMemory_SkipsAForeignHome_ButNotItsOwn()
        {
            var knownObjects = new[] { new ObjectLocationFact("jug_01", TavernId, Now, 0.80, WorldObjectCategory.Drink) };
            var worldMap = BuildMap(new[] { (HomeId, TavernId, 50.0) });

            var thirsty = BuildForagingHuman(HomeId, MoveToDrink, knownObjects);
            var neighbour = BuildForagingHuman(TavernId, Idle, Array.Empty<ObjectLocationFact>());
            neighbour.Home = TavernId;
            BuildOrchestrator(worldMap, new EmptyWorldObjectProvider()).OnTick(Now, new IHuman[] { thirsty, neighbour });
            Assert.AreEqual(HomeId, _locationService.GetLocation(thirsty.Id), "Someone else lives at the tavern: no walking in for a drink.");

            var owner = BuildForagingHuman(HomeId, MoveToDrink, knownObjects);
            owner.Home = TavernId;
            BuildOrchestrator(worldMap, new EmptyWorldObjectProvider()).OnTick(Now, new IHuman[] { owner });
            Assert.AreEqual(TavernId, _locationService.GetLocation(owner.Id), "Its own home is fine.");
        }

        // ══════════════════════════════════════════════════════════════════════
        // Test 7 — Travel time: character stays in transit, arrives after the trip
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// With <see cref="SceneOrchestratorOptions.EnableTravelTime"/> on, a 1600 m trip at
        /// 80 m/min takes 20 minutes: the character must NOT teleport on the routing tick. While
        /// travelling it is genuinely unplaced (on the road — <c>GetLocation</c> is <c>null</c>,
        /// so it is not at its origin for crowding/social purposes), then it is placed at the
        /// tavern once the arrival time is reached.
        /// </summary>
        [TestMethod]
        public void RouteMoveTo_TravelTimeEnabled_ArrivesOnlyAfterTravelDuration()
        {
            var knownObjects = new[]
            {
                new ObjectLocationFact("bread_01", TavernId, Now, 0.85, WorldObjectCategory.Food)
            };

            // home → tavern, 1600 m; at 80 m/min that is a 20-minute walk.
            var worldMap = BuildMap(new[] { (HomeId, TavernId, 1600.0) });

            var character = BuildForagingHuman(
                currentLocation: HomeId,
                action: MoveToFood,
                knownObjects: knownObjects);

            var orchestrator = BuildOrchestrator(
                worldMap,
                provider: new EmptyWorldObjectProvider(),
                options: new SceneOrchestratorOptions { EnableTravelTime = true });

            // Routing tick: trip starts, character is on the road (unplaced), not at home.
            orchestrator.OnTick(Now, new[] { character });
            Assert.IsNull(_locationService.GetLocation(character.Id),
                "While travelling the character must be unplaced (on the road), not at its origin.");
            Assert.AreEqual(TavernId, orchestrator.GetTravelDestination(character.Id),
                "The orchestrator must report the in-flight destination.");

            // Halfway (10 min < 20 min): still travelling, still unplaced.
            orchestrator.OnTick(Now + WTimeSpan.FromMinutes(10), new[] { character });
            Assert.IsNull(_locationService.GetLocation(character.Id),
                "Before the travel duration elapses the character has not arrived yet.");
            Assert.AreEqual(TavernId, orchestrator.GetTravelDestination(character.Id),
                "Still in transit halfway through the trip.");

            // Past arrival (25 min >= 20 min): placed at the destination.
            orchestrator.OnTick(Now + WTimeSpan.FromMinutes(25), new[] { character });
            Assert.AreEqual(TavernId, _locationService.GetLocation(character.Id),
                "Once the travel duration has elapsed the character arrives at the tavern.");
            Assert.IsNull(orchestrator.GetTravelDestination(character.Id),
                "After arrival the character is no longer in transit.");
        }

        /// <summary>A trip that fits in one clock step happens at once; a longer one is still timed.</summary>
        [TestMethod]
        public void RouteMoveTo_TripShorterThanTheThreshold_ArrivesAtOnce_LongerTripStillTimed()
        {
            var knownObjects = new[] { new ObjectLocationFact("well_01", TavernId, Now, 0.80, WorldObjectCategory.Drink) };
            var options = new SceneOrchestratorOptions { EnableTravelTime = true, InstantTravelBelowMinutes = 15.0 };

            // 50 m at 80 m/min is under a minute.
            var near = BuildForagingHuman(HomeId, MoveToDrink, knownObjects);
            BuildOrchestrator(BuildMap(new[] { (HomeId, TavernId, 50.0) }), new EmptyWorldObjectProvider(), options).OnTick(Now, new[] { near });
            Assert.AreEqual(TavernId, _locationService.GetLocation(near.Id), "A trip under the threshold must not hold the character in transit.");

            // 1600 m is 20 minutes, above the threshold.
            var far = BuildForagingHuman(HomeId, MoveToDrink, knownObjects);
            var orchestrator = BuildOrchestrator(BuildMap(new[] { (HomeId, TavernId, 1600.0) }), new EmptyWorldObjectProvider(), options);
            orchestrator.OnTick(Now, new[] { far });
            Assert.IsNull(_locationService.GetLocation(far.Id), "A 20 minute trip is still in transit.");
            Assert.AreEqual(TavernId, orchestrator.GetTravelDestination(far.Id));
        }

        // ══════════════════════════════════════════════════════════════════════
        // Factory helpers
        // ══════════════════════════════════════════════════════════════════════

        #region Factory methods

        private DefaultSceneOrchestrator BuildOrchestrator(
            WorldMap worldMap,
            IWorldObjectProvider provider,
            SceneOrchestratorOptions? options = null)
            => new DefaultSceneOrchestrator(
                attractionCalculator: new NeutralAttractionCalculator(),
                locationService: _locationService,
                perceptionPolicy: new AllFullPerceptionPolicy(),
                perceptionOptions: new CharacterPerceptionOptions
                {
                    MaxLocalOnlyTargets = 2,
                    MaxCoarseTargets = 1,
                    LocalOnlyNoiseThreshold = 0.85,
                    LocalOnlyCrowdingThreshold = 0.90,
                    CoarseNoiseThreshold = 0.60,
                    CoarseCrowdingThreshold = 0.70
                },
                lodRuntime: new AllBackgroundLodRuntime(),
                worldMap: worldMap,
                speedProvider: new ConstantSpeedProvider(80.0),
                rng: new Random(42),
                log: NullLogger<DefaultSceneOrchestrator>.Instance,
                objectProvider: provider,
                options: options ?? new SceneOrchestratorOptions(),
                mutableObjects: provider as IMutableWorldObjectProvider);

        private ForagingHuman BuildForagingHuman(
            string currentLocation,
            string action,
            IReadOnlyList<ObjectLocationFact> knownObjects,
            string? targetTypeId = null)
        {
            var id = new HumanId(Guid.NewGuid());

            var memoryIndex = new MemoryIndex(Array.Empty<EpisodicMemory>())
            {
                KnownObjects = knownObjects
            };

            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                new PsychologyState(0.0, 0.5, 0.5, 10, 10, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(currentLocation, false, 0.05, 0.1, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                memoryIndex);

            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            var committed = new ActionCommitted(Now, id, action, WTimeSpan.FromMinutes(20), TargetTypeId: targetTypeId);

            var human = new ForagingHuman(id, personality, snapshot, committed);
            _locationService.MoveCharacter(id, currentLocation);
            return human;
        }

        private ForagingHuman BuildPerson(
            string location, ActionCommitted committed, Dictionary<HumanId, RelationshipEdge>? edges = null, bool onWorkShift = false, double agreeableness = 0.5)
        {
            var id = committed.Human;
            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                new PsychologyState(0.0, 0.5, 0.5, 10, 10, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(location, false, 0.05, 0.1, SurfaceKind.Social),
                new RelationshipState(edges ?? new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(Array.Empty<EpisodicMemory>()));
            if (onWorkShift)
            {
                var slot = new GameEngineTools.Characters.Engines.Schedule.ScheduleSlot("work", 5, Work);
                snapshot = snapshot with { Schedule = new GameEngineTools.Characters.Engines.Schedule.DailyScheduleState(new[] { slot }, slot, 0, "farmer") };
            }

            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, agreeableness, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);
            var human = new ForagingHuman(id, personality, snapshot, committed);
            _locationService.MoveCharacter(id, location);
            return human;
        }

        private static RelationshipEdge Edge(HumanId from, HumanId to, double trust, double like, double closeness)
            => new(from, to, Like: like, Trust: trust, Familiarity: 50, AestheticAttraction: 0, PhysicalAttraction: 0, IntimateAffinity: 0, SexualInterest: 0,
                Closeness: closeness, Respect: 50, Comfort: 50, Breakdown: new DomainBreakdown(50, 50, 50, 50, 50));

        private static WorldMap BuildMap(IEnumerable<(string From, string To, double Meters)> connections)
        {
            var adjacency = new Dictionary<string, IReadOnlyList<WorldConnection>>();

            foreach (var (from, to, meters) in connections)
            {
                if (!adjacency.ContainsKey(from))
                    adjacency[from] = new List<WorldConnection>();

                ((List<WorldConnection>)adjacency[from]).Add(new WorldConnection(to, meters));
            }

            return new WorldMap(
                new Dictionary<string, LocationDescriptor>(),
                adjacency,
                new Dictionary<string, IReadOnlyList<string>>());
        }

        private static WorldObject MakeFoodObject(string id, string locationId)
            => new WorldObject
            {
                Id = id,
                DisplayName = id,
                Category = WorldObjectCategory.Food,
                LocationId = locationId,
                IsAvailable = true,
                Affordances = System.Collections.Immutable.ImmutableArray<WorldObjectAffordance>.Empty
            };

        #endregion Factory methods

        // ══════════════════════════════════════════════════════════════════════
        // ForagingHuman spy
        // ══════════════════════════════════════════════════════════════════════

        #region ForagingHuman

        /// <summary>
        /// Minimal <see cref="IHuman"/> whose <see cref="LastOutbox"/> returns a single
        /// <see cref="ActionCommitted"/> with the requested <c>MoveTo:*</c> action name.
        /// </summary>
        private sealed class ForagingHuman : IHuman
        {
            private readonly ActionCommitted _committed;
            private readonly Personality _personality;
            private EnginesSnapshot _snapshot;

            public ForagingHuman(HumanId id, Personality personality, EnginesSnapshot snapshot, ActionCommitted committed)
            {
                Id = id;
                _personality = personality;
                _snapshot = snapshot;
                _committed = committed;
            }

            public HumanId Id { get; }

            /// <summary>The home location of this character, if any.</summary>
            public string? Home { get; set; }

            public Identity Identity => new(
                new Name { Original = "Test", Familiar = new[] { "Test" } },
                new Surname { Male = "Forager", Female = "Forager" },
                WDateOnly.New(80, 1, 1)) { HomeLocationId = Home };

            public SexBiology Biology => SexBiology.Female;
            public Personality Personality => _personality;
            public PsychologicalProfile PsychologyProfile => PsychologicalProfile.FromPersonality(_personality);

            public PhysicalAppearance PhysicalAppearance
                => TestAppearanceFactory.Build(
                    heightCm: 170,
                    frame: BodyFrame.Medium,
                    skinTone: SkinTone.Light,
                    eyeColor: EyeColor.Blue,
                    hairColor: HairColorNatural.Brown,
                    hairType: HairType.Straight,
                    faceShape: FaceShape.Oval,
                    shoulderBreadthCm: 40,
                    hipBreadthCm: 38,
                    noseProjection: 0.5,
                    lipFullness: 0.5);

            public AttractionProfile? AttractionProfile => null;
            public EnginesSnapshot Snapshot => _snapshot;
            public IReadOnlyList<IDomainEvent> LastOutbox => new[] { _committed };
            public int Age => 25;
            public StadiumType Stadium => StadiumType.Adult;

            public List<IDomainEvent> Received { get; } = new();

            public void ReceiveEvent(IDomainEvent @event) => Received.Add(@event);

            public void Tick(WDateTime now, WTimeSpan dt)
            { }

            public void RestoreSnapshot(EnginesSnapshot snapshot, WDateOnly today = default) => _snapshot = snapshot;

            public void FlushInbox()
            { }

            public int CompareTo(IHuman? other)
            {
                throw new NotImplementedException();
            }
        }

        #endregion ForagingHuman

        // ══════════════════════════════════════════════════════════════════════
        // Stubs
        // ══════════════════════════════════════════════════════════════════════

        #region Stubs

        private sealed class AllFullPerceptionPolicy : IPerceptionFidelityPolicy
        {
            public PerceptionFidelityLevel GetLevel(HumanId id) => PerceptionFidelityLevel.Full;
        }

        private sealed class AllBackgroundLodRuntime : ICognitiveResolutionLevelRuntime
        {
            public void Clear(HumanId id)
            { }

            public CognitiveResolutionLevel Get(HumanId id) => CognitiveResolutionLevel.Background;

            public void Set(HumanId id, CognitiveResolutionLevel level)
            { }
        }

        private sealed class NeutralAttractionCalculator : IAttractionCalculator
        {
            public AttractionResult Calculate(
                AttractionProfile observerProfile,
                PhysicalAppearance targetAppearance,
                AppearanceView targetView,
                SexBiology targetBiology,
                double observerValence = 0.0,
                double observerArousal = 0.0,
                int? observerAgeYears = null,
                int? targetAgeYears = null)
                => AttractionResult.Neutral;
        }

        private sealed class ConstantSpeedProvider(double metersPerMinute) : IMovementSpeedProvider
        {
            public double GetSpeedMetersPerMinute(
                EnginesSnapshot snapshot, TerrainType terrain = TerrainType.Indoor) => metersPerMinute;
        }

        private sealed class EmptyWorldObjectProvider : IWorldObjectProvider
        {
            public IEnumerable<WorldObject> GetObjectsAt(string locationId) => Enumerable.Empty<WorldObject>();

            public IEnumerable<WorldObject> GetAllObjects() => Enumerable.Empty<WorldObject>();

            public void AddObject(WorldObject obj)
            { }

            public WorldObject? FindObject(string objectId) => null;
        }

        private sealed class FixedWorldObjectProvider(IEnumerable<WorldObject> objects) : IWorldObjectProvider
        {
            private readonly List<WorldObject> _objects = objects.ToList();

            public IEnumerable<WorldObject> GetObjectsAt(string locationId)
                => _objects.Where(o => o.LocationId == locationId);

            public IEnumerable<WorldObject> GetAllObjects() => _objects;

            public void AddObject(WorldObject obj) => _objects.Add(obj);

            public WorldObject? FindObject(string objectId)
                => _objects.FirstOrDefault(o => o.Id == objectId);
        }

        /// <summary>Mirrors the SQLite provider: <c>GetAllObjects</c> lists consumed objects without marking them, runtime state is in <c>GetAllObjectsAt</c>.</summary>
        private sealed class ConsumingWorldObjectProvider(IEnumerable<WorldObject> objects, IEnumerable<string> consumedIds) : IMutableWorldObjectProvider
        {
            private readonly List<WorldObject> _objects = objects.ToList();
            private readonly HashSet<string> _consumed = consumedIds.ToHashSet();

            public IEnumerable<WorldObject> GetObjectsAt(string locationId)
                => _objects.Where(o => o.LocationId == locationId && !_consumed.Contains(o.Id));

            public IEnumerable<WorldObject> GetAllObjects() => _objects;

            public IEnumerable<WorldObject> GetAllObjectsAt(string locationId)
                => _objects.Where(o => o.LocationId == locationId)
                           .Select(o => _consumed.Contains(o.Id) ? o with { ConsumedAt = Now } : o);

            public void AddObject(WorldObject obj) => _objects.Add(obj);

            public WorldObject? FindObject(string objectId) => _objects.FirstOrDefault(o => o.Id == objectId);

            public bool ConsumeObject(string locationId, string objectId, WDateTime now) => _consumed.Add(objectId);

            public bool RestoreObject(string locationId, string objectId) => _consumed.Remove(objectId);

            public bool RemoveObject(string locationId, string objectId) => _objects.RemoveAll(o => o.Id == objectId) > 0;

            public bool SetHeldBy(string locationId, string objectId, HumanId? holder) => true;

            public IEnumerable<WorldObject> GetHeldBy(HumanId holder) => Array.Empty<WorldObject>();

            public IEnumerable<string> GetKnownLocationIds() => _objects.Select(o => o.LocationId).Distinct();
        }

        #endregion Stubs
    }
}
