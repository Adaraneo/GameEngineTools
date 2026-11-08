// FieldWorkTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.World.Objects.Crops;
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
            Assert.IsNull(FieldWork.Available(ploughed, Autumn(seed: 999), Cfg), "A hectare needs 1000 units of seed and the bin holds 999.");
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

            Assert.AreEqual(2000, provider.FindObject(GrainStores.SeedBinId("farm"))!.Quantity, "Two fields in a two-year rotation: seed for one (2 ha x 1000 units).");
            Assert.AreEqual(3000, provider.FindObject(GrainStores.FoodStoreId("farm"))!.Quantity);
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
        public void Sowing_ConsumesTheSeedWhenFinished()
        {
            var field = new FieldState(FieldStage.Ploughed, 1.0, WorkDoneHours: 28.0);

            var result = FieldWork.Apply(FieldTask.Sow, field, 4.0, T0, 2025, Cfg);

            Assert.IsTrue(result.Completed);
            Assert.AreEqual(FieldStage.Sown, result.Field.Stage);
            Assert.AreEqual(1000, result.SeedConsumed);
            Assert.AreEqual(1000, result.Field.SeedUnits);
        }

        [TestMethod]
        public void Reaping_FixesTheHarvest_AndLeavesSheavesToThresh()
        {
            var t0 = T0;
            var ripe = new FieldState(FieldStage.Ripe, 1.0, t0, t0, 2800.0, SeedUnits: 1000, WorkDoneHours: 158.0);

            var result = FieldWork.Apply(FieldTask.Reap, ripe, 4.0, t0, 2026, Cfg);

            Assert.IsTrue(result.Completed);
            Assert.AreEqual(FieldStage.Reaped, result.Field.Stage);
            Assert.AreEqual(5000, result.Field.PendingUnits, "Seed 1000 x ratio 5, reaped the day it ripened.");
            Assert.AreEqual(160.0, result.Field.ThreshHoursRemaining, 1e-9);
            Assert.AreEqual(2026, result.Field.ReapedYear);
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
    }
}
