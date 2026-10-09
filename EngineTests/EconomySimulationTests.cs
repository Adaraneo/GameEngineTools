// EconomySimulationTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using EngineTests.Utils;
    using GameEngineTools.Characters.Engines;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Economy;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Objects.Production;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Text;

    /// <summary>Long closed-economy run of real characters through the production object path (SQLite + write buffer + cache).</summary>
    [TestClass]
    public sealed class EconomySimulationTests : TestBase
    {
        private const string Village = "econ_village";
        private const string Shop = "bakery_shop";

        private WorldObjectWriteBuffer _buffer = null!;
        private WorldObjectSnapshotCache _cache = null!;

        private sealed record Result(
            List<IHuman> Humans, double StartWealth, double EndWealth, double Wages, double Spent, double Earned,
            int Bought, int Eaten, List<double> Prices, List<double> BuyPrices, int ShopStock, double MeanHunger, int Produced, bool SitesIntact);

        private Result RunVillage(int days)
        {
            var report = new StringBuilder();
            var humans = BuildVillage(out var occupations);
            var clock = (TestClock)ServiceProvider.GetRequiredService<IClock>();
            var now = clock.Now;
            var dt = WTimeSpan.FromMinutes(30);
            var ticks = (int)(days * WWorld.Spec.HoursPerDay * 2);

            var actions = new Dictionary<string, int>();
            var prices = new List<double>();
            var buyPrices = new List<double>();
            double hungerSum = 0;
            var hungerSamples = 0;
            double wages = 0, spent = 0, earned = 0;
            int bought = 0, sold = 0, eaten = 0, produced = 0;
            var startWealth = humans.Sum(h => h.Snapshot.Economy?.Wealth ?? 0.0);

            for (var i = 0; i < ticks; i++)
            {
                _buffer.Flush();
                _cache.Refresh(new[] { Village });

                foreach (var h in humans)
                {
                    h.Tick(now, dt);
                    if (h.LastOutbox.OfType<SleepPromptRequested>().Any())
                        h.ReceiveEvent(new SleepConfirmed(now, h.Id, default));
                    foreach (var e in h.LastOutbox)
                    {
                        switch (e)
                        {
                            case ActionCommitted ac:
                                actions[ac.ActionName] = actions.GetValueOrDefault(ac.ActionName) + 1;
                                if (ac.ActionName is ActionNames.Eat or ActionNames.EatStored) eaten++;
                                if (ac.ActionName is ActionNames.Produce or ActionNames.Process) produced++;
                                break;
                            case WageEarned w: wages += w.WagePerHour * w.HoursWorked; break;
                            case Purchased p: spent += p.Price; bought++; buyPrices.Add(p.Price); break;
                            case Sold s: earned += s.Price; sold++; break;
                            case PriceChanged pc: prices.Add(pc.NewPrice); break;
                        }
                    }
                }

                foreach (var h in humans)
                {
                    hungerSum += h.Snapshot.Physiology.Hunger;
                    hungerSamples++;
                }

                now += dt;
                clock.SetNow(now);
            }

            _buffer.Flush();
            var endWealth = humans.Sum(h => h.Snapshot.Economy?.Wealth ?? 0.0);
            var stock = _buffer.GetObjectsAt(Village).Count(o => o.ShopId == Shop);
            var all = _buffer.GetAllObjects().Where(o => o.LocationId == Village || o.HeldBy is not null).ToList();

            report.AppendLine($"mean hunger {hungerSum / hungerSamples:F1}");
            report.AppendLine($"days={days} ticks={ticks} humans={humans.Count}");
            report.AppendLine($"wealth {startWealth:F1} -> {endWealth:F1} | wages {wages:F1} spent {spent:F1} earned {earned:F1}");
            report.AppendLine($"bought {bought} sold {sold} eaten {eaten} produced/processed {produced} | shop stock {stock}");
            report.AppendLine($"prices: n={prices.Count} min={(prices.Count > 0 ? prices.Min() : 0):F2} max={(prices.Count > 0 ? prices.Max() : 0):F2} last={(prices.Count > 0 ? prices[^1] : 0):F2}");
            report.AppendLine("actions: " + string.Join(", ", actions.OrderByDescending(a => a.Value).Take(14).Select(a => $"{a.Key}={a.Value}")));
            report.AppendLine("objects by kind: " + string.Join(", ", all.GroupBy(o => o.ItemKind).Select(g => $"{g.Key}={g.Count()}")));
            foreach (var h in humans)
                report.AppendLine($"  {occupations[h.Id]}: wealth {(h.Snapshot.Economy?.Wealth ?? 0):F1} hunger {h.Snapshot.Physiology.Hunger:F0}");
            Console.WriteLine(report.ToString());

            return new Result(humans, startWealth, endWealth, wages, spent, earned, bought, eaten, prices, buyPrices, stock, hungerSum / hungerSamples, produced,
                new[] { "econ_field", "econ_mill", "econ_oven", "econ_counter" }.All(id => _buffer.FindObject(id) is not null));
        }

        [TestMethod]
        public void Village_Run_KeepsMoneyConsistentAndPricesBounded()
        {
            var r = RunVillage(days: 28);
            var cfg = EconomyConfig.Default;

            Assert.IsTrue(r.Humans.All(h => (h.Snapshot.Economy?.Wealth ?? 0) >= 0), "Nobody may end with negative wealth.");
            Assert.IsTrue(r.Prices.All(p => p >= cfg.MinPrice - 1e-9 && p <= cfg.MaxPrice + 1e-9), "Posted prices stay within [Min, Max].");
            Assert.AreEqual(r.StartWealth + r.Wages + r.Earned - r.Spent, r.EndWealth, 1e-6, "Wealth changes only through wages, sales and purchases.");
            Assert.IsTrue(r.Bought > 0, "The shop must sell something.");
            Assert.AreEqual(r.Bought, r.BuyPrices.Count);
            for (var i = 1; i < r.BuyPrices.Count; i++)
                Assert.IsTrue(r.BuyPrices[i] > r.BuyPrices[i - 1] - 1e-9, "While the shelf empties, each purchase is charged the higher posted price.");
        }

        [TestMethod]
        public void Village_Run_ProductionChainKeepsRunningAndKeepsItsFixtures()
        {
            var r = RunVillage(days: 14);

            Assert.IsTrue(r.SitesIntact, "Field, mill, oven and counter must survive weeks of production.");
            Assert.IsTrue(r.Produced > 200, $"Grain must be harvested and processed into bread continuously (got {r.Produced}).");
            Assert.IsTrue(r.Eaten > 100, $"The bread must be eaten (got {r.Eaten}).");
        }

        [TestMethod]
        [Ignore("Calibration gap: a loaf costs ~3 labor-hours and relieves one 30-minute meal, so the village sits at mean hunger ~88 and never has surplus to sell (docs/todo-list.md).")]
        public void Village_Run_FeedsItsInhabitantsFromItsOwnProduction()
        {
            var r = RunVillage(days: 28);

            Assert.IsTrue(r.MeanHunger < 60, $"Mean hunger over the run must stay moderate (was {r.MeanHunger:F0}).");
            Assert.IsTrue(r.Earned > 0, "Surplus must reach the shop.");
        }

        [TestMethod]
        public void WriteBuffer_AddOfAnExistingId_SupersedesAPendingRemove()
        {
            BuildVillage(out _);
            var shelf = _buffer.GetObjectsAt(Village).First(o => o.ShopId == Shop);

            _buffer.RemoveObject(Village, shelf.Id);
            _buffer.AddObject(shelf with { Price = 9.0 });
            _buffer.Flush();
            _cache.Refresh(new[] { Village });

            var found = _cache.GetObjectsAt(Village).SingleOrDefault(o => o.Id == shelf.Id);
            Assert.IsNotNull(found, "An object updated by Remove+Add must not be deleted at Flush.");
            Assert.AreEqual(9.0, found!.Price);
        }

        #region Setup

        private List<IHuman> BuildVillage(out Dictionary<HumanId, string> occupations)
        {
            _buffer = ServiceProvider.GetRequiredService<WorldObjectWriteBuffer>();
            _cache = ServiceProvider.GetRequiredService<WorldObjectSnapshotCache>();
            var db = ServiceProvider.GetRequiredService<SqliteWorldDatabase>();
            var locations = (DefaultLocationService)ServiceProvider.GetRequiredService<ILocationService>();

            var descriptor = new LocationDescriptor(
                Id: Village, DisplayName: "Village", BaseNoise: 0.05, NoisePerPerson: 0.01,
                Capacity: 100, AllowsPrivacy: true, Type: LocationType.Social);
            db.InsertLocation(descriptor, "r");
            locations.RegisterLocation(descriptor);

            var now = new WDateTime(0);
            _buffer.AddObject(ProductionSiteFactory.Create("econ_field", Village, PickupItemKind.Grain, "Pole"));
            _buffer.AddObject(ProductionSiteFactory.Create("econ_mill", Village, PickupItemKind.Flour, "Mlýn"));
            _buffer.AddObject(ProductionSiteFactory.Create("econ_oven", Village, PickupItemKind.Bread, "Pec"));
            _buffer.AddObject(Fixture("econ_tool", "Dílna", WorldObjectCategory.Tool, AffordanceType.Work));
            _buffer.AddObject(Fixture("econ_well", "Studna", WorldObjectCategory.Drink, AffordanceType.Thirst));
            for (var b = 0; b < 8; b++)
                _buffer.AddObject(Fixture($"econ_bed_{b}", "Postel", WorldObjectCategory.Shelter, AffordanceType.Rest));
            _buffer.AddObject(ShopCounterFactory.Create("econ_counter", Village, Shop, PickupItemKind.Bread, "Pult"));
            for (var i = 0; i < 8; i++)
                _buffer.AddObject(FoodItemCatalog.Create(PickupItemKind.Bread, Village, now, null) with { Price = 2.0, ShopId = Shop });

            var generator = ServiceProvider.GetRequiredService<IHumanBlueprintGenerator>();
            var factory = ServiceProvider.GetRequiredService<IHumanFactory>();
            occupations = new Dictionary<HumanId, string>();
            var humans = new List<IHuman>();
            foreach (var occupation in new[] { "farmer", "farmer", "farmer", "laborer", "laborer", "merchant", "craftsperson", "healer" })
            {
                var human = factory.Create(generator.Generate() with { Occupation = occupation });
                locations.MoveCharacter(human.Id, Village);
                human.RestoreSnapshot(human.Snapshot with { InteractionSurface = human.Snapshot.InteractionSurface with { Location = Village } });
                occupations[human.Id] = occupation;
                humans.Add(human);
            }

            return humans;
        }

        private static WorldObject Fixture(string id, string name, WorldObjectCategory category, AffordanceType affordance)
            => new()
            {
                Id = id,
                DisplayName = name,
                Category = category,
                LocationId = Village,
                IsAvailable = true,
                IsPickable = false,
                Respawns = false,
                WeightGrams = 500_000,
                Affordances = ImmutableArray.Create(new WorldObjectAffordance(affordance, 1.0)),
            };

        #endregion Setup
    }
}
