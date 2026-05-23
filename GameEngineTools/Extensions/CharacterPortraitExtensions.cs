// CharacterPortraitExtensions.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Extensions
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.Characters.Generation.Portraits;

    /// <summary>
    /// Helpers for deriving deterministic portrait data from characters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These extension methods are the bridge between the domain model
    /// (<see cref="IHuman"/>, <see cref="CharacterBase"/>) and the portrait pipeline.
    /// They are responsible for extracting age and ancestry from the character
    /// and forwarding them to <see cref="IPortraitSpecBuilder.Build"/>.
    /// </para>
    /// </remarks>
    public static class CharacterPortraitExtensions
    {
        /// <summary>
        /// Builds a portrait specification from a character.
        /// </summary>
        /// <remarks>
        /// Passes <see cref="IHuman.Age"/> and <see cref="Identity.AncestryHint"/>
        /// to the builder so the formatter can produce a complete, model-ready prompt.
        /// </remarks>
        /// <param name="human">Source character.</param>
        /// <param name="builder">Portrait spec builder.</param>
        /// <returns>Deterministic portrait specification.</returns>
        public static PortraitSpec BuildPortraitSpec(this IHuman human, IPortraitSpecBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(human);
            ArgumentNullException.ThrowIfNull(builder);

            return builder.Build(
                biology: human.Biology,
                appearance: human.PhysicalAppearance,
                ageYears: human.Age,                      // critical — without age the model defaults to ~25yo
                ancestryHint: human.Identity.AncestryHint,    // null = omit hint from prompt
                snapshot: human.Snapshot);
        }

        /// <summary>
        /// Formats a portrait prompt directly from a character.
        /// </summary>
        /// <param name="human">Source character.</param>
        /// <param name="builder">Portrait spec builder.</param>
        /// <param name="formatter">Portrait prompt formatter.</param>
        /// <returns>Human-readable portrait prompt ready for an image generation model.</returns>
        public static string ToPortraitPrompt(this IHuman human, IPortraitSpecBuilder builder, IPortraitPromptFormatter formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            return formatter.Format(human.BuildPortraitSpec(builder));
        }

        /// <summary>
        /// Formats a portrait prompt from a game character wrapper.
        /// </summary>
        /// <param name="character">Character wrapper.</param>
        /// <param name="builder">Portrait spec builder.</param>
        /// <param name="formatter">Portrait prompt formatter.</param>
        /// <returns>Human-readable portrait prompt ready for an image generation model.</returns>
        public static string ToPortraitPrompt(this CharacterBase character, IPortraitSpecBuilder builder, IPortraitPromptFormatter formatter)
        {
            ArgumentNullException.ThrowIfNull(character);
            return character.Person.ToPortraitPrompt(builder, formatter);
        }
    }
}
