// NounPhrase.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Realization
{
    using Grammar.Core.Enums;

    /// <summary>The inanimate thing an act is about, as the realizer needs it to name it: nominative lemma, declension pattern, gender.</summary>
    public readonly record struct NounPhrase(string Lemma, string Pattern, Gender Gender);
}
