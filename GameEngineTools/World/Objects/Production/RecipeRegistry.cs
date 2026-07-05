// RecipeRegistry.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Production
{
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Collections.ObjectModel;

    /// <summary>
    /// Static, immutable table of all <see cref="Recipe"/> definitions. Design data, not runtime
    /// state (analogous to <c>ActionValueLoadings</c>), so it is a compiled dictionary, not a
    /// SQLite table. Food-economy Tier 1.
    /// </summary>
    public static class RecipeRegistry
    {
        #region Public accessors

        /// <summary>Looks up the recipe that produces <paramref name="outputKind"/>, or <c>null</c>.</summary>
        public static Recipe? FindByOutput(PickupItemKind outputKind)
            => Table.TryGetValue(outputKind, out var recipe) ? recipe : null;

        /// <summary>All registered recipes (for the provisioning bridge to consider).</summary>
        public static IEnumerable<Recipe> All => Table.Values;

        #endregion Public accessors

        #region Internal table

        // Deliberately small Tier 1 set: grain→flour→bread and milk→cheese exercise the full
        // production → processing → consumption chain end to end. Expand only after Tier 1 tests pass.
        private static readonly IReadOnlyDictionary<PickupItemKind, Recipe> Table =
            new ReadOnlyDictionary<PickupItemKind, Recipe>(BuildTable());

        private static Dictionary<PickupItemKind, Recipe> BuildTable() => new()
        {
            [PickupItemKind.Flour] = new Recipe(
                Id: "recipe_flour_from_grain",
                DisplayName: "Mlít obilí",
                Inputs: ImmutableArray.Create(new RecipeInput(PickupItemKind.Grain, 2)),
                OutputKind: PickupItemKind.Flour,
                OutputDisplayName: "Mouka",
                DurationMinutes: 45,
                RequiredLocationType: "mill"),

            [PickupItemKind.Bread] = new Recipe(
                Id: "recipe_bread_from_flour",
                DisplayName: "Péct chléb",
                Inputs: ImmutableArray.Create(new RecipeInput(PickupItemKind.Flour, 1)),
                OutputKind: PickupItemKind.Bread,
                OutputDisplayName: "Čerstvý chléb",
                DurationMinutes: 90,
                RequiredLocationType: "bakery"),

            [PickupItemKind.Cheese] = new Recipe(
                Id: "recipe_cheese_from_milk",
                DisplayName: "Vyrábět sýr",
                Inputs: ImmutableArray.Create(new RecipeInput(PickupItemKind.Milk, 2)),
                OutputKind: PickupItemKind.Cheese,
                OutputDisplayName: "Čerstvý sýr",
                DurationMinutes: 180,
                RequiredLocationType: "dairy"),
        };

        #endregion Internal table
    }
}
