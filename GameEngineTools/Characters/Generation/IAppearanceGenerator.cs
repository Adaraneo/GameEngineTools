// IAppearanceGenerator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Generates appearance data for a character.
    /// </summary>
    public interface IAppearanceGenerator
    {
        /// <summary>
        /// Generates an immutable <see cref="GeneticBlueprint"/> that stores age-agnostic genetic traits.
        /// Always uses Adult-neutral parameters — age effects are applied by <see cref="AppearanceProjector"/> at runtime.
        /// </summary>
        /// <param name="sex">Biological sex of the character.</param>
        /// <param name="seed">RNG seed. The same seed and sex always produce identical output.</param>
        /// <param name="spec">Optional generation parameters. Defaults to <see cref="AppearanceGenSpec.Default"/>.</param>
        GeneticBlueprint GenerateBlueprint(SexBiology sex, int seed, AppearanceGenSpec? spec = null);

        /// <summary>
        /// Convenience wrapper — projects the blueprint at the representative age for the given stadium.
        /// Prefer <see cref="GenerateBlueprint"/> for new code.
        /// </summary>
        PhysicalAppearance Generate(SexBiology sex, int seed, StadiumType stadium = StadiumType.Adult, AppearanceGenSpec? spec = null);
    }
}
