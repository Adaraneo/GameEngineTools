// NarrativeCharacterInfo.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Narrative
{
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Basic character information for narrative formatting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why don't we use just a <c>string</c> name?</b><br/>
    /// Czech requires grammatical gender for correct verb inflection —
    /// "šel" vs. "šla", "přijal" vs. "přijala". Without the sex we would have to
    /// either write the ugly "šel/a", or ignore grammar.
    /// </para>
    /// </remarks>
    /// <param name="Name">Character name shown in the narrative (e.g. "Anna", "Petr").</param>
    /// <param name="Biology">Biological sex — for correct inflection in Czech.</param>
    public sealed record NarrativeCharacterInfo(string Name, SexBiology Biology)
    {
        /// <summary>
        /// Returns <c>true</c> if the character is female.
        /// Used internally to choose the grammatical gender in sentences.
        /// </summary>
        public bool IsFemale => Biology == SexBiology.Female;
    }
}
