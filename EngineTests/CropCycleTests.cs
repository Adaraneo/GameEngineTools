// CropCycleTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Objects.Crops;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Configuration;
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

        // ── Independent of the planet's calendar and hemisphere ─────────────────────

        private static readonly WDateTime Epoch = new(0);

        // A temperate year on the world's own calendar: coldest at day 0, mean 11 degC, +-9; the southern twin is half a year later.
        private static double North(WDateTime t)
        {
            var year = WWorld.Spec.Calendar.DaysInYear(2025);
            var day = t.WorldTicks / (double)WWorld.Spec.TicksPerDay;
            return 11.0 - 9.0 * Math.Cos(2.0 * Math.PI * day / year);
        }

        private static double South(WDateTime t) => North(t + WTimeSpan.FromDays(WWorld.Spec.Calendar.DaysInYear(2025) / 2.0));

        [TestMethod]
        public void YearFactor_HasMeanOneAndTheConfiguredSpread_AndIsTheSameEveryRun()
        {
            var factors = Enumerable.Range(0, 4000).Select(i => CropMath.YearFactor("village_" + i % 40, 1000 + i / 40, Cfg)).ToList();
            var mean = factors.Average();
            var spread = Math.Sqrt(factors.Sum(f => (f - mean) * (f - mean)) / factors.Count) / mean;

            Assert.AreEqual(1.0, mean, 0.03);
            Assert.AreEqual(Cfg.YieldVariation, spread, 0.04);
            Assert.IsTrue(factors.All(f => f is >= 0.3 and <= 1.8));
            Assert.AreEqual(CropMath.YearFactor("village_3", 1005, Cfg), CropMath.YearFactor("village_3", 1005, Cfg));
            Assert.AreNotEqual(CropMath.YearFactor("village_3", 1005, Cfg), CropMath.YearFactor("village_3", 1006, Cfg), "Another year, another weather.");
        }

        [TestMethod]
        public void YearFactor_WithNoVariation_IsAlwaysOne()
        {
            var cfg = new CropConfig { YieldVariation = 0.0 };

            Assert.AreEqual(1.0, CropMath.YearFactor("v", 2026, cfg));
        }

        [TestMethod]
        public void SowingWeather_IsCoolingAirInTheBand_AnywhereOnTheYear()
        {
            Assert.IsTrue(CropMath.IsSowingWeather(12.0, -0.2, Cfg));
            Assert.IsFalse(CropMath.IsSowingWeather(12.0, 0.2, Cfg), "Spring: the same air, but warming.");
            Assert.IsFalse(CropMath.IsSowingWeather(20.0, -0.2, Cfg), "Cooling, but still too warm.");
            Assert.IsFalse(CropMath.IsSowingWeather(6.0, -0.2, Cfg), "Cooling, but already too cold.");
        }

        [TestMethod]
        public void Heat_ActsInAbsoluteTime_SoALongerDayBringsMoreOfIt()
        {
            var longDay = (double)WWorld.Spec.HoursPerDay;
            var oneWorldDay = CropMath.DegreeDaysBetween(Epoch, Epoch + WTimeSpan.FromDays(1), _ => 10.0, Cfg);

            Assert.AreEqual(10.0 * longDay / 24.0, oneWorldDay, 1e-9, "A day of 26 hours at 10 degC is 10 x 26/24 degree-days, not 10.");
        }

        [TestMethod]
        public void FoundedField_IsSownInTheLastSowingWeather_OnEitherHemisphere()
        {
            var year = (int)WWorld.Spec.Calendar.DaysInYear(2025);
            var seed = new FieldState(FieldStage.Sown, 2.0, SeedUnits: 2000);

            foreach (var climate in new Func<WDateTime, double>[] { North, South })
            {
                // Check a start at every season of the year: the crop is sown before the start and is not older than a year.
                for (var startDay = 10; startDay < year; startDay += 36)
                {
                    var now = Epoch + WTimeSpan.FromDays(startDay + year);
                    var found = CropMath.Found(seed, now, climate, Cfg);

                    Assert.IsTrue(found.Stage is FieldStage.Sown or FieldStage.Ripe, $"Start day {startDay}: {found.Stage}");
                    var sown = found.SownAt!.Value;
                    var age = WDateTime.Difference(now, sown).TotalDays;
                    Assert.IsTrue(age > 0 && age <= year, $"Start day {startDay}: sown {age:F0} days ago.");
                    var weatherAtSowing = CropMath.IsSowingWeather(climate(sown), (climate(sown) - climate(sown - WTimeSpan.FromDays(2))) / 2.0, Cfg);
                    Assert.IsTrue(weatherAtSowing, $"Start day {startDay}: it was sown in sowing weather.");
                }
            }
        }

        [TestMethod]
        public void FoundedField_RipensWhenTheHeatSinceSowingIsEnough_AndWaitsOtherwise()
        {
            var year = (int)WWorld.Spec.Calendar.DaysInYear(2025);
            var seed = new FieldState(FieldStage.Sown, 2.0, SeedUnits: 2000);

            var justAfterSowing = CropMath.Found(seed, Epoch + WTimeSpan.FromDays(year * 0.80 + year), North, Cfg);
            Assert.AreEqual(FieldStage.Sown, justAfterSowing.Stage, "Sown only weeks ago in autumn: still growing.");
            Assert.IsTrue(justAfterSowing.DegreeDays > 0 && justAfterSowing.DegreeDays < Cfg.DegreeDaysToMaturity);

            var lateSummer = CropMath.Found(seed, Epoch + WTimeSpan.FromDays(year * 0.45 + year), North, Cfg);
            Assert.IsTrue(lateSummer.Stage == FieldStage.Ripe || lateSummer.DegreeDays > 0.5 * Cfg.DegreeDaysToMaturity, "A year-old crop is ripe or close to it.");
        }

        [TestMethod]
        public void FoundedField_OnAPlanetWithoutSowingWeather_StartsFallow()
        {
            var seed = new FieldState(FieldStage.Sown, 2.0, SeedUnits: 2000);

            var found = CropMath.Found(seed, Epoch + WTimeSpan.FromDays(400), _ => 30.0, Cfg);

            Assert.AreEqual(FieldStage.Fallow, found.Stage, "A constantly hot planet has no cooling autumn to sow in.");
        }

        [TestMethod]
        public void Service_WorksOutFoundedFieldsAtTheFirstTick()
        {
            var provider = new MemoryProvider();
            provider.AddObject(Field(new FieldState(FieldStage.Sown, 1.0, SeedUnits: 1000)));
            var service = new CropGrowthService(provider, Options.Create(Cfg));

            service.Tick(Epoch + WTimeSpan.FromDays(400), 12.0, North);

            var field = provider.FindObject("field_1")!.Field!;
            Assert.IsNotNull(field.SownAt, "The service gave it a sowing date from the climate.");
            Assert.IsTrue(field.Stage is FieldStage.Sown or FieldStage.Ripe);
        }

        [TestMethod]
        public void ClimateBeforeTheEpoch_IsTheClimateOfTheSameDayInALaterYear()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile(System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.World.json"), optional: false).Build();
            var astro = config.GetSection("World:Astro").Get<GameEngineTools.World.Core.Astro.AstroConfig>()!;
            var universe = config.GetSection("World:Universe").Get<GameEngineTools.World.Core.Astro.UniverseConfig>()!;
            var computer = new GameEngineTools.World.Core.Astro.CelestialContextComputer(new GameEngineTools.World.Core.Astro.SunModel());
            var objects = (universe.ToStarPhysics(), universe.ToOrbitalElements(), universe.ToPlanetConfig());
            var year = WWorld.Spec.Calendar.DaysInYear(1);

            var before = computer.ComputeFor(Epoch - WTimeSpan.FromDays(40), astro, objects);
            var later = computer.ComputeFor(Epoch + WTimeSpan.FromDays(year - 40), astro, objects);

            Assert.AreEqual(later.SeasonFraction, before.SeasonFraction, 1e-6);
            Assert.AreEqual(later.BaseAmbientTempCelsius, before.BaseAmbientTempCelsius, 0.5, "The orbit is slightly eccentric, so a year later differs by a fraction of a degree.");
            Assert.IsNotNull(computer.ComputeWithTrend(Epoch, astro, objects), "The trend at the very first instant needs the day before it.");
        }

        [TestMethod]
        public void TemperatureTrend_FollowsTheSeasons_OnTheWorldsOwnPlanet()
        {
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddJsonFile(System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.World.json"), optional: false).Build();
            var astro = config.GetSection("World:Astro").Get<GameEngineTools.World.Core.Astro.AstroConfig>()!;
            var universe = config.GetSection("World:Universe").Get<GameEngineTools.World.Core.Astro.UniverseConfig>()!;
            var computer = new GameEngineTools.World.Core.Astro.CelestialContextComputer(new GameEngineTools.World.Core.Astro.SunModel());
            var objects = (universe.ToStarPhysics(), universe.ToOrbitalElements(), universe.ToPlanetConfig());
            var year = (int)WWorld.Spec.Calendar.DaysInYear(2025);

            double TrendAt(double seasonShare, double latitude)
                => computer.ComputeWithTrend(Epoch + WTimeSpan.FromDays(seasonShare * year), astro with { LatitudeDeg = latitude }, objects).TemperatureTrendPerDay;

            Assert.IsTrue(TrendAt(0.1, 50.0) > 0, "North, spring: warming.");
            Assert.IsTrue(TrendAt(0.6, 50.0) < 0, "North, autumn: cooling.");
            Assert.IsTrue(TrendAt(0.1, -50.0) < 0, "South, the same dates: cooling.");
            Assert.IsTrue(TrendAt(0.6, -50.0) > 0, "South: warming.");
        }

        // ── The clock over the world ─────────────────────────────────────────────────

        private static WorldObject Field(FieldState state)
            => new() { Id = "field_1", DisplayName = "Pole", Category = WorldObjectCategory.Ambient, LocationId = Loc, ItemKind = PickupItemKind.Grain, Field = state };

        private static WorldObject Store(int units)
            => new() { Id = "store_1", DisplayName = "Sypka", Category = WorldObjectCategory.Ambient, LocationId = Loc, ItemKind = PickupItemKind.Grain, Quantity = units, IsStock = true };

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

        internal sealed class MemoryProvider : IMutableWorldObjectProvider
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
