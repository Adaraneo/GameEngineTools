// ObjectLocationFactTests.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Engines.Memory;
using GameEngineTools.World.Objects;
using GameEngineTools.World.Utils.Time;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EngineTests;

[TestClass]
public class ObjectLocationFactTests : TestBase
{
    [TestMethod]
    public void KnownObjects_EmptyByDefault()
    {
        var idx = new MemoryIndex(Array.Empty<EpisodicMemory>());
        Assert.AreEqual(0, idx.KnownObjects.Count);
    }

    [TestMethod]
    public void ObjectLocationFact_ConfidenceInRange()
    {
        var fact = new ObjectLocationFact("herb_01", "herb_garden", new WDateTime(10000), 0.85, PickupItemKind.Herb);
        Assert.IsTrue(fact.Confidence >= 0.0 && fact.Confidence <= 1.0);
    }

    [TestMethod]
    public void ObjectLocationFact_StoresAllFields()
    {
        var at = new WDateTime(99999);
        var fact = new ObjectLocationFact("mushroom_01", "forest_clearing", at, 0.7, PickupItemKind.Food);

        Assert.AreEqual("mushroom_01",    fact.ObjectId);
        Assert.AreEqual("forest_clearing", fact.LocationId);
        Assert.AreEqual(at,               fact.SeenAt);
        Assert.AreEqual(0.7,              fact.Confidence, 0.001);
        Assert.AreEqual(PickupItemKind.Food, fact.ItemKind);
    }

    [TestMethod]
    public void MemoryIndex_WithKnownObjects_RetainsCount()
    {
        var facts = new[]
        {
            new ObjectLocationFact("herb_01", "herb_garden", new WDateTime(1000), 0.9, PickupItemKind.Herb),
            new ObjectLocationFact("mushroom_01", "forest_clearing", new WDateTime(2000), 0.6, PickupItemKind.Food)
        };

        var idx = new MemoryIndex(Array.Empty<EpisodicMemory>()) { KnownObjects = facts };
        Assert.AreEqual(2, idx.KnownObjects.Count);
    }
}
