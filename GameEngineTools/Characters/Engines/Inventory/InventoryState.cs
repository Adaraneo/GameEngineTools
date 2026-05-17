// InventoryState.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Inventory;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameEngineTools.World.Objects;

/// <summary>
/// Immutable state of a character's inventory at a single point in time.
/// </summary>
public sealed record InventoryState
{
    /// <summary>Empty inventory with no items.</summary>
    public static readonly InventoryState Empty = new();

    /// <summary>
    /// Items carried by the character, keyed by object ID.
    /// </summary>
    public IReadOnlyDictionary<string, InventorySlot> Slots { get; init; }
        = ImmutableDictionary<string, InventorySlot>.Empty;

    /// <summary>
    /// Total carry weight of all items in the inventory, in grams.
    /// </summary>
    public int TotalWeightGrams => Slots.Values.Sum(s => s.WeightGrams * s.Quantity);
}

/// <summary>
/// One stack of identical items in an inventory.
/// </summary>
/// <param name="ObjectId">Source world-object ID.</param>
/// <param name="DisplayName">Human-readable item name.</param>
/// <param name="Kind">Semantic item category.</param>
/// <param name="WeightGrams">Per-unit weight in grams.</param>
/// <param name="Quantity">Number of units in this slot.</param>
public sealed record InventorySlot(
    string ObjectId,
    string DisplayName,
    PickupItemKind Kind,
    int WeightGrams,
    int Quantity);
