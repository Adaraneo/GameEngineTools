// IAppearanceGenerator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Generates a randomised <see cref="PhysicalAppearance"/> for a character.
    /// </summary>
    /// <remarks>
    /// The generation is fully deterministic when a fixed seed is provided —
    /// the same seed and the same biological sex will always produce the same result.
    /// </remarks>
    public interface IAppearanceGenerator
    {
        /// <summary>
        /// Generates a <see cref="PhysicalAppearance"/> for a character of the given biological sex.
        /// </summary>
        /// <param name="sex">
        /// Biological sex of the character.
        /// Drives soft statistical morphology tendencies through the effective spec.
        /// </param>
        /// <param name="seed">
        /// RNG seed. The same seed combined with the same <paramref name="sex"/>
        /// always produces identical output (deterministic generation).
        /// </param>
        /// <param name="stadium">
        /// Life stage of the character. Drives height ranges, proportions and facial feature
        /// distributions appropriate for the character's age group.
        /// </param>
        /// <param name="spec">
        /// Optional generation parameters.
        /// Defaults to <see cref="AppearanceGenSpec.Default"/> when <c>null</c>.
        /// </param>
        /// <returns>An immutable <see cref="PhysicalAppearance"/> record.</returns>
        PhysicalAppearance Generate(SexBiology sex, int seed, StadiumType stadium = StadiumType.Adult, AppearanceGenSpec? spec = null);
    }
}
