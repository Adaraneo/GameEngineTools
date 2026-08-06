// LexicalVocabulary.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Language
{
    using System.Collections.Generic;

    /// <summary>
    /// One character's whole vocabulary, in the form it is saved and restored in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A record of its own rather than a bare list, so the saved shape can grow (a version marker, a
    /// distinct-sources set for complex contagion) without changing the field on the save DTO.
    /// </para>
    /// <para>
    /// Words are stored as <b>lemmas, not identifiers into the seed lexicon</b>. An index would be
    /// smaller, but it is only meaningful against a table that stays stable forever: reorder or remove a
    /// seed predicate and every saved vocabulary silently shifts by one, so a character comes back
    /// knowing a different word than they learned. Nothing would throw — the behaviour would just
    /// quietly change. Lemmas are self-describing and cannot drift, and the volume is trivial (a closed
    /// set of ~20 predicates, of which a character knows a handful).
    /// </para>
    /// </remarks>
    /// <param name="Entries">One record per lemma this character has encountered.</param>
    public sealed record LexicalVocabulary(IReadOnlyList<LexicalAcquisition> Entries)
    {
        /// <summary>An empty vocabulary — what a character who has learned nothing yet carries.</summary>
        public static LexicalVocabulary Empty { get; } = new(new List<LexicalAcquisition>());
    }
}
