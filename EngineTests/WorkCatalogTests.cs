// WorkCatalogTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Objects.Production;
    using GameEngineTools.World.Work;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.IO;
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

        [TestMethod]
        public void TheSourceFilesCopies_AreIdenticalToTheBuiltInFiles_SoNothingIsShadowedWithAStaleVersion()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "GameEngineTools", "World", "Data", "Work")))
                root = root.Parent;
            if (root is null)
                Assert.Inconclusive("The repository folders were not found next to the test binaries.");

            foreach (var file in new[] { "materials.json", "objecttypes.json", "tasks.json" })
            {
                var builtIn = File.ReadAllText(Path.Combine(root.FullName, "GameEngineTools", "World", "Data", "Work", file)).Replace("\r\n", "\n");
                foreach (var project in new[] { "GameSandbox", "WorldObserver" })
                {
                    var copy = Path.Combine(root.FullName, project, "SourceFiles", "World", file);
                    Assert.IsTrue(File.Exists(copy), $"{project}/SourceFiles/World/{file} is missing; copy it from GameEngineTools/World/Data/Work.");
                    Assert.AreEqual(builtIn, File.ReadAllText(copy).Replace("\r\n", "\n"), $"{project}/SourceFiles/World/{file} differs from the built-in file; keep them identical.");
                }
            }
        }
        [TestMethod]
        public void TheRecipesFromTheData_AreExactlyTheOnesThatWereInTheCode()
        {
            var flour = RecipeRegistry.FindByOutput(PickupItemKind.Flour)!;
            Assert.AreEqual("recipe_flour_from_grain", flour.Id, "The ids of the old code table are kept.");
            Assert.AreEqual("Mlít obilí", flour.DisplayName);
            Assert.AreEqual("Mouka", flour.OutputDisplayName);
            Assert.AreEqual(45, flour.DurationMinutes);
            Assert.AreEqual("mill", flour.RequiredLocationType);
            Assert.AreEqual(new RecipeInput(PickupItemKind.Grain, 2), flour.Inputs.Single());

            var bread = RecipeRegistry.FindByOutput(PickupItemKind.Bread)!;
            Assert.AreEqual((90, "bakery", "Čerstvý chléb"), (bread.DurationMinutes, bread.RequiredLocationType, bread.OutputDisplayName));
            Assert.AreEqual(new RecipeInput(PickupItemKind.Flour, 1), bread.Inputs.Single());

            var cheese = RecipeRegistry.FindByOutput(PickupItemKind.Cheese)!;
            Assert.AreEqual((180, "dairy", "Čerstvý sýr"), (cheese.DurationMinutes, cheese.RequiredLocationType, cheese.OutputDisplayName));
            Assert.AreEqual(new RecipeInput(PickupItemKind.Milk, 2), cheese.Inputs.Single());
            Assert.AreEqual(3, RecipeRegistry.All.Count());
        }

        [TestMethod]
        public void TheFieldTasks_NameTheirActionAndToolClass()
        {
            var catalog = WorkCatalogLoader.Load();

            Assert.AreEqual(ToolClasses.Dig, catalog.FieldTaskFor("Plough")!.ToolClass);
            Assert.IsNull(catalog.FieldTaskFor("Sow")!.ToolClass, "Sowing needs no tool.");
            Assert.AreEqual(ToolClasses.CutGrain, catalog.FieldTaskFor("Reap")!.ToolClass);
            Assert.AreEqual(ToolClasses.Thresh, catalog.FieldTaskFor("Thresh")!.ToolClass);
            Assert.IsNull(catalog.FieldTaskFor("Dance"));
            foreach (var task in catalog.Tasks.Where(t => t.ToolClass is not null))
                Assert.IsTrue(catalog.Types.Any(type => type.ToolClasses.ContainsKey(task.ToolClass!)), $"Task '{task.Id}' needs the tool class '{task.ToolClass}' but no object type serves as one.");
        }

        [TestMethod]
        public void BadTasks_AreRejected_NamingTheTask()
        {
            const string types = """{ "types": [] }""";
            var noOutput = Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse(Materials, types,
                """{ "tasks": [ { "id": "t", "kind": "Craft", "inputs": [ { "item": "Grain", "qty": 1 } ], "durationMinutes": 5, "location": "mill" } ] }"""));
            StringAssert.Contains(noOutput.Message, "'t'");
            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse(Materials, types, """{ "tasks": [ { "id": "t", "kind": "Teleport" } ] }"""));
            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse(Materials, types, """{ "tasks": [ { "id": "f", "kind": "FieldWork" } ] }"""));
            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse(Materials, types,
                """{ "tasks": [ { "id": "a", "kind": "FieldWork", "action": "Plough" }, { "id": "a", "kind": "FieldWork", "action": "Sow" } ] }"""));

            var catalog = WorkCatalogLoader.Parse(Materials, types,
                """{ "tasks": [ { "id": "pot", "kind": "Craft", "inputs": [ { "item": "Clay", "qty": 2 } ], "output": { "item": "Bread", "qty": 1 }, "durationMinutes": 30, "location": "kiln" } ] }""");
            Assert.AreEqual("kiln", catalog.Task("pot")!.Location);

            var unknownItem = Assert.ThrowsExactly<FormatException>(() => RecipeRegistry.FromCatalog(catalog));
            StringAssert.Contains(unknownItem.Message, "Clay");
        }
    }
}
