// InventoryStateTests.cs
// Copyright (c) 50PSoftware

using Microsoft.VisualStudio.TestTools.UnitTesting;
using GameEngineTools.Characters.Engines.Inventory;
using GameEngineTools.World.Objects;
using System.Collections.Generic;

namespace EngineTests;

[TestClass]
public class InventoryStateTests
{
    [TestMethod]
    public void TotalWeightGrams_EmptyInventory_ReturnsZero()
    {
        Assert.AreEqual(0, InventoryState.Empty.TotalWeightGrams);
    }

    [TestMethod]
    public void TotalWeightGrams_MultipleSlots_SumsCorrectly()
    {
        var state = new InventoryState
        {
            Slots = new Dictionary<string, InventorySlot>
            {
                ["herb_01"] = new("herb_01", "Chamomile", PickupItemKind.Herb, 20, 3),
                ["key_01"]  = new("key_01",  "Old Key",   PickupItemKind.Key,  50, 1)
            }
        };

        // 3×20 + 1×50 = 110
        Assert.AreEqual(110, state.TotalWeightGrams);
    }
}
