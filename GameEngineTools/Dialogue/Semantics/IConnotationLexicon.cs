// IConnotationLexicon.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Semantics
{
    /// <summary>
    /// Read-only lemma → affect lookup. A missing lemma is neutral — implementations never throw.
    /// Connotation data is owned by GET (it is a fact about this appraisal model, not about the word
    /// itself), joined to GM lemmata only at runtime via the string key.
    /// </summary>
    public interface IConnotationLexicon
    {
        /// <summary>Returns the affect record for <paramref name="lemma"/>, or the neutral record when unknown.</summary>
        LemmaAffectRecord Lookup(string lemma);
    }

    /// <summary>A no-op lexicon: every lemma is neutral. Used when no lexicon is wired.</summary>
    public sealed class NeutralConnotationLexicon : IConnotationLexicon
    {
        /// <summary>Shared instance.</summary>
        public static NeutralConnotationLexicon Instance { get; } = new();

        private static readonly LemmaAffectRecord Neutral = new(0.0, 0.0, AffectSource.Curated);

        /// <inheritdoc/>
        public LemmaAffectRecord Lookup(string lemma) => Neutral;
    }
}
