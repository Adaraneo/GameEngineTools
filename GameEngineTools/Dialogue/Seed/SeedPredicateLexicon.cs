// SeedPredicateLexicon.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Seed
{
    using System.Collections.Generic;
    using Grammar.Core.Enums;

    /// <summary>
    /// One seed predicate for the <c>SpeechActPlanner</c> (Phase 3): a Czech lemma plus the
    /// dimensions the planner needs to build a <see cref="SpeechAct"/>. This is authored data (see
    /// <c>docs/dialogue-seed-predicates.md</c>), validated against VALLEX/NESČ/IJP — it is NOT copied
    /// from any NC-SA corpus and carries no runtime dependency on one.
    /// </summary>
    /// <param name="LemmaImperfective">Canonical lemma key (imperfective, matches GM <c>LexicalEntry.Lemma</c>).</param>
    /// <param name="LemmaPerfective">Perfective aspect counterpart.</param>
    /// <param name="Point">Default illocutionary point for this predicate.</param>
    /// <param name="ReflexiveParticle"><c>"se"</c>, <c>"si"</c>, or <c>null</c> for a non-reflexive verb.</param>
    /// <param name="AddresseeRole">
    /// Which FGD role the addressee fills for this predicate — the load-bearing binding: it is
    /// <see cref="FgdFunctor.PAT"/> for <c>pozvat</c>/<c>pochválit</c>/<c>souhlasit</c>, but
    /// <see cref="FgdFunctor.ADDR"/> for <c>svěřit se</c>/<c>navrhnout</c>. <c>null</c> when the
    /// addressee is not a core argument (e.g. <c>odmítnout</c>, <c>vtipkovat</c>).
    /// </param>
    /// <param name="TakesDirection">Whether a DIR3 (<c>kam</c>) slot applies, e.g. <c>pozvat</c>.</param>
    public sealed record SeedPredicate(
        string LemmaImperfective,
        string LemmaPerfective,
        IllocutionaryPoint Point,
        string? ReflexiveParticle,
        FgdFunctor? AddresseeRole,
        bool TakesDirection = false);

    /// <summary>
    /// Seed predicate vocabulary for the dialogue engine, grouped by <see cref="RelationalActKind"/>.
    /// The Phase-3 planner selects deterministically among the candidates for a chosen act kind; the
    /// GM side later realises the surface Czech from the chosen lemma and its valency frame.
    /// </summary>
    public static class SeedPredicateLexicon
    {
        /// <summary>Candidate predicates per relational act kind.</summary>
        public static IReadOnlyDictionary<RelationalActKind, IReadOnlyList<SeedPredicate>> Predicates { get; } =
            new Dictionary<RelationalActKind, IReadOnlyList<SeedPredicate>>
            {
                [RelationalActKind.SmallTalk] = new SeedPredicate[]
                {
                    new("povídat si", "popovídat si", IllocutionaryPoint.Expressive, "si", FgdFunctor.ADDR),
                    new("bavit se", "pobavit se", IllocutionaryPoint.Expressive, "se", FgdFunctor.ADDR),
                },
                [RelationalActKind.Question] = new SeedPredicate[]
                {
                    new("ptát se", "zeptat se", IllocutionaryPoint.Question, "se", FgdFunctor.ADDR),
                    new("vyptávat se", "vyptat se", IllocutionaryPoint.Question, "se", FgdFunctor.ADDR),
                },
                [RelationalActKind.SelfDisclosure] = new SeedPredicate[]
                {
                    new("svěřovat se", "svěřit se", IllocutionaryPoint.Assertive, "se", FgdFunctor.ADDR),
                    new("přiznávat", "přiznat", IllocutionaryPoint.Assertive, null, FgdFunctor.ADDR),
                },
                [RelationalActKind.Validation] = new SeedPredicate[]
                {
                    new("chválit", "pochválit", IllocutionaryPoint.Expressive, null, FgdFunctor.PAT),
                    new("oceňovat", "ocenit", IllocutionaryPoint.Expressive, null, FgdFunctor.PAT),
                    new("souhlasit", "souhlasit", IllocutionaryPoint.Expressive, null, FgdFunctor.PAT),
                },
                [RelationalActKind.Boundary] = new SeedPredicate[]
                {
                    new("odmítat", "odmítnout", IllocutionaryPoint.Directive, null, null),
                    new("ohrazovat se", "ohradit se", IllocutionaryPoint.Directive, "se", null),
                },
                [RelationalActKind.Humor] = new SeedPredicate[]
                {
                    new("žertovat", "zažertovat", IllocutionaryPoint.Expressive, null, FgdFunctor.ADDR),
                    new("vtipkovat", "zavtipkovat", IllocutionaryPoint.Expressive, null, null),
                },
                [RelationalActKind.Meta] = new SeedPredicate[]
                {
                    new("rozebírat", "rozebrat", IllocutionaryPoint.Assertive, null, FgdFunctor.ADDR),
                    new("mluvit", "promluvit", IllocutionaryPoint.Assertive, null, FgdFunctor.ADDR),
                },
                [RelationalActKind.Invite] = new SeedPredicate[]
                {
                    new("zvát", "pozvat", IllocutionaryPoint.Directive, null, FgdFunctor.PAT, TakesDirection: true),
                    new("navrhovat", "navrhnout", IllocutionaryPoint.Directive, null, FgdFunctor.ADDR),
                },
            };
    }
}
