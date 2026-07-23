// TemporaryCzechActRealizer.cs
// Copyright (c) 50PSoftware

// ═══════════════════════════════════════════════════════════════════════════════════════════════
//  ⚠ TEMPORARY — STOPGAP SURFACE REALIZATION ⚠
//
//  This type produces a ROUGH Czech gloss of a SpeechAct by sending per-word CzechWordRequests to
//  the GM word-form composer (declension of participants) + stored past-tense verb forms — exactly
//  the manual style used by DefaultNarrativeFormatter.
//
//  It is a PLACEHOLDER until the Grammar (GM) repo ships its valency-lexicon-driven pipeline
//  (SentencePlanner → ClausePlanner → Microplanner → WordOrderResolver → MorphologyEngine). At that
//  point this whole file (and its hand-written verb-form / case table) is DELETED and the GM side
//  consumes the SpeechAct directly. Do NOT grow this into a real sentence planner and do NOT let the
//  simulation depend on its output — it exists only to preview acts (e.g. in WorldObserver).
//
//  Everything about it is deliberately kept in the GameEngineTools.Dialogue.Temporary namespace so
//  it is trivially greppable and removable.
// ═══════════════════════════════════════════════════════════════════════════════════════════════

namespace GameEngineTools.Dialogue.Temporary
{
    using System;
    using System.Collections.Generic;
    using Grammar.Core.Enums;
    using Grammar.Czech.Models;
    using Grammar.Czech.Services;

    /// <summary>
    /// <b>TEMPORARY.</b> Renders a <see cref="SpeechAct"/> to a rough Czech string for preview only.
    /// See the file banner — this is a stopgap until GM-side realization lands and will be removed.
    /// </summary>
    public sealed class TemporaryCzechActRealizer
    {
        /// <summary>A participant's display identity, supplied by the caller (the act carries only ids).</summary>
        /// <param name="Name">Nominative name lemma.</param>
        /// <param name="IsFemale">Grammatical gender for agreement.</param>
        public readonly record struct Person(string Name, bool IsFemale);

        /// <summary>TEMPORARY per-lemma inflection hints (perfective past forms + addressee slot form).</summary>
        private readonly record struct TempForm(string PastMasculine, string PastFeminine, string? Preposition, Case? AddresseeCase);

        // Keyed by the imperfective lemma (== SpeechAct.PredicateLemma). Reflexive particle is baked
        // into the past form so a name-first template yields correct 2nd-position (Wackernagel) clitic
        // placement ("Petr se zeptal Jany.").
        private static readonly IReadOnlyDictionary<string, TempForm> Forms = new Dictionary<string, TempForm>
        {
            ["povídat si"] = new("si povídal", "si povídala", "s", Case.Instrumental),
            ["bavit se"] = new("se bavil", "se bavila", "s", Case.Instrumental),
            ["ptát se"] = new("se zeptal", "se zeptala", null, Case.Genitive),
            ["vyptávat se"] = new("se vyptával", "se vyptávala", null, Case.Genitive),
            ["svěřovat se"] = new("se svěřil", "se svěřila", null, Case.Dative),
            ["přiznávat"] = new("přiznal", "přiznala", null, Case.Dative),
            ["chválit"] = new("pochválil", "pochválila", null, Case.Accusative),
            ["oceňovat"] = new("ocenil", "ocenila", null, Case.Accusative),
            ["souhlasit"] = new("souhlasil", "souhlasila", "s", Case.Instrumental),
            ["odmítat"] = new("odmítl", "odmítla", null, null),
            ["ohrazovat se"] = new("se ohradil", "se ohradila", null, null),
            ["žertovat"] = new("žertoval", "žertovala", "s", Case.Instrumental),
            ["vtipkovat"] = new("vtipkoval", "vtipkovala", null, null),
            ["rozebírat"] = new("rozebral", "rozebrala", "s", Case.Instrumental),
            ["mluvit"] = new("mluvil", "mluvila", "s", Case.Instrumental),
            ["zvát"] = new("pozval", "pozvala", null, Case.Accusative),
            ["navrhovat"] = new("navrhl", "navrhla", null, Case.Dative),
        };

        private readonly CzechWordFormComposer _composer;

        /// <summary>Creates the realizer over the GM word-form composer.</summary>
        public TemporaryCzechActRealizer(CzechWordFormComposer composer)
            => _composer = composer ?? throw new ArgumentNullException(nameof(composer));

        /// <summary>
        /// <b>TEMPORARY.</b> Produces a rough Czech sentence for <paramref name="act"/>. Falls back to a
        /// bracketed marker when the predicate is not in the stopgap table.
        /// </summary>
        public string Realize(SpeechAct act, Person speaker, Person addressee)
        {
            ArgumentNullException.ThrowIfNull(act);

            if (!Forms.TryGetValue(act.PredicateLemma, out var form))
            {
                return $"[{act.RelationalKind}] {speaker.Name} → {addressee.Name}";
            }

            var verb = speaker.IsFemale ? form.PastFeminine : form.PastMasculine;

            if (form.AddresseeCase is not { } addresseeCase)
            {
                return $"{speaker.Name} {verb}.";
            }

            var declined = Decline(addressee, addresseeCase);
            var prefix = form.Preposition is null ? string.Empty : form.Preposition + " ";
            return $"{speaker.Name} {verb} {prefix}{declined}.";
        }

