// LemmaAffectRecord.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Contracts
{
    /// <summary>Provenance of a connotation entry.</summary>
    public enum AffectSource
    {
        /// <summary>Hand-curated by a native speaker (the only source in the MVP).</summary>
        Curated,

        /// <summary>Reserved for future bulk import (SubLex/SocioLex/NRC-VAD hybrid) — not used yet.</summary>
        MachineDerived,
    }

    /// <summary>
    /// Connotative/affective properties of one lemma, independent of the grammatical
    /// <see cref="Polarity"/> (which stays purely sentence negation and never carries sentiment).
    /// Curated entries cover the <c>SeedPredicateLexicon</c>; <see cref="AffectSource.MachineDerived"/>
    /// is reserved for a future bulk import.
    /// </summary>
    /// <param name="Valence">Affective valence of the lemma, −1 (negative) … +1 (positive).</param>
    /// <param name="Conventionality">
    /// How conventionally the lemma/phrase is used ironically, [0..1] (Giora's Graded Salience:
    /// a highly conventional ironic phrase is decoded even by low-ToM listeners).
    /// </param>
    /// <param name="Source">Where the entry came from.</param>
    public sealed record LemmaAffectRecord(double Valence, double Conventionality, AffectSource Source);
}
