// Recipe.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Production
{
    using System.Collections.Immutable;

    /// <summary>
    /// Defines a transformation from one or more input item kinds into a single output item.
    /// Recipes are static game-design data (analogous to <c>ActionValueLoadings</c>), not runtime
    /// state — they live in <see cref="RecipeRegistry"/>, not in SQLite.
    /// </summary>
    /// <remarks>
    /// Design rationale: household production theory (Becker 1965, <i>The Economic Journal</i>
    /// 75(299):493-517) models a "commodity" (here: a food item) as the combination of produced
    /// inputs <b>and</b> labor time — raw ingredients alone generate no utility until processed.
    /// <see cref="DurationMinutes"/> is that labor-time cost. See
    /// docs/research/food-economy-research-findings.md §1.
    /// </remarks>
    public sealed record Recipe(
        string Id,
        string DisplayName,
        ImmutableArray<RecipeInput> Inputs,
        PickupItemKind OutputKind,
        string OutputDisplayName,
        int DurationMinutes,
        string RequiredLocationType);

    /// <summary>One required input line of a <see cref="Recipe"/> (an item kind and a quantity).</summary>
    public sealed record RecipeInput(PickupItemKind Kind, int Quantity);
}
