// PickupItemKind.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects;

/// <summary>
/// Semantic category of a pickable world object.
/// Used by the inventory engine and behavior modifiers to reason about
/// what a character gains from picking up an item.
/// </summary>
public enum PickupItemKind
{
    /// <summary>Not a pickable item.</summary>
    None,

    /// <summary>Consumable food item.</summary>
    Food,

    /// <summary>Consumable drink or water source.</summary>
    Drink,

    /// <summary>Plant material — alchemical or medicinal use.</summary>
    Herb,

    /// <summary>Craftable or usable tool.</summary>
    Tool,

    /// <summary>Melee or ranged weapon.</summary>
    Weapon,

    /// <summary>Key for a lock or gate.</summary>
    Key,

    /// <summary>Small decorative or sentimental object.</summary>
    Trinket,

    /// <summary>Currency.</summary>
    Gold,

    /// <summary>Protective armor piece.</summary>
    Armor,

    /// <summary>Clothes</summary>
    Clothes,

    /// <summary>Musical instrument or game piece — lute, dice, chess board.</summary>
    Instrument,

    // ── Food-economy Tier 1: raw materials and processed foods ──────────────
    // Typed kinds let RecipeRegistry verify input/output at compile time (grain→flour→bread,
    // milk→cheese). See docs/plans/food-economy-tier1-implementation-plan.md §1.

    /// <summary>Raw grain harvested from a field (input to milling).</summary>
    Grain,

    /// <summary>Milled flour (output of grain, input to baking).</summary>
    Flour,

    /// <summary>Raw milk (input to cheese-making).</summary>
    Milk,

    /// <summary>Baked bread — a processed, edible food.</summary>
    Bread,

    /// <summary>Cheese — a processed, edible food.</summary>
    Cheese,
}
