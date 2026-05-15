// NuclearFamilySpec.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Declarative specification for generating a prebuilt nuclear family
    /// with genetically inherited children of arbitrary ages.
    /// </summary>
    /// <remarks>
    /// Pass this to <see cref="NuclearFamilyGenerator.Generate"/> to get back a
    /// <see cref="NuclearFamily"/> with all characters created, genetically linked,
    /// and relationship edges seeded via <see cref="FamilyBuilder"/>.
    /// </remarks>
    /// <param name="PartnerARequest">
    /// Blueprint request for the first parent.
    /// Controls sex, age range, personality hints, and seed.
    /// </param>
    /// <param name="PartnerBRequest">
    /// Blueprint request for the second parent.
    /// </param>
    /// <param name="Children">
    /// Ordered list of child specifications.
    /// Each child will be generated via <see cref="IChildBlueprintGenerator"/>
    /// using the two parents as genetic sources.
    /// </param>
    public sealed record NuclearFamilySpec(
        HumanBlueprintRequest PartnerARequest,
        HumanBlueprintRequest PartnerBRequest,
        IReadOnlyList<ChildSpec> Children);

    /// <summary>
    /// Specification for a single child within a <see cref="NuclearFamilySpec"/>.
    /// </summary>
    /// <param name="BornOn">
    /// The child's birth date in world time.
    /// Determines the child's age, <see cref="StadiumType"/>, and appearance generation parameters.
    /// The same date always produces the same child for the same parents (deterministic seed).
    /// </param>
    /// <param name="Sex">
    /// Optional forced biological sex for the child.
    /// When <c>null</c>, sex is randomly determined during generation (50/50 Male/Female).
    /// </param>
    /// <param name="Seed">
    /// Optional explicit RNG seed for the child.
    /// When <c>null</c>, the seed is derived deterministically from both parent IDs and <paramref name="BornOn"/>.
    /// Use an explicit seed only when you need to reproduce a specific child across different parent pairs.
    /// </param>
    public sealed record ChildSpec(
        WDateOnly BornOn,
        SexBiology? Sex = null,
        int? Seed = null);
}
