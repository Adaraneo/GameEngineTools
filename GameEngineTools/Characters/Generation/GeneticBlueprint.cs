// GeneticBlueprint.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Immutable genetic blueprint for a character — created once at character generation, never changes.
    /// Stores age-agnostic heritable traits used by <see cref="AppearanceProjector"/> to derive
    /// <see cref="PhysicalAppearance"/> at any age.
    /// </summary>
    /// <remarks>
    /// Always generated with Adult-neutral latents (<c>Juvenility = 0.15</c>, <c>Aging = 0.0</c>).
    /// The projector overwrites those fields from the character's actual age at runtime.
    /// </remarks>
    public sealed record GeneticBlueprint(
        /// <summary>Biological sex — drives height scaling and sexual dimorphism projection.</summary>
        SexBiology Sex,

        /// <summary>RNG seed — guarantees deterministic projection for a given age year.</summary>
        int Seed,

        /// <summary>Immutable colour traits: eye colour, natural hair colour, skin tone, hair type.</summary>
        ColorTraits Colors,

        /// <summary>Genetic body latents without stadium-specific juvenility/aging overrides.</summary>
        BodyLatent BodyLatent,

        /// <summary>Genetic face latents without stadium-specific juvenility/aging overrides.</summary>
        FaceLatent FaceLatent);
}
