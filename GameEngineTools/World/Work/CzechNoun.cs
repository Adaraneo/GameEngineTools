// CzechNoun.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Work
{
    /// <summary>How an object type is named in Czech: nominative lemma, grammar-library declension pattern, gender (Masculine, Feminine or Neuter).</summary>
    public sealed record CzechNoun(string Lemma, string Pattern, string Gender)
    {
        /// <summary>The grammatical genders a noun may be given.</summary>
        public static readonly IReadOnlyList<string> Genders = new[] { "Masculine", "Feminine", "Neuter" };
    }
}
