// FieldWorkTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.World.Objects.Crops;
    using GameEngineTools.World.Work;
    using System;
    using System.Collections.Generic;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>The jobs on a field: which one is possible when, and how sessions of labor move the field through the year.</summary>
    [TestClass]
    public sealed class FieldWorkTests : TestBase
    {
        private static readonly CropConfig Cfg = CropConfig.Default;
        private static readonly WDateTime T0 = new(0);

        // Summer: warm and still warming; autumn: 12 degC and cooling; winter: frozen.
        private static FieldSeason Summer(int year = 2025, int seed = 0) => new(0.1, 18.0, year, seed);
        private static FieldSeason Autumn(int year = 2025, int seed = 5000) => new(-0.2, 12.0, year, seed);
        private static FieldSeason Winter(int year = 2025) => new(0.0, -3.0, year, 0);

        // ── Which job, when ──────────────────────────────────────────────────────────

        [TestMethod]
        public void RipeFieldIsReaped_AndSheavesAreThreshed_InAnySeason()
        {
            var ripe = new FieldState(FieldStage.Ripe, 1.0);
            var sheaves = new FieldState(FieldStage.Reaped, 1.0, PendingUnits: 4000, ThreshHoursRemaining: 160.0);

            Assert.AreEqual(FieldTask.Reap, FieldWork.Available(ripe, Summer(), Cfg));
            Assert.AreEqual(FieldTask.Thresh, FieldWork.Available(sheaves, Winter(), Cfg), "Threshing is winter work.");
        }

        [TestMethod]
        public void PloughingNeedsWorkableGround()
        {
            var bare = new FieldState(FieldStage.Fallow, 1.0);

            Assert.AreEqual(FieldTask.Plough, FieldWork.Available(bare, Summer(), Cfg));
            Assert.IsNull(FieldWork.Available(bare, Winter(), Cfg), "Frozen ground cannot be worked.");
        }

        [TestMethod]
        public void SowingNeedsCoolingWeatherInTheBand_ASeedBin_AndRestedGround()
        {
            var ploughed = new FieldState(FieldStage.Ploughed, 1.0);

            Assert.AreEqual(FieldTask.Sow, FieldWork.Available(ploughed, Autumn(), Cfg));
            Assert.IsNull(FieldWork.Available(ploughed, Summer(seed: 5000), Cfg), "Warm and warming: not sowing weather.");
            Assert.IsNull(FieldWork.Available(ploughed, new FieldSeason(0.2, 12.0, 2025, 5000), Cfg), "Same temperature but the year is warming: spring, not autumn.");
            Assert.IsNull(FieldWork.Available(ploughed, new FieldSeason(-0.2, 5.0, 2025, 5000), Cfg), "Cooling but already too cold for the band.");
            Assert.AreEqual(FieldTask.Sow, FieldWork.Available(ploughed, Autumn(seed: 850), Cfg), "850 of 1000 units is the thinnest sowing accepted.");
            Assert.IsNull(FieldWork.Available(ploughed, Autumn(seed: 849), Cfg), "Less than that is not worth sowing.");
        }

        [TestMethod]
        public void AFieldRestsTheConfiguredYearsAfterAHarvest()
        {
            var reapedIn2025 = new FieldState(FieldStage.Fallow, 1.0, ReapedYear: 2025);

            Assert.IsNull(FieldWork.Available(reapedIn2025, Summer(2025), Cfg), "Same year: it rests.");
            Assert.AreEqual(FieldTask.Plough, FieldWork.Available(reapedIn2025, Summer(2026), Cfg), "The year after: back in use.");

            var continuous = new CropConfig { FallowYears = 0.0 };
            Assert.AreEqual(FieldTask.Plough, FieldWork.Available(reapedIn2025, Summer(2025), continuous), "With no fallow year it is ploughed at once.");
        }

        // ── Stores ───────────────────────────────────────────────────────────────────

        [TestMethod]
        public void SeedBin_IsFilledForTheFieldsSownInOneYear_NotForAllOfThem()
        {
            var provider = new CropCycleTests.MemoryProvider();
            provider.AddObject(new GameEngineTools.World.Objects.WorldObject { Id = "f1", DisplayName = "Pole", Category = GameEngineTools.World.Objects.WorldObjectCategory.Ambient, LocationId = "farm", Field = new FieldState(FieldStage.Fallow, 2.0) });
            provider.AddObject(new GameEngineTools.World.Objects.WorldObject { Id = "f2", DisplayName = "Pole", Category = GameEngineTools.World.Objects.WorldObjectCategory.Ambient, LocationId = "farm", Field = new FieldState(FieldStage.Fallow, 2.0) });

            GrainStores.Deposit(provider, "farm", 5000, Cfg);

            Assert.AreEqual(2200, provider.FindObject(GrainStores.SeedBinId("farm"))!.Quantity, "Two fields in a two-year rotation: seed for one (2 ha x 1000 units) plus the 10% margin.");
            Assert.AreEqual(2800, provider.FindObject(GrainStores.FoodStoreId("farm"))!.Quantity);
        }

        // ── Labor moves the field on ─────────────────────────────────────────────────

        [TestMethod]
        public void Ploughing_TakesTheLaborPerHectare_AcrossManySessions()
        {
            var field = new FieldState(FieldStage.Fallow, 1.0);

            for (var session = 1; session < 56; session++)
                field = FieldWork.Apply(FieldTask.Plough, field, 4.0, T0, 2025, Cfg).Field;

            Assert.AreEqual(FieldStage.Fallow, field.Stage, "55 sessions of 4 h are 220 h of the 224 needed.");
            Assert.AreEqual(220.0, field.WorkDoneHours, 1e-9);

            var last = FieldWork.Apply(FieldTask.Plough, field, 4.0, T0, 2025, Cfg);
            Assert.IsTrue(last.Completed);
            Assert.AreEqual(FieldStage.Ploughed, last.Field.Stage);
            Assert.AreEqual(0.0, last.Field.WorkDoneHours, 1e-9, "The next task starts from nothing.");
        }

        [TestMethod]
        public void Sowing_WithAShortBin_SowsThinnerAndTheCropIsSmaller()
        {
            var field = new FieldState(FieldStage.Ploughed, 1.0, WorkDoneHours: 28.0);

            var result = FieldWork.Apply(FieldTask.Sow, field, 4.0, T0, 2025, Cfg, seedInStore: 900);

            Assert.AreEqual(900, result.SeedConsumed);
            Assert.AreEqual(900, result.Field.SeedUnits, "The crop is counted from the seed that went in.");
        }

        [TestMethod]
        public void Sowing_ConsumesTheSeedWhenFinished()
        {
            var field = new FieldState(FieldStage.Ploughed, 1.0, WorkDoneHours: 28.0);

            var result = FieldWork.Apply(FieldTask.Sow, field, 4.0, T0, 2025, Cfg);

            Assert.IsTrue(result.Completed);
            Assert.AreEqual(FieldStage.Sown, result.Field.Stage);
            Assert.AreEqual(1000, result.SeedConsumed);
            Assert.AreEqual(1000, result.Field.SeedUnits);
        }

        private static (FieldState Field, int Sessions) ReapAll(FieldState ripe, Func<int, WDateTime> timeOfSession)
        {
            var field = ripe;
            var sessions = 0;
            while (field.Stage == FieldStage.Ripe && sessions < 100)
            {
                field = FieldWork.Apply(FieldTask.Reap, field, 4.0, timeOfSession(sessions), 2026, Cfg).Field;
                sessions++;
            }

            return (field, sessions);
        }

        [TestMethod]
        public void Reaping_CutsTheFieldSessionBySession_AndLeavesSheavesToThresh()
        {
            var ripe = new FieldState(FieldStage.Ripe, 1.0, T0, T0, 2800.0, SeedUnits: 1000);

            var (field, sessions) = ReapAll(ripe, _ => T0);

            Assert.AreEqual(40, sessions, "160 h in sessions of 4 h.");
            Assert.AreEqual(FieldStage.Reaped, field.Stage);
            Assert.AreEqual(5000, field.PendingUnits, 40, "Seed 1000 x ratio 5, all reaped the day it ripened (rounding per session).");
            Assert.AreEqual(160.0, field.ThreshHoursRemaining, 1e-9);
            Assert.AreEqual(2026, field.ReapedYear);
        }

        [TestMethod]
        public void Reaping_LosesWhatShatteredByTheDayOfEachSession_NotByTheLastDay()
        {
            var ripe = new FieldState(FieldStage.Ripe, 1.0, T0, T0, 2800.0, SeedUnits: 1000);

            // Sessions spread over 40 days: the early ones lose almost nothing, the late ones 0.5% a day.
            var spread = ReapAll(ripe, session => T0 + WTimeSpan.FromDays(session)).Field.PendingUnits;
            var allLate = ReapAll(ripe, _ => T0 + WTimeSpan.FromDays(40)).Field.PendingUnits;

            Assert.IsTrue(spread > allLate + 400, $"Spread over 40 days {spread} against everything at day 40 {allLate}.");
            Assert.AreEqual(5000 * (1.0 - 0.005 * 19.5), spread, 60, "On average the grain stood about 20 days.");
        }

        [TestMethod]
        public void Threshing_DeliversEveryUnitOfTheHarvestOverItsSessions()
        {
            var field = new FieldState(FieldStage.Reaped, 1.0, PendingUnits: 4999, ThreshHoursRemaining: 160.0, ReapedYear: 2026);
            var delivered = 0;
            var sessions = 0;

            while (field.Stage == FieldStage.Reaped)
            {
                var result = FieldWork.Apply(FieldTask.Thresh, field, 4.0, T0, 2026, Cfg);
                delivered += result.ThreshedUnits;
                field = result.Field;
                sessions++;
                Assert.IsTrue(sessions <= 40, "Threshing must finish in the labor it was given.");
            }

            Assert.AreEqual(40, sessions, "160 h in sessions of 4 h.");
            Assert.AreEqual(4999, delivered, "Rounding loses no grain.");
            Assert.AreEqual(FieldStage.Fallow, field.Stage);
            Assert.AreEqual(0, field.PendingUnits);
        }

        [TestMethod]
        public void AWholeHectareYear_NeedsAboutHalfAThousandHoursOfLabor()
        {
            var hours = FieldWork.RequiredHours(FieldTask.Plough, new FieldState(FieldStage.Fallow, 1.0), Cfg)
                      + FieldWork.RequiredHours(FieldTask.Sow, new FieldState(FieldStage.Fallow, 1.0), Cfg)
                      + FieldWork.RequiredHours(FieldTask.Reap, new FieldState(FieldStage.Fallow, 1.0), Cfg)
                      + FieldWork.RequiredHours(FieldTask.Thresh, new FieldState(FieldStage.Fallow, 1.0), Cfg);

            Assert.AreEqual(576.0, hours, 1e-9);
            Assert.IsTrue(hours * 0.4 is > 150 and < 250, "About 0.4 ha feeds a person on grain alone, so 150-250 h of grain work a person-year, as estimated from the sources.");
        }
        // The rules as they were written in code before they became the conditions of tasks.json, kept here as the reference the data must reproduce.
        private static FieldTask? LegacyAvailable(FieldState field, FieldSeason now, CropConfig cfg)
        {
            bool Rested() => field.ReapedYear is not { } reaped || now.Year >= reaped + (int)Math.Round(cfg.FallowYears);
            bool Workable() => now.TemperatureCelsius >= cfg.MinWorkTemperatureCelsius;

            switch (field.Stage)
            {
                case FieldStage.Ripe:
                    return FieldTask.Reap;
                case FieldStage.Reaped when field.PendingUnits > 0:
                    return FieldTask.Thresh;
                case FieldStage.Fallow:
                case FieldStage.Reaped:
                    return Workable() && Rested() ? FieldTask.Plough : null;
                case FieldStage.Ploughed:
                    return Workable() && CropMath.IsSowingWeather(now.TemperatureCelsius, now.TemperatureTrendPerDay, cfg) && Rested()
                        && now.SeedInStore >= CropMath.SeedUnits(field.AreaHectares, cfg) * cfg.MinSowingShare
                        ? FieldTask.Sow : null;
                default:
                    return null;
            }
        }

        [TestMethod]
        public void TheConditionsInTheData_GiveTheSameJobAsTheOldRules_OverAGridOfFieldsAndSeasons()
        {
            var catalog = WorkCatalogLoader.Load();
            var configs = new[] { Cfg, new CropConfig { FallowYears = 0.0, MinWorkTemperatureCelsius = 5.0, MinSowingShare = 0.5 }, new CropConfig { FallowYears = 2.0 } };
            var checkedCases = 0;

            foreach (var cfg in configs)
            foreach (var stage in Enum.GetValues<FieldStage>())
            foreach (var pending in new[] { 0, 7 })
            foreach (var reapedYear in new int?[] { null, 2024, 2025 })
            foreach (var year in new[] { 2024, 2025, 2026, 2027 })
            foreach (var temperature in new[] { -5.0, 1.9, 2.0, 4.0, 5.0, 9.0, 12.0, 16.0, 17.0, 25.0 })
            foreach (var trend in new[] { -0.3, -0.05, 0.0, 0.1 })
            foreach (var seed in new[] { 0, 400, 850, 900, 5000 })
            {
                var field = new FieldState(stage, 1.0) { PendingUnits = pending, ReapedYear = reapedYear };
                var season = new FieldSeason(trend, temperature, year, seed);

                Assert.AreEqual(LegacyAvailable(field, season, cfg), FieldWork.Available(field, season, cfg, catalog),
                    $"{stage}, pending {pending}, reaped {reapedYear}, year {year}, {temperature} degC, trend {trend}, seed {seed}");
                checkedCases++;
            }

            Assert.IsTrue(checkedCases > 50_000, $"{checkedCases} cases");
        }

        [TestMethod]
        public void AnotherJobCanBeAddedByData_WithoutTouchingTheCode()
        {
            var catalog = WorkCatalogLoader.Parse("""{ "materials": [] }""", """{ "types": [] }""", """
                { "tasks": [ { "id": "weed", "kind": "FieldWork", "action": "Thresh", "requires": [ "stage:Sown", "workable" ] } ] }
                """);
            var sown = new FieldState(FieldStage.Sown, 1.0);

            Assert.AreEqual(FieldTask.Thresh, FieldWork.Available(sown, Summer(), Cfg, catalog), "A row with its own conditions decides.");
            Assert.IsNull(FieldWork.Available(sown, Winter(), Cfg, catalog), "The condition 'workable' fails in a frozen season.");
            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse("""{ "materials": [] }""", """{ "types": [] }""",
                """{ "tasks": [ { "id": "x", "kind": "FieldWork", "action": "Plough", "requires": [ "moon_is_full" ] } ] }"""));
            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse("""{ "materials": [] }""", """{ "types": [] }""",
                """{ "tasks": [ { "id": "x", "kind": "FieldWork", "action": "Plough", "requires": [ "stage:Frozen" ] } ] }"""));
            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse("""{ "materials": [] }""", """{ "types": [] }""",
                """{ "tasks": [ { "id": "x", "kind": "FieldWork", "action": "Plough" } ] }"""));
        }
        [TestMethod]
        public void TheVocabularyIsStrict_NumbersEmptyNamesAndTyposAreRefused_AndTheFirstRowInTheFileDecides()
        {
            foreach (var bad in new[] { "stage:", "stage:5", "stage:Fallow|", "stage:|Fallow", "stage:fallow", "stage:Fallow|Frozen" })
                Assert.IsFalse(FieldConditions.IsKnown(bad), bad);
            Assert.IsTrue(FieldConditions.IsKnown("stage:Fallow|Reaped"));
            Assert.ThrowsExactly<ArgumentException>(() => FieldConditions.Holds("stage:5", new FieldState(FieldStage.Ripe, 1.0), Summer(), Cfg));

            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse("""{ "materials": [] }""", """{ "types": [] }""",
                """{ "tasks": [ { "id": "x", "kind": "FieldWork", "action": "Plow", "requires": [ "workable" ] } ] }"""), "An action that is no FieldTask is refused at load.");

            var catalog = WorkCatalogLoader.Parse("""{ "materials": [] }""", """{ "types": [] }""", """
                { "tasks": [ { "id": "first", "kind": "FieldWork", "action": "Reap", "requires": [ "workable" ] },
                             { "id": "second", "kind": "FieldWork", "action": "Thresh", "requires": [ "workable" ] } ] }
                """);
            Assert.AreEqual("first", catalog.Tasks[0].Id);
            Assert.AreEqual(FieldTask.Reap, FieldWork.Available(new FieldState(FieldStage.Sown, 1.0), Summer(), Cfg, catalog), "Both match; the file order decides.");
        }
    }
}
