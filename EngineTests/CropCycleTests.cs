// CropCycleTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Objects.Crops;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;

    /// <summary>The seasonal grain cycle: stage rules, thermal time, yield, storage loss and persistence of stacks and field state.</summary>
    [TestClass]
    public sealed class CropCycleTests : TestBase
    {
        private static readonly CropConfig Cfg = CropConfig.Default;
        private const string Loc = "farm";

        // Earth-like year of 360 days: mean 11 degC, +-9 degC, coldest on day 0 (the shape of the world's Astro settings).
        private static double Temperature(double dayOfYear) => 11.0 - 9.0 * Math.Cos(2.0 * Math.PI * (dayOfYear % 360.0) / 360.0);

        private static FieldState Bare(double hectares = 1.0) => new(FieldStage.Fallow, hectares);

        // ── Arithmetic and stage rules ───────────────────────────────────────────────

        [TestMethod]
        public void SeedUnits_FollowAreaAndUnitSize()
        {
            Assert.AreEqual(1000, CropMath.SeedUnits(1.0, Cfg), "150 kg of seed per hectare in 150 g units.");
            Assert.AreEqual(2500, CropMath.SeedUnits(2.5, Cfg));

            var coarse = new CropConfig { GramsPerUnit = 1500.0 };
            Assert.AreEqual(100, CropMath.SeedUnits(1.0, coarse), "A bigger unit rescales every stock.");
        }

        [TestMethod]
        public void Stages_AllowOnlyTheNextStepOfTheCycle()
        {
            var now = new WDateTime(0);
            Assert.IsNull(CropMath.Sow(Bare(), now, Cfg), "Bare ground cannot be sown before it is ploughed.");
            Assert.IsNull(CropMath.Reap(Bare()), "Nothing to reap.");

            var ploughed = CropMath.Plough(Bare())!;
            Assert.AreEqual(FieldStage.Ploughed, ploughed.Stage);
            Assert.IsNull(CropMath.Plough(ploughed), "Ploughing twice is not a step.");

            var sown = CropMath.Sow(ploughed, now, Cfg)!;
            Assert.AreEqual(FieldStage.Sown, sown.Stage);
            Assert.AreEqual(1000, sown.SeedUnits);
            Assert.IsNull(CropMath.Reap(sown), "A crop that is not ripe cannot be reaped.");
            Assert.IsNull(CropMath.Plough(sown), "Ground with a crop in it cannot be ploughed.");

            var reaped = CropMath.Reap(sown with { Stage = FieldStage.Ripe })!;
            Assert.AreEqual(FieldStage.Reaped, reaped.Stage);
            Assert.AreEqual(FieldStage.Ploughed, CropMath.Plough(reaped)!.Stage, "Stubble is ploughed in for the next year.");
        }

        [TestMethod]
        public void DegreeDays_AreHeatBetweenBaseAndCeiling()
        {
            Assert.AreEqual(0.0, CropMath.DegreeDays(-5.0, 1.0, Cfg), "Frost adds no heat.");
            Assert.AreEqual(10.0, CropMath.DegreeDays(10.0, 1.0, Cfg), 1e-9);
            Assert.AreEqual(26.0, CropMath.DegreeDays(40.0, 1.0, Cfg), 1e-9, "More heat than the ceiling adds nothing.");
            Assert.AreEqual(50.0, CropMath.DegreeDays(10.0, 5.0, Cfg), 1e-9);
        }

        // ── The year ─────────────────────────────────────────────────────────────────

        private static int DaysToRipen(double sowingDayOfYear, Func<double, double> temperature, int limitDays)
        {
            var field = CropMath.Sow(CropMath.Plough(Bare())!, new WDateTime(0), Cfg)!;
            for (var day = 0; day < limitDays; day++)
            {
                field = CropMath.Grow(field, CropMath.DegreeDays(temperature(sowingDayOfYear + day), 1.0, Cfg), new WDateTime(0), Cfg);
                if (field.Stage == FieldStage.Ripe)
                    return day + 1;
            }

            return -1;
        }

        [TestMethod]
        public void AutumnSownWheat_RipensInSummerOfTheNextYear()
        {
            var days = DaysToRipen(270.0, Temperature, 720);

            var ripeDayOfYear = (270 + days) % 360;
            Assert.IsTrue(days > 250 && days < 330, $"Sown at the start of autumn it takes most of a year, got {days} days.");
            Assert.IsTrue(ripeDayOfYear is > 170 and < 230, $"Ripe in high summer (day {ripeDayOfYear} of 360), as winter wheat does in temperate Europe.");
        }

        [TestMethod]
        public void ColdClimate_NeverRipensWheatInAYear()
        {
            Assert.AreEqual(-1, DaysToRipen(270.0, d => Temperature(d) - 8.0, 360), "A mean of 3 degC gives about 1600 degree-days a year, not 2800.");
        }

        // ── Yield ────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Harvest_IsSeedTimesRatioTimesYear_LessWhatShatteredWhileStanding()
        {
            var t0 = new WDateTime(0);
            var ripe = new FieldState(FieldStage.Ripe, 1.0, t0, t0, 2800.0, SeedUnits: 1000, YearFactor: 1.0);

            Assert.AreEqual(5000, CropMath.HarvestUnits(ripe, t0, Cfg), "Ratio 5 on 1000 seed units, reaped the day it ripened.");
            Assert.AreEqual(2500, CropMath.HarvestUnits(ripe with { YearFactor = 0.5 }, t0, Cfg), "A bad year.");

            var tenDaysLater = t0 + WTimeSpan.FromDays(10);
            Assert.AreEqual(4750, CropMath.HarvestUnits(ripe, tenDaysLater, Cfg), "Ten days standing at 0.5% a day.");
            Assert.AreEqual(3500, CropMath.HarvestUnits(ripe, t0 + WTimeSpan.FromDays(60), Cfg), "Two months standing costs a third.");
            Assert.AreEqual(0, CropMath.HarvestUnits(ripe, t0 + WTimeSpan.FromDays(250), Cfg), "Left standing for most of a year, nothing is left.");
            Assert.AreEqual(0, CropMath.HarvestUnits(ripe with { Stage = FieldStage.Sown }, t0, Cfg), "Not ripe, nothing to reap.");
        }

        [TestMethod]
        public void StorageLoss_IsAFewPercentAYear_AndNeverCreatesGrain()
        {
            Assert.AreEqual(950, CropMath.AfterStorage(1000, 360.0, 360.0, Cfg), "5% over a year.");
            Assert.AreEqual(1000, CropMath.AfterStorage(1000, 0.0, 360.0, Cfg));
            Assert.AreEqual(3, CropMath.AfterStorage(3, 30.0, 360.0, Cfg), "A tiny stock does not shrink by rounding.");
        }

        // ── The clock over the world ─────────────────────────────────────────────────

        private static WorldObject Field(FieldState state)
            => new() { Id = "field_1", DisplayName = "Pole", Category = WorldObjectCategory.Ambient, LocationId = Loc, ItemKind = PickupItemKind.Grain, Field = state };

        private static WorldObject Store(int units)
            => new() { Id = "store_1", DisplayName = "Sypka", Category = WorldObjectCategory.Ambient, LocationId = Loc, ItemKind = PickupItemKind.Grain, Quantity = units };

        [TestMethod]
        public void Service_AddsHeatToSownFieldsOncePerHour_AndRipensThem()
        {
            var provider = new MemoryProvider();
            provider.AddObject(Field(new FieldState(FieldStage.Sown, 1.0, new WDateTime(0), null, 2790.0, 1000)));
            provider.AddObject(Field(new FieldState(FieldStage.Ploughed, 1.0)) with { Id = "field_2" });
            var service = new CropGrowthService(provider, Options.Create(Cfg));
            var start = new WDateTime(0);

            service.Tick(start, 10.0);
            service.Tick(start + WTimeSpan.FromMinutes(30), 10.0);
            Assert.AreEqual(2790.0, provider.FindObject("field_1")!.Field!.DegreeDays, 1e-9, "Less than an hour has passed: no growth yet.");

            service.Tick(start + WTimeSpan.FromDays(1), 10.0);

            var grown = provider.FindObject("field_1")!.Field!;
            Assert.AreEqual(FieldStage.Ripe, grown.Stage, "Ten degrees for a day added ten degree-days and crossed 2800.");
            Assert.IsNotNull(grown.RipeAt);
            Assert.AreEqual(FieldStage.Ploughed, provider.FindObject("field_2")!.Field!.Stage, "A field that is not sown does not grow.");
        }

        [TestMethod]
        public void Service_WearsDownStoredGrainAfterTheInterval_ButNotProductionFixtures()
        {
            var provider = new MemoryProvider();
            provider.AddObject(Store(10_000));
            var fixture = Store(10_000) with
            {
                Id = "mill_1",
                Affordances = ImmutableArray.Create(new WorldObjectAffordance(AffordanceType.Production, 1.0)),
            };
            provider.AddObject(fixture);
            var service = new CropGrowthService(provider, Options.Create(Cfg));
            var start = new WDateTime(0);

            service.Tick(start, 10.0);
            service.Tick(start + WTimeSpan.FromDays(10), 10.0);
            Assert.AreEqual(10_000, provider.FindObject("store_1")!.Quantity, "Not yet a month.");

            service.Tick(start + WTimeSpan.FromDays(31), 10.0);

            Assert.IsTrue(provider.FindObject("store_1")!.Quantity < 10_000, "A month in the store costs a little grain.");
            Assert.IsTrue(provider.FindObject("store_1")!.Quantity > 9_900, "A little, not a lot: 5% a year.");
            Assert.AreEqual(10_000, provider.FindObject("mill_1")!.Quantity, "A production fixture is not a store.");
        }

        // ── Persistence ──────────────────────────────────────────────────────────────

        private static SqliteWorldDatabase NewDb()
        {
            var db = new SqliteWorldDatabase(":memory:");
            WorldDatabaseSeeder.InitializeSchemaOnly(db);
            db.InsertLocation(new LocationDescriptor(Loc, Loc, 0.1, 0.01, 10, false, LocationType.Public), "Test");
            return db;
        }

        [TestMethod]
        public void Database_KeepsStackSizeAndFieldState_AcrossAReload()
        {
            using var db = NewDb();
            var sown = new FieldState(FieldStage.Sown, 2.5, new WDateTime(1000), new WDateTime(2000), 1234.5, 2500, 0.8);
            db.AddObject(Field(sown));
            db.AddObject(Store(4200));
            db.AddObject(new WorldObject { Id = "plain", DisplayName = "Plain", Category = WorldObjectCategory.Ambient, LocationId = Loc });

            var loaded = db.GetObjectsAt(Loc).ToDictionary(o => o.Id);

            Assert.AreEqual(sown, loaded["field_1"].Field);
            Assert.AreEqual(4200, loaded["store_1"].Quantity);
            Assert.AreEqual(1, loaded["plain"].Quantity, "Ordinary objects are single units.");
            Assert.IsNull(loaded["plain"].Field);
        }

        [TestMethod]
        public void Database_ReplacingAnObject_DropsStateItNoLongerHas()
        {
            using var db = NewDb();
            db.AddObject(Store(4200));
            db.AddObject(Store(1));
            db.AddObject(Field(new FieldState(FieldStage.Ploughed, 1.0)));
            db.AddObject(Field(new FieldState(FieldStage.Ploughed, 1.0)) with { Field = null });

            var loaded = db.GetObjectsAt(Loc).ToDictionary(o => o.Id);

            Assert.AreEqual(1, loaded["store_1"].Quantity, "Back to a single unit, no stale stack row.");
            Assert.IsNull(loaded["field_1"].Field, "No longer a field, no stale crop row.");
        }

        // ── Stubs ────────────────────────────────────────────────────────────────────

        private sealed class MemoryProvider : IMutableWorldObjectProvider
        {
            private readonly Dictionary<string, WorldObject> _objects = new();

            public IEnumerable<WorldObject> GetObjectsAt(string loc) => _objects.Values.Where(o => o.LocationId == loc && o.HeldBy is null);

            public IEnumerable<WorldObject> GetAllObjectsAt(string loc) => _objects.Values.Where(o => o.LocationId == loc);

            public IEnumerable<WorldObject> GetAllObjects() => _objects.Values.ToList();

            public void AddObject(WorldObject o) => _objects[o.Id] = o;

            public WorldObject? FindObject(string id) => _objects.TryGetValue(id, out var o) ? o : null;

            public bool RemoveObject(string loc, string id) => _objects.Remove(id);

            public bool ConsumeObject(string loc, string id, WDateTime now) => _objects.Remove(id);

            public bool RestoreObject(string loc, string id) => false;

            public bool SetHeldBy(string loc, string id, HumanId? holder) => true;

            public IEnumerable<WorldObject> GetHeldBy(HumanId holder) => Array.Empty<WorldObject>();

            public IEnumerable<string> GetKnownLocationIds() => _objects.Values.Select(o => o.LocationId).Distinct();
        }
    }
}
