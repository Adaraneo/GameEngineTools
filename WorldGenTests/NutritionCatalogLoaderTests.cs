// NutritionCatalogLoaderTests.cs
// Copyright (c) 50PSoftware

using GameEngineTools.World.Data;
using GameEngineTools.World.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorldGenTests;

[TestClass]
public class NutritionCatalogLoaderTests
{
    [TestMethod]
    public void Load_EmbeddedDefaultCatalog_ParsesWithoutError()
    {
        var catalog = NutritionCatalogLoader.Load();

        Assert.IsTrue(catalog.Count > 0);
    }

    [TestMethod]
    public void Load_EmbeddedDefaultCatalog_CoversHungerThirstRest_ForForestMountainAndAny()
    {
        var catalog = NutritionCatalogLoader.Load();

        foreach (var biome in new[] { "Forest", "Mountain" })
        {
            foreach (var need in new[] { AffordanceType.Hunger, AffordanceType.Thirst, AffordanceType.Rest })
            {
                var hasMatch = catalog.Any(t => t.AffordanceType == need &&
                    (string.Equals(t.Biome, biome, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(t.Biome, "Any", StringComparison.OrdinalIgnoreCase)));
                Assert.IsTrue(hasMatch, $"No catalog template covers {need} for biome '{biome}' (directly or via Any).");
            }
        }
    }

    [TestMethod]
    public void Parse_ValidCsv_ReturnsExpectedTemplate()
    {
        const string csv = """
            TemplateId;DisplayName;Biome;AffordanceType;Satisfaction;ItemKind;WeightGrams;RespawnMinutes;Pickable;CalorieGain;ProteinGain;IronGain;VitaminDGain;HydrationGain;HemeIronFraction;VitaminCMilligrams
            test_food;Test Food;Forest;Hunger;0.5;Food;100;600;true;20;5;2;;10;;
            """;

        var templates = NutritionCatalogLoader.Parse(csv);

        Assert.AreEqual(1, templates.Count);
        var t = templates[0];
        Assert.AreEqual("test_food", t.TemplateId);
        Assert.AreEqual("Test Food", t.DisplayName);
        Assert.AreEqual("Forest", t.Biome);
        Assert.AreEqual(AffordanceType.Hunger, t.AffordanceType);
        Assert.AreEqual(0.5, t.Satisfaction, 1e-9);
        Assert.AreEqual(PickupItemKind.Food, t.ItemKind);
        Assert.AreEqual(100, t.WeightGrams);
        Assert.AreEqual(600, t.RespawnMinutes);
        Assert.IsTrue(t.Pickable);
        Assert.AreEqual(20.0, t.CalorieGain);
        Assert.AreEqual(5.0, t.ProteinGain);
        Assert.AreEqual(2.0, t.IronGain);
        Assert.IsNull(t.VitaminDGain);
        Assert.AreEqual(10.0, t.HydrationGain);
        Assert.IsNull(t.HemeIronFraction);
        Assert.IsNull(t.VitaminCMilligrams);
    }

    [TestMethod]
    public void Parse_SkipsCommentAndBlankLines()
    {
        const string csv = """
            TemplateId;DisplayName;Biome;AffordanceType;Satisfaction;ItemKind;WeightGrams;RespawnMinutes;Pickable;CalorieGain;ProteinGain;IronGain;VitaminDGain;HydrationGain;HemeIronFraction;VitaminCMilligrams
            # this is a comment

            a;A;Any;Rest;0.25;None;0;0;false;;;;;;;
            """;

        var templates = NutritionCatalogLoader.Parse(csv);

        Assert.AreEqual(1, templates.Count);
    }

    [TestMethod]
    public void Parse_DuplicateTemplateId_Throws()
    {
        const string csv = """
            TemplateId;DisplayName;Biome;AffordanceType;Satisfaction;ItemKind;WeightGrams;RespawnMinutes;Pickable;CalorieGain;ProteinGain;IronGain;VitaminDGain;HydrationGain;HemeIronFraction;VitaminCMilligrams
            dup;A;Any;Rest;0.25;None;0;0;false;;;;;;;
            dup;B;Any;Rest;0.25;None;0;0;false;;;;;;;
            """;

        Assert.Throws<FormatException>(() => NutritionCatalogLoader.Parse(csv));
    }

    [TestMethod]
    public void Parse_WrongColumnCount_Throws()
    {
        const string csv = """
            TemplateId;DisplayName;Biome;AffordanceType;Satisfaction;ItemKind;WeightGrams;RespawnMinutes;Pickable;CalorieGain;ProteinGain;IronGain;VitaminDGain;HydrationGain;HemeIronFraction;VitaminCMilligrams
            bad;Missing Columns;Any;Rest;0.25;None
            """;

        Assert.Throws<FormatException>(() => NutritionCatalogLoader.Parse(csv));
    }

    [TestMethod]
    public void Load_DiskOverride_TakesPrecedenceOverEmbedded()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"worldgen_test_nutrition_{Guid.NewGuid():N}.csv");
        File.WriteAllText(tempPath, """
            TemplateId;DisplayName;Biome;AffordanceType;Satisfaction;ItemKind;WeightGrams;RespawnMinutes;Pickable;CalorieGain;ProteinGain;IronGain;VitaminDGain;HydrationGain;HemeIronFraction;VitaminCMilligrams
            only_one;Only One;Any;Rest;0.25;None;0;0;false;;;;;;;
            """);

        try
        {
            var catalog = NutritionCatalogLoader.Load(tempPath);
            Assert.AreEqual(1, catalog.Count);
            Assert.AreEqual("only_one", catalog[0].TemplateId);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