        /// <summary>
        /// <b>TEMPORARY.</b> Mode-2 render: the DIRECT SPEECH a speaker utters to the addressee —
        /// vocative address + a per-lemma template with tykání/vykání per <see cref="Register"/>
        /// (e.g. "Jano, nezajdeš se mnou?" vs "Jano, nezašla byste se mnou?"). Falls back to a
        /// bracketed marker for lemmas outside the stopgap table. Like everything in this file it is
        /// a preview until GM sentence planning lands and never re-enters simulation state.
        /// </summary>
        public string RealizeDirectSpeech(SpeechAct act, Person speaker, Person addressee)
        {
            ArgumentNullException.ThrowIfNull(act);

            var vocative = Decline(addressee, Case.Vocative);
            var formal = act.Register == Register.Formal;
            var line = BuildDirectSpeech(act.PredicateLemma, vocative, formal, addressee.IsFemale);

            return line ?? $"[{act.RelationalKind}] {speaker.Name} → {addressee.Name}";
        }

        /// <summary>TEMPORARY per-lemma direct-speech templates (informal ⇒ tykání, formal ⇒ vykání).</summary>
        private static string? BuildDirectSpeech(string lemma, string voc, bool formal, bool addresseeFemale) => lemma switch
        {
            // SmallTalk
            "povídat si" => formal ? $"{voc}, jak se máte?" : $"{voc}, jak se máš?",
            "bavit se" => formal ? $"Co je u vás nového, {voc}?" : $"Tak co je nového, {voc}?",

            // Question
            "ptát se" => formal ? $"{voc}, smím se vás na něco zeptat?" : $"{voc}, můžu se tě na něco zeptat?",
            "vyptávat se" => formal ? "A jak to bylo dál? Povídejte." : $"A jak to bylo dál, {voc}? Povídej.",

            // SelfDisclosure
            "svěřovat se" => formal ? $"{voc}, chci se vám s něčím svěřit." : $"{voc}, chci se ti s něčím svěřit.",
            "přiznávat" => formal ? $"{voc}, musím vám něco přiznat." : $"{voc}, musím ti něco přiznat.",

            // Validation
            "chválit" => formal ? $"{voc}, tohle se vám opravdu povedlo." : $"{voc}, tohle se ti fakt povedlo.",
            "oceňovat" => formal ? $"Vážím si vás, {voc}." : $"Vážím si tě, {voc}.",
            "souhlasit" => formal ? $"Souhlasím s vámi, {voc}." : $"Souhlasím s tebou, {voc}.",

            // Boundary
            "odmítat" => formal ? $"Ne, {voc}. To musím odmítnout." : $"Ne, {voc}. Tohle nechci.",
            "ohrazovat se" => formal ? "Proti tomu se musím ohradit." : $"Tak to teda ne, {voc}!",

            // Humor
            "žertovat" => formal ? $"Jen žertuji, {voc}." : $"Ale no tak, {voc}, dělám si legraci.",
            "vtipkovat" => $"To mi připomíná jeden vtip, {voc}.",

            // Meta
            "rozebírat" => formal ? $"{voc}, měli bychom si promluvit." : $"{voc}, měli bychom si promluvit o nás.",
            "mluvit" => formal ? $"Máte chvilku, {voc}?" : $"Máš chvilku, {voc}?",

            // Invite
            "zvát" => formal
                ? (addresseeFemale ? $"{voc}, nezašla byste se mnou?" : $"{voc}, nezašel byste se mnou?")
                : $"{voc}, nezajdeš se mnou?",
            "navrhovat" => $"{voc}, co kdybychom se někdy sešli?",

            _ => null,
        };

        /// <summary>Declines a participant name into <paramref name="case"/> via a CzechWordRequest.</summary>
        private string Decline(Person person, Case @case)
        {
            var request = new CzechWordRequest
            {
                Lemma = person.Name,
                WordCategory = WordCategory.Noun,
                Case = @case,
                Number = Number.Singular,
                Gender = person.IsFemale ? Gender.Feminine : Gender.Masculine,
                IsAnimate = true,
                Pattern = GuessPattern(person),
            };

            try
            {
                return _composer.GetFullForm(request).Form;
            }
            catch
            {
                // Best-effort preview only — never let a composer edge case break the caller.
                return person.Name;
            }
        }

        private static string GuessPattern(Person person) => person switch
        {
            { IsFemale: true, Name: var n } when n.EndsWith("a") || n.EndsWith("ia") => "žena",
            { IsFemale: true, Name: var n } when n.EndsWith("e") => "růže",
            { IsFemale: false, Name: var n } when n.EndsWith("us") => "muž",
            { IsFemale: false, Name: var n } when n.EndsWith("tor") => "pán",
            { IsFemale: true } => "žena",
            _ => "pán",
        };
    }
}
