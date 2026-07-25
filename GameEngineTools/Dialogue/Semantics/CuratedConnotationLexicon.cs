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

        // Columns: Valence, Conventionality, Source, PowerAgent, AgencyAgent (Sap 2017 frames).
        private static readonly IReadOnlyDictionary<string, LemmaAffectRecord> Entries =
            new Dictionary<string, LemmaAffectRecord>(StringComparer.Ordinal)
            {
                // SmallTalk — near-synonyms, kept equal (calibration guard).
                ["povídat si"] = new(0.30, 0.0, AffectSource.Curated, PowerAgent: 0.0, AgencyAgent: 0.1),
                ["bavit se"] = new(0.30, 0.0, AffectSource.Curated, PowerAgent: 0.0, AgencyAgent: 0.1),

                // Question — plain interest vs intrusive probing (probing asserts some control).
                ["ptát se"] = new(0.10, 0.0, AffectSource.Curated, PowerAgent: 0.0, AgencyAgent: 0.2),
                ["vyptávat se"] = new(-0.15, 0.0, AffectSource.Curated, PowerAgent: 0.3, AgencyAgent: 0.4),

                // SelfDisclosure — gift of trust vs burdened admission (both cede a little power).
                ["svěřovat se"] = new(0.45, 0.0, AffectSource.Curated, PowerAgent: -0.1, AgencyAgent: 0.1),
                ["přiznávat"] = new(0.05, 0.0, AffectSource.Curated, PowerAgent: -0.2, AgencyAgent: 0.0),

                // Validation — warm praise vs considered appreciation vs cognitive assent.
                ["chválit"] = new(0.60, 0.0, AffectSource.Curated, PowerAgent: 0.1, AgencyAgent: 0.2),
                ["oceňovat"] = new(0.70, 0.0, AffectSource.Curated, PowerAgent: 0.1, AgencyAgent: 0.2),
                ["souhlasit"] = new(0.40, 0.0, AffectSource.Curated, PowerAgent: -0.1, AgencyAgent: -0.2),

                // Boundary — calm refusal vs offended push-back (both assert the self).
                ["odmítat"] = new(-0.50, 0.0, AffectSource.Curated, PowerAgent: 0.3, AgencyAgent: 0.4),
                ["ohrazovat se"] = new(-0.40, 0.0, AffectSource.Curated, PowerAgent: 0.2, AgencyAgent: 0.3),

                // Humor — near-synonyms.
                ["žertovat"] = new(0.35, 0.0, AffectSource.Curated, PowerAgent: 0.1, AgencyAgent: 0.3),
                ["vtipkovat"] = new(0.30, 0.0, AffectSource.Curated, PowerAgent: 0.1, AgencyAgent: 0.3),

                // Meta.
                ["rozebírat"] = new(0.00, 0.0, AffectSource.Curated, PowerAgent: 0.1, AgencyAgent: 0.3),
                ["mluvit"] = new(0.05, 0.0, AffectSource.Curated, PowerAgent: 0.0, AgencyAgent: 0.2),

                // Invite — personal warmth vs businesslike suggestion (host is agentive).
                ["zvát"] = new(0.50, 0.0, AffectSource.Curated, PowerAgent: 0.2, AgencyAgent: 0.5),
                ["navrhovat"] = new(0.20, 0.0, AffectSource.Curated, PowerAgent: 0.1, AgencyAgent: 0.4),

                // Directive family — NOT in SeedPredicateLexicon yet (planner can't pick them), but
                // curated here so the Phase-2 power/agency mechanism has the decision-test's strong
                // differentials to exercise: request (deferential) vs demand (dominant) vs beg (subordinate).
                ["požádat"] = new(0.10, 0.0, AffectSource.Curated, PowerAgent: -0.2, AgencyAgent: 0.2),
                ["vyžadovat"] = new(-0.30, 0.0, AffectSource.Curated, PowerAgent: 0.8, AgencyAgent: 0.7),
                ["žebrat o"] = new(-0.20, 0.0, AffectSource.Curated, PowerAgent: -0.8, AgencyAgent: -0.5),
                ["pokárat"] = new(-0.35, 0.0, AffectSource.Curated, PowerAgent: 0.6, AgencyAgent: 0.5),
                ["zesměšnit"] = new(-0.55, 0.0, AffectSource.Curated, PowerAgent: 0.5, AgencyAgent: 0.5),

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
