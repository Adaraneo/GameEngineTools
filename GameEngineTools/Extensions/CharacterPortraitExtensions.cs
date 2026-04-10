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
    public static class CharacterPortraitExtensions
    {
        /// <summary>
        /// Builds a portrait specification from a character.
        /// </summary>
        /// <param name="human">Character source.</param>
        /// <param name="builder">Portrait spec builder.</param>
        /// <returns>Deterministic portrait specification.</returns>
        public static PortraitSpec BuildPortraitSpec(this IHuman human, IPortraitSpecBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(human);
            ArgumentNullException.ThrowIfNull(builder);
            return builder.Build(human.Biology, human.PhysicalAppearance, human.Snapshot);
        }

        /// <summary>
        /// Formats a portrait prompt from a character.
        /// </summary>
        /// <param name="human">Character source.</param>
        /// <param name="builder">Portrait spec builder.</param>
        /// <param name="formatter">Portrait prompt formatter.</param>
        /// <returns>Human-readable portrait prompt.</returns>
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
        /// <returns>Human-readable portrait prompt.</returns>
        public static string ToPortraitPrompt(this CharacterBase character, IPortraitSpecBuilder builder, IPortraitPromptFormatter formatter)
        {
            ArgumentNullException.ThrowIfNull(character);
            return character.Person.ToPortraitPrompt(builder, formatter);
        }
    }
}
