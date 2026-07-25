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
    /// <param name="PowerAgent">
    /// Connotation frame of POWER (Sap et al. 2017), [−1..1]: does the verb imply the agent (speaker)
    /// holds power OVER the theme (addressee)? <c>+1</c> dominant ("vyžadovat"), <c>−1</c> subordinate
    /// ("žebrat o"), <c>0</c> neutral. Phase-2 signal — carried on the record, consumed later.
    /// </param>
    /// <param name="AgencyAgent">
    /// Connotation frame of AGENCY (Sap et al. 2017), [−1..1]: does the verb portray the agent as
    /// high-agency / in control (<c>+1</c>, "rozhodnout") or low-agency / passive (<c>−1</c>, "doufat")?
    /// </param>
    public sealed record LemmaAffectRecord(
        double Valence,
        double Conventionality,
        AffectSource Source,
        double PowerAgent = 0.0,
        double AgencyAgent = 0.0);
}
