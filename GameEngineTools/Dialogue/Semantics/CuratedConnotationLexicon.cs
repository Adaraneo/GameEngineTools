// CuratedConnotationLexicon.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Semantics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Hand-curated connotation entries for the lemmata in <c>SeedPredicateLexicon</c> (plus a few
    /// conventionally ironic phrases). Deliberately small and native-speaker-authored — no bulk
    /// import (that is deferred until curation stops scaling; see the connotation plan, Phase 3/4).
    /// Values follow the Phase-0 decision test in <c>docs/dialogue-connotation-decision-test.md</c>;
    /// near-synonym pairs (povídat si/bavit se, žertovat/vtipkovat) differ by ≤ 0.05 by design.
    /// </summary>
    public sealed class CuratedConnotationLexicon : IConnotationLexicon
    {
        private static readonly LemmaAffectRecord Neutral = new(0.0, 0.0, AffectSource.Curated);

        private static readonly IReadOnlyDictionary<string, LemmaAffectRecord> Entries =
            new Dictionary<string, LemmaAffectRecord>(StringComparer.Ordinal)
            {
                // SmallTalk — near-synonyms, kept equal (calibration guard).
                ["povídat si"] = new(0.30, 0.0, AffectSource.Curated),
                ["bavit se"] = new(0.30, 0.0, AffectSource.Curated),

                // Question — plain interest vs intrusive probing.
                ["ptát se"] = new(0.10, 0.0, AffectSource.Curated),
                ["vyptávat se"] = new(-0.15, 0.0, AffectSource.Curated),

                // SelfDisclosure — gift of trust vs burdened admission.
                ["svěřovat se"] = new(0.45, 0.0, AffectSource.Curated),
                ["přiznávat"] = new(0.05, 0.0, AffectSource.Curated),

                // Validation — warm praise vs considered appreciation vs cognitive assent.
                ["chválit"] = new(0.60, 0.0, AffectSource.Curated),
                ["oceňovat"] = new(0.70, 0.0, AffectSource.Curated),
                ["souhlasit"] = new(0.40, 0.0, AffectSource.Curated),

                // Boundary — calm refusal vs offended push-back.
                ["odmítat"] = new(-0.50, 0.0, AffectSource.Curated),
                ["ohrazovat se"] = new(-0.40, 0.0, AffectSource.Curated),

                // Humor — near-synonyms.
                ["žertovat"] = new(0.35, 0.0, AffectSource.Curated),
                ["vtipkovat"] = new(0.30, 0.0, AffectSource.Curated),

                // Meta.
                ["rozebírat"] = new(0.00, 0.0, AffectSource.Curated),
                ["mluvit"] = new(0.05, 0.0, AffectSource.Curated),

                // Invite — personal warmth vs businesslike suggestion.
                ["zvát"] = new(0.50, 0.0, AffectSource.Curated),
                ["navrhovat"] = new(0.20, 0.0, AffectSource.Curated),

                // Conventionally ironic phrases (Giora GSH) — high Conventionality drives the
                // irony bypass even for low-ToM listeners.
                ["to se povedlo"] = new(-0.30, 0.90, AffectSource.Curated),
                ["no výborně"] = new(-0.35, 0.85, AffectSource.Curated),
            };

        /// <inheritdoc/>
        public LemmaAffectRecord Lookup(string lemma)
            => !string.IsNullOrEmpty(lemma) && Entries.TryGetValue(lemma, out var record) ? record : Neutral;
    }
}
