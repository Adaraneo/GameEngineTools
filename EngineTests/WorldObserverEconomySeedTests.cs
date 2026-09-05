// WorldObserverEconomySeedTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Objects;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// End-to-end validation that the WorldObserver seed makes the bakery a priced bread shop once the
    /// food-economy Tier 2 schema (Price/ShopId columns) is applied. Loads the real embedded
    /// <c>schema.sql</c> plus the on-disk WorldObserver <c>seed_data.sql</c> into an in-memory database —
    /// the same pair the WorldObserver hosted service seeds at startup.
    /// </summary>
    [TestClass]
    public sealed class WorldObserverEconomySeedTests
    {
        [TestMethod]
        public void WorldObserverSeed_MakesBakeryAPricedBreadShop()
        {
            var seedPath = ResolveWorldObserverSeed();
            if (seedPath is null)
            {
                Assert.Inconclusive("WorldObserver seed_data.sql not found above the test output path — skipping.");
                return;
            }

            using var db = new SqliteWorldDatabase(":memory:");
            db.ExecuteScript(SqlScriptLoader.Load("schema.sql"));

            // SocialNorms no longer live in seed_data.sql — WorldDatabaseSeeder.Initialize inserts
            // them from SocialNorms.csv (SocialNormCatalogLoader) first, since Locations.NormId
            // (e.g. 'norm_formal_work') is a foreign key into SocialNorms. Mirror that order here
            // using WorldObserver's own disk-override CSV, sitting next to its seed_data.sql.
            var normsPath = Path.Combine(Path.GetDirectoryName(seedPath)!, "..", "SocialNorms.csv");
            if (File.Exists(normsPath))
            {
                foreach (var norm in SocialNormCatalogLoader.Load(normsPath))
                    db.InsertSocialNorm(norm);
            }

            db.ExecuteScript(File.ReadAllText(seedPath));

            var provider = new SqliteWorldObjectProvider(db);
            var bakeryFood = provider.GetObjectsAt("bakery")
                                     .Where(o => o.Category == WorldObjectCategory.Food)
                                     .ToList();

            Assert.IsTrue(bakeryFood.Count > 0, "The bakery should stock ready food.");
            Assert.IsTrue(
                bakeryFood.All(o => o.Price == 2.0 && o.ShopId == "bakery_shop" && o.ItemKind == PickupItemKind.Bread),
                "Every ready food at the bakery must be priced shop stock (Price 2.0, ShopId bakery_shop, kind Bread).");
            Assert.IsFalse(
                bakeryFood.Any(o => o.Price is null),
                "There must be no free food at the bakery — scarcity is what makes the shop meaningful.");
        }

        /// <summary>Walks up from the test output directory to find the WorldObserver seed script.</summary>
        private static string? ResolveWorldObserverSeed()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "WorldObserver", "SourceFiles", "World", "SQL", "seed_data.sql");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
