// SocialNormCatalogLoaderTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.IO;
    using System.Linq;
    using GameEngineTools.World.Data;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class SocialNormCatalogLoaderTests
    {
        [TestMethod]
        public void Load_EmbeddedDefaultCatalog_ContainsTheThreeDefaultNorms()
        {
            var rows = SocialNormCatalogLoader.Load();

            Assert.IsTrue(rows.Any(r => r.Id == "norm_funeral"));
            Assert.IsTrue(rows.Any(r => r.Id == "norm_formal_work"));
            Assert.IsTrue(rows.Any(r => r.Id == "norm_casual_social"));
        }

        [TestMethod]
        public void Parse_ValidCsv_ReturnsExpectedRow()
        {
            const string csv = """
                Id;DisplayName;Kind;Severity;EnforcementProbability;RelationalModel;CultureId;ValidFromYear;ValidToYear
                norm_test;Test Norm;Authority;0.5;0.6;AuthorityRanking;;;
                """;

            var rows = SocialNormCatalogLoader.Parse(csv);

            Assert.AreEqual(1, rows.Count);
            var r = rows[0];
            Assert.AreEqual("norm_test", r.Id);
            Assert.AreEqual("Test Norm", r.DisplayName);
            Assert.AreEqual("Authority", r.Kind);
            Assert.AreEqual(0.5, r.Severity, 1e-9);
            Assert.AreEqual(0.6, r.EnforcementProbability, 1e-9);
            Assert.AreEqual("AuthorityRanking", r.RelationalModel);
            Assert.IsNull(r.CultureId);
            Assert.IsNull(r.ValidFromYear);
            Assert.IsNull(r.ValidToYear);
        }

        [TestMethod]
        public void Parse_OptionalColumns_ParseWhenPresent()
        {
            const string csv = """
                Id;DisplayName;Kind;Severity;EnforcementProbability;RelationalModel;CultureId;ValidFromYear;ValidToYear
                norm_test;Test Norm;Honesty;0.2;0.3;;czech;1200;1500
                """;

            var rows = SocialNormCatalogLoader.Parse(csv);

            var r = rows[0];
            Assert.IsNull(r.RelationalModel);
            Assert.AreEqual("czech", r.CultureId);
            Assert.AreEqual(1200, r.ValidFromYear);
            Assert.AreEqual(1500, r.ValidToYear);
        }

        [TestMethod]
        public void Parse_SkipsCommentAndBlankLines()
        {
            const string csv = """
                Id;DisplayName;Kind;Severity;EnforcementProbability;RelationalModel;CultureId;ValidFromYear;ValidToYear
                # this is a comment

                norm_a;A;Greeting;0.1;0.2;;;;
                """;

            var rows = SocialNormCatalogLoader.Parse(csv);

            Assert.AreEqual(1, rows.Count);
        }

        [TestMethod]
        public void Parse_DuplicateId_Throws()
        {
            const string csv = """
                Id;DisplayName;Kind;Severity;EnforcementProbability;RelationalModel;CultureId;ValidFromYear;ValidToYear
                dup;A;Greeting;0.1;0.2;;;;
                dup;B;Greeting;0.1;0.2;;;;
                """;

            Assert.Throws<FormatException>(() => SocialNormCatalogLoader.Parse(csv));
        }

        [TestMethod]
        public void Parse_WrongColumnCount_Throws()
        {
            const string csv = """
                Id;DisplayName;Kind;Severity;EnforcementProbability;RelationalModel;CultureId;ValidFromYear;ValidToYear
                bad;Missing Columns;Greeting
                """;

            Assert.Throws<FormatException>(() => SocialNormCatalogLoader.Parse(csv));
        }

        [TestMethod]
        public void Parse_InvalidKind_Throws()
        {
            const string csv = """
                Id;DisplayName;Kind;Severity;EnforcementProbability;RelationalModel;CultureId;ValidFromYear;ValidToYear
                bad;Bad Kind;NotARealKind;0.1;0.2;;;;
                """;

            Assert.Throws<FormatException>(() => SocialNormCatalogLoader.Parse(csv));
        }

        [TestMethod]
        public void Parse_InvalidRelationalModel_Throws()
        {
            const string csv = """
                Id;DisplayName;Kind;Severity;EnforcementProbability;RelationalModel;CultureId;ValidFromYear;ValidToYear
                bad;Bad Model;Authority;0.1;0.2;NotARealModel;;;
                """;

            Assert.Throws<FormatException>(() => SocialNormCatalogLoader.Parse(csv));
        }

        [TestMethod]
        public void Load_DiskOverride_TakesPrecedenceOverEmbedded()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"enginetests_socialnorms_{Guid.NewGuid():N}.csv");
            File.WriteAllText(tempPath, """
                Id;DisplayName;Kind;Severity;EnforcementProbability;RelationalModel;CultureId;ValidFromYear;ValidToYear
                only_one;Only One;Greeting;0.1;0.2;;;;
                """);

            try
            {
                var rows = SocialNormCatalogLoader.Load(tempPath);
                Assert.AreEqual(1, rows.Count);
                Assert.AreEqual("only_one", rows[0].Id);
            }
            finally
            {
                File.Delete(tempPath);
            }
        }
    }
}
