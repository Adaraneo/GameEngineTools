// WorkCatalogTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.World.Work;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Linq;

    /// <summary>The materials and object types: loading, checking, and the weight that follows from density and volume.</summary>
    [TestClass]
    public sealed class WorkCatalogTests
    {
        private const string Materials = """
            { "materials": [
              { "id": "iron", "class": "Metal", "densityKgM3": 7870 },
              { "id": "ash",  "class": "Wood",  "densityKgM3": 690 } ] }
            """;

        [TestMethod]
        public void TheBuiltInCatalog_Loads_AndGivesToolsOrdinaryWeights()
        {
            var catalog = WorkCatalogLoader.Load();

            Assert.IsTrue(catalog.Materials.Count >= 8);
            var spade = catalog.WeightGrams("spade");
            var sickle = catalog.WeightGrams("sickle");
            var flail = catalog.WeightGrams("flail");
            Assert.IsTrue(spade is >= 1500 and <= 2500, $"spade {spade} g");
            Assert.IsTrue(sickle is >= 300 and <= 500, $"sickle {sickle} g");
            Assert.IsTrue(flail is >= 1000 and <= 1500, $"flail {flail} g");
            Assert.AreEqual(1.0, catalog.ToolQuality("spade", "dig"));
            Assert.AreEqual(0.0, catalog.ToolQuality("spade", "thresh"), "A spade does not thresh.");
        }

        [TestMethod]
        public void Weight_IsDensityTimesVolume_SummedOverTheParts()
        {
            var catalog = WorkCatalogLoader.Parse(Materials, """
                { "types": [ { "id": "t", "category": "Tool", "parts": [ { "material": "iron", "volumeCm3": 100 }, { "material": "ash", "volumeCm3": 1000 } ] } ] }
                """);

            // 7.87 g/cm3 x 100 + 0.69 g/cm3 x 1000 = 787 + 690
            Assert.AreEqual(1477, catalog.WeightGrams("t"));
            Assert.AreEqual(0, catalog.WeightGrams("missing"), "An unknown type weighs nothing instead of failing.");
            Assert.IsNull(catalog.Type(null));
        }

        [TestMethod]
        public void BadData_IsRejectedWithTheFileAndTheEntryNamed()
        {
            var unknownMaterial = Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse(Materials,
                """{ "types": [ { "id": "x", "parts": [ { "material": "gold", "volumeCm3": 1 } ] } ] }"""));
            StringAssert.Contains(unknownMaterial.Message, "'x'");
            StringAssert.Contains(unknownMaterial.Message, "gold");

            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse(
                """{ "materials": [ { "id": "a", "densityKgM3": 0 } ] }""", """{ "types": [] }"""));
            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse(
                """{ "materials": [ { "id": "a", "densityKgM3": 1 }, { "id": "a", "densityKgM3": 2 } ] }""", """{ "types": [] }"""));
            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse(Materials, """{ "types": [ { "id": "x" }, { "id": "x" } ] }"""));
            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse(Materials, """{ "types": [ { "id": "x", "category": "NoSuchCategory" } ] }"""));
            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse("{ not json", """{ "types": [] }"""));
        }

        [TestMethod]
        public void CommentsAndTrailingCommas_AreAllowedInTheFiles()
        {
            var catalog = WorkCatalogLoader.Parse("""
                // a comment
                { "materials": [ { "id": "iron", "densityKgM3": 7870, }, ], }
                """, """{ "types": [ { "id": "bar", "category": "Tool", "parts": [ { "material": "iron", "volumeCm3": 1000 } ], } ] }""");

            Assert.AreEqual(7870, catalog.WeightGrams("bar"));
            Assert.AreEqual(1, catalog.Types.Count(t => t.Id == "bar"));
        }
    }
}
