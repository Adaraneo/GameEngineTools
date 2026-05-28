// SqliteSocialNormProviderTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.World.Data;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Tests for <see cref="SqliteSocialNormProvider"/> using an in-memory SQLite database.
    /// </summary>
    [TestClass]
    public class SqliteSocialNormProviderTests
    {
        #region Helpers

        private static SqliteWorldDatabase CreateDb()
        {
            var db = new SqliteWorldDatabase(":memory:");
            db.ExecuteScript(GameEngineTools.World.Data.WorldDatabaseSchema.CreateTables);
            return db;
        }

        private static void SeedNorms(SqliteWorldDatabase db)
        {
            db.InsertSocialNorm(new SocialNormRow(
                Id: "norm_funeral",
                DisplayName: "Funeral / Mourning",
                Kind: "RitualContext",
                Severity: 0.85,
                EnforcementProbability: 0.90,
                RelationalModel: null,
                CultureId: null,
                ValidFromYear: null,
                ValidToYear: null));

            db.InsertSocialNorm(new SocialNormRow(
                Id: "norm_formal_work",
                DisplayName: "Formal Workplace",
                Kind: "Authority",
                Severity: 0.55,
                EnforcementProbability: 0.70,
                RelationalModel: "AuthorityRanking",
                CultureId: null,
                ValidFromYear: null,
                ValidToYear: null));

            db.InsertSocialNorm(new SocialNormRow(
                Id: "norm_casual_social",
                DisplayName: "Casual Social Gathering",
                Kind: "PublicConduct",
                Severity: 0.20,
                EnforcementProbability: 0.40,
                RelationalModel: null,
                CultureId: null,
                ValidFromYear: null,
                ValidToYear: null));
        }

        #endregion Helpers

        #region Test 1 — known id returns correct context

        [TestMethod]
        public void SqliteSocialNormProvider_KnownId_ReturnsCorrectContext()
        {
            using var db = CreateDb();
            SeedNorms(db);
            var provider = new SqliteSocialNormProvider(db);

            var ctx = provider.GetNormContext("norm_funeral");

            Assert.IsNotNull(ctx, "norm_funeral must be found.");
            Assert.AreEqual(SocialNormKind.RitualContext, ctx!.Kind);
            Assert.AreEqual(0.85, ctx.Severity, delta: 0.001);
            Assert.AreEqual(0.90, ctx.EnforcementProbability, delta: 0.001);
            Assert.IsNull(ctx.RelationalModel);
        }

        #endregion Test 1 — known id returns correct context

        #region Test 2 — unknown id returns null

        [TestMethod]
        public void SqliteSocialNormProvider_UnknownId_ReturnsNull()
        {
            using var db = CreateDb();
            SeedNorms(db);
            var provider = new SqliteSocialNormProvider(db);

            var ctx = provider.GetNormContext("norm_does_not_exist");

            Assert.IsNull(ctx, "Unknown norm id must return null.");
        }

        #endregion Test 2 — unknown id returns null

        #region Test 3 — relational model is parsed correctly

        [TestMethod]
        public void SqliteSocialNormProvider_ParsesRelationalModel_Correctly()
        {
            using var db = CreateDb();
            SeedNorms(db);
            var provider = new SqliteSocialNormProvider(db);

            var ctx = provider.GetNormContext("norm_formal_work");

            Assert.IsNotNull(ctx, "norm_formal_work must be found.");
            Assert.AreEqual(RelationalModel.AuthorityRanking, ctx!.RelationalModel,
                "RelationalModel should be parsed from 'AuthorityRanking' string.");
        }

        #endregion Test 3 — relational model is parsed correctly

        #region Test 4 — all seeded norms load without error

        [TestMethod]
        public void SqliteSocialNormProvider_AllSeededNorms_LoadWithoutError()
        {
            using var db = CreateDb();
            SeedNorms(db);
            var provider = new SqliteSocialNormProvider(db);

            var funeral = provider.GetNormContext("norm_funeral");
            var work    = provider.GetNormContext("norm_formal_work");
            var casual  = provider.GetNormContext("norm_casual_social");

            Assert.IsNotNull(funeral,  "norm_funeral must load.");
            Assert.IsNotNull(work,     "norm_formal_work must load.");
            Assert.IsNotNull(casual,   "norm_casual_social must load.");

            Assert.AreEqual(SocialNormKind.PublicConduct, casual!.Kind);
            Assert.AreEqual(0.20, casual.Severity, delta: 0.001);
        }

        #endregion Test 4 — all seeded norms load without error
    }
}
