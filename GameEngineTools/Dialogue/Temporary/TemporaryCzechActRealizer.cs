// TemporaryCzechActRealizer.cs
// Copyright (c) 50PSoftware

// ═══════════════════════════════════════════════════════════════════════════════════════════════
//  ⚠ TEMPORARY — STOPGAP SURFACE REALIZATION ⚠
//
//  This type renders a SpeechAct to Czech for PREVIEW ONLY (WorldObserver, logs). Its output never
//  re-enters simulation state.
//
//  Mode 1 (Realize) hands the act to GM's sentence layer: the act becomes a CzechClause — a
//  predicate plus ClauseElements carrying FGD functors — and CzechSentenceBuilder derives the
//  surface form. GM owns ALL of the linguistics: verb conjugation and gender agreement, name
//  declension, Wackernagel placement of the se/si clitic, preposition vocalization and government,
//  word order from communicative status, and punctuation. Nothing here stores an inflected form.
//
//  What is still hand-held, and why:
//    • the per-lemma ClauseSpec table below carries GRAMMATICAL METADATA (verb class, aspect,
//      reflexive type, and the functor/case/preposition of the addressee slot) — not surface
//      strings. The case/preposition half exists only because GM's valency.json currently ships
//      frames for four verbs (dát, dávat, jít, vidět); per its README, "for a verb without a frame
//      the caller supplies the cases as before". When the dialogue predicates gain frames, that half
//      of the table is deleted and CzechClause fills the cases from the frame instead.
//    • mode 2 (RealizeDirectSpeech) keeps authored idiom skeletons. "Jak se máš?" is a conventional
//      utterance, not something derivable from a SpeechAct, so a generator cannot invent it. GM still
//      supplies the morphology inside them (vocative address, prepositional pronouns per register).
//
//  This remains a PLACEHOLDER until the GM repo ships its valency-lexicon-driven pipeline
//  (SentencePlanner → ClausePlanner → Microplanner → WordOrderResolver), at which point this whole
//  file is DELETED and GM consumes the SpeechAct directly. Do NOT grow it into a sentence planner —
//  delegating to GM's planner is the intended direction; re-implementing one here is not.
//
//  Everything stays in the GameEngineTools.Dialogue.Temporary namespace so it is trivially greppable
//  and removable.
// ═══════════════════════════════════════════════════════════════════════════════════════════════

namespace GameEngineTools.Dialogue.Temporary
{
    using System;
    using System.Collections.Generic;
    using Grammar.Core.Enums;
    using Grammar.Czech.Enums;
    using Grammar.Czech.Models;
    using Grammar.Czech.Models.Syntax;
    using Grammar.Czech.Services;
    using GPerson = Grammar.Core.Enums.Person;

    /// <summary>
    /// <b>TEMPORARY.</b> Renders a <see cref="SpeechAct"/> to Czech for preview only, delegating the
    /// linguistics to GM's sentence layer. See the file banner — this is a stopgap and will be removed.
    /// </summary>
    public sealed class TemporaryCzechActRealizer
    {
        /// <summary>A participant's display identity, supplied by the caller (the act carries only ids).</summary>
        /// <param name="Name">Nominative name lemma.</param>
        /// <param name="IsFemale">Grammatical gender for agreement.</param>
        public readonly record struct Person(string Name, bool IsFemale);

        /// <summary>
        /// TEMPORARY grammatical metadata for one predicate lemma — never a surface form.
        /// </summary>
        /// <param name="VerbLemma">Lemma actually conjugated; often the perfective counterpart of the act's lemma.</param>
        /// <param name="Pattern">GM verb class (<c>trida1</c>–<c>trida5</c>), verified against the composer.</param>
        /// <param name="Aspect">Verb aspect.</param>
        /// <param name="Reflexive">Reflexive type; GM places the resulting se/si clitic itself.</param>
        /// <param name="AddresseeFunctor">FGD functor the addressee fills, or null when the act names no addressee.</param>
        /// <param name="AddresseeCase">Case of the addressee slot — supplied here only until GM has a valency frame.</param>
        /// <param name="Preposition">Preposition opening the addressee constituent, if any; GM vocalizes it.</param>
        private readonly record struct ClauseSpec(
            string VerbLemma,
            string Pattern,
            VerbAspect Aspect,
            ReflexiveType Reflexive,
            FgdFunctor? AddresseeFunctor,
            Case? AddresseeCase,
            string? Preposition);

        // Keyed by SpeechAct.PredicateLemma. Verb classes were verified empirically against
        // CzechWordFormComposer — GuessVerbClass is documented as unreliable, so they are pinned here
        // and locked down by tests rather than guessed at runtime.
        private static readonly IReadOnlyDictionary<string, ClauseSpec> Specs = new Dictionary<string, ClauseSpec>
        {
            // SmallTalk
            ["povídat si"] = new("povídat", "trida5", VerbAspect.Imperfective, ReflexiveType.ReflexivumTantum_Si, FgdFunctor.ADDR, Case.Instrumental, "s"),
            ["bavit se"] = new("bavit", "trida4", VerbAspect.Imperfective, ReflexiveType.DerivedReflexive_Se, FgdFunctor.ADDR, Case.Instrumental, "s"),

            // Question
            ["ptát se"] = new("zeptat", "trida5", VerbAspect.Perfective, ReflexiveType.ReflexivumTantum_Se, FgdFunctor.ADDR, Case.Genitive, null),
            ["vyptávat se"] = new("vyptávat", "trida5", VerbAspect.Imperfective, ReflexiveType.ReflexivumTantum_Se, FgdFunctor.ADDR, Case.Genitive, null),

            // SelfDisclosure
            ["svěřovat se"] = new("svěřit", "trida4", VerbAspect.Perfective, ReflexiveType.DerivedReflexive_Se, FgdFunctor.ADDR, Case.Dative, null),
            ["přiznávat"] = new("přiznat", "trida5", VerbAspect.Perfective, ReflexiveType.None, FgdFunctor.ADDR, Case.Dative, null),

            // Validation
            ["chválit"] = new("pochválit", "trida4", VerbAspect.Perfective, ReflexiveType.None, FgdFunctor.PAT, Case.Accusative, null),
            ["oceňovat"] = new("ocenit", "trida4", VerbAspect.Perfective, ReflexiveType.None, FgdFunctor.PAT, Case.Accusative, null),
            ["souhlasit"] = new("souhlasit", "trida4", VerbAspect.Imperfective, ReflexiveType.None, FgdFunctor.ADDR, Case.Instrumental, "s"),

            // Boundary
            ["odmítat"] = new("odmítnout", "trida2", VerbAspect.Perfective, ReflexiveType.None, null, null, null),
            ["ohrazovat se"] = new("ohradit", "trida4", VerbAspect.Perfective, ReflexiveType.DerivedReflexive_Se, null, null, null),

            // Humor
            ["žertovat"] = new("žertovat", "trida3", VerbAspect.Imperfective, ReflexiveType.None, FgdFunctor.ADDR, Case.Instrumental, "s"),
            ["vtipkovat"] = new("vtipkovat", "trida3", VerbAspect.Imperfective, ReflexiveType.None, null, null, null),

            // Meta
            ["rozebírat"] = new("rozebrat", "trida5", VerbAspect.Perfective, ReflexiveType.None, FgdFunctor.ADDR, Case.Instrumental, "s"),
            ["mluvit"] = new("mluvit", "trida4", VerbAspect.Imperfective, ReflexiveType.None, FgdFunctor.ADDR, Case.Instrumental, "s"),

            // Invite
            ["zvát"] = new("pozvat", "trida5", VerbAspect.Perfective, ReflexiveType.None, FgdFunctor.PAT, Case.Accusative, null),
            ["navrhovat"] = new("navrhnout", "trida2", VerbAspect.Perfective, ReflexiveType.None, FgdFunctor.ADDR, Case.Dative, null),

            // Request (Directive) — power-graded synonyms.
            ["požádat"] = new("požádat", "trida5", VerbAspect.Perfective, ReflexiveType.None, FgdFunctor.ADDR, Case.Accusative, null),
            ["vyžadovat"] = new("vyžadovat", "trida3", VerbAspect.Imperfective, ReflexiveType.None, FgdFunctor.ADDR, Case.Genitive, "od"),
            ["žebrat o"] = new("žebrat", "trida5", VerbAspect.Imperfective, ReflexiveType.None, FgdFunctor.ADDR, Case.Genitive, "u"),
        };

        private readonly CzechSentenceBuilder _builder;
        private readonly CzechWordFormComposer _composer;

        /// <summary>Creates the realizer over GM's sentence builder and word-form composer.</summary>
        /// <param name="builder">Builds a clause into a surface sentence (mode 1).</param>
        /// <param name="composer">Single-word forms — vocative address and pronouns (mode 2).</param>
        public TemporaryCzechActRealizer(CzechSentenceBuilder builder, CzechWordFormComposer composer)
        {
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
            _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        }

        /// <summary>
        /// <b>TEMPORARY.</b> Mode-1 render: a narrative Czech sentence describing the act
        /// ("Petr se zeptal Jany."), built as a <see cref="CzechClause"/> so GM derives every form and
        /// the word order. Falls back to a bracketed marker when the predicate has no clause spec.
        /// </summary>
        public string Realize(SpeechAct act, Person speaker, Person addressee)
        {
            ArgumentNullException.ThrowIfNull(act);

            if (!Specs.TryGetValue(act.PredicateLemma, out var spec))
            {
                return Fallback(act, speaker, addressee);
            }

            try
            {
                var elements = new List<ClauseElement>
                {
                    // The speaker is the topic — given material, so it precedes the verb and the clitic
                    // cluster settles right after it.
                    ClauseElement.Of(NameOf(speaker, Case.Nominative), FgdFunctor.ACT, InformationStatus.Given),
                };

                if (spec.AddresseeCase is { } addresseeCase)
                {
                    var word = NameOf(addressee, addresseeCase);
                    var functor = spec.AddresseeFunctor ?? FgdFunctor.ADDR;

                    // A prepositional phrase is one constituent; GM vocalizes the preposition itself.
                    elements.Add(spec.Preposition is null
                        ? ClauseElement.Of(word, functor)
                        : ClauseElement.Of(spec.Preposition, word, functor));
                }

                return _builder.Build(new CzechClause
                {
                    Predicate = PredicateOf(spec, speaker),
                    Elements = elements,
                });
            }
            catch
            {
                // Preview only — a builder edge case must never break the caller.
                return Fallback(act, speaker, addressee);
            }
        }

        /// <summary>
        /// <b>TEMPORARY.</b> Mode-2 render: the DIRECT SPEECH a speaker utters to the addressee —
        /// vocative address plus an authored idiom skeleton, with tykání/vykání per <see cref="Register"/>
        /// ("Jano, nezajdeš se mnou?" vs "Jano, nezašla byste se mnou?"). GM supplies the morphology
        /// inside the skeleton; the skeleton itself is conventional wording a generator cannot derive.
        /// Falls back to a bracketed marker for lemmas outside the table.
        /// </summary>
        public string RealizeDirectSpeech(SpeechAct act, Person speaker, Person addressee)
        {
            ArgumentNullException.ThrowIfNull(act);

            var vocative = Decline(addressee, Case.Vocative);
            var formal = act.Register == Register.Formal;
            var line = BuildDirectSpeech(act.PredicateLemma, vocative, formal, addressee.IsFemale);

            return line ?? Fallback(act, speaker, addressee);
        }

        private static string Fallback(SpeechAct act, Person speaker, Person addressee)
            => $"[{act.RelationalKind}] {speaker.Name} → {addressee.Name}";

        /// <summary>Builds the past-tense predicate; GM conjugates it and agrees it with the speaker's gender.</summary>
        private static CzechWordRequest PredicateOf(ClauseSpec spec, Person speaker) => new()
        {
            Lemma = spec.VerbLemma,
            WordCategory = WordCategory.Verb,
            Pattern = spec.Pattern,
            Aspect = spec.Aspect,
            Tense = Tense.Past,
            Modus = Modus.Indicative,
            Voice = Voice.Active,
            Person = GPerson.Third,
            Number = Number.Singular,
            Gender = speaker.IsFemale ? Gender.Feminine : Gender.Masculine,
            ReflexiveType = spec.Reflexive,
        };

        /// <summary>Builds the word request for a participant's name in <paramref name="case"/>.</summary>
        private static CzechWordRequest NameOf(Person person, Case @case) => new()
        {
            Lemma = person.Name,
            WordCategory = WordCategory.Noun,
            Case = @case,
            Number = Number.Singular,
            Gender = person.IsFemale ? Gender.Feminine : Gender.Masculine,
            IsAnimate = true,
            Pattern = GuessPattern(person),
        };

        /// <summary>TEMPORARY per-lemma direct-speech skeletons (informal ⇒ tykání, formal ⇒ vykání).</summary>
        private string? BuildDirectSpeech(string lemma, string voc, bool formal, bool addresseeFemale) => lemma switch
        {
            // SmallTalk
            "povídat si" => formal ? $"{voc}, jak se máte?" : $"{voc}, jak se máš?",
            "bavit se" => formal ? $"Co je u {Pron(Case.Genitive, formal)} nového, {voc}?" : $"Tak co je nového, {voc}?",

            // Question
            "ptát se" => formal ? $"{voc}, smím se {Pron(Case.Genitive, formal)} na něco zeptat?" : $"{voc}, můžu se tě na něco zeptat?",
            "vyptávat se" => formal ? "A jak to bylo dál? Povídejte." : $"A jak to bylo dál, {voc}? Povídej.",

            // SelfDisclosure
            "svěřovat se" => formal ? $"{voc}, chci se {Pron(Case.Dative, formal)} s něčím svěřit." : $"{voc}, chci se ti s něčím svěřit.",
            "přiznávat" => formal ? $"{voc}, musím {Pron(Case.Dative, formal)} něco přiznat." : $"{voc}, musím ti něco přiznat.",

            // Validation
            "chválit" => formal ? $"{voc}, tohle se {Pron(Case.Dative, formal)} opravdu povedlo." : $"{voc}, tohle se ti fakt povedlo.",
            "oceňovat" => formal ? $"Vážím si {Pron(Case.Genitive, formal)}, {voc}." : $"Vážím si tě, {voc}.",
            "souhlasit" => $"Souhlasím s {Pron(Case.Instrumental, formal)}, {voc}.",

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

            // Request (Directive) — deferential ↔ dominant ↔ subordinate.
            "požádat" => $"{voc}, mám na {Pron(Case.Accusative, formal)} prosbu.",
            "vyžadovat" => formal ? $"{voc}, tohle od {Pron(Case.Genitive, formal)} žádám." : $"{voc}, tohle po tobě chci.",
            "žebrat o" => formal ? $"Snažně {Pron(Case.Accusative, formal)} prosím, {voc}…" : $"Moc tě prosím, {voc}…",

            _ => null,
        };

        /// <summary>
        /// Second-person pronoun in <paramref name="case"/>, tykání or vykání per <paramref name="formal"/>.
        /// </summary>
        /// <remarks>
        /// GM returns the long (stressed) forms — <i>tebe</i>, <i>tobě</i>, <i>tebou</i> — which are the
        /// correct ones after a preposition and in the emphatic slots used above. The short clitics
        /// (<i>tě</i>, <i>ti</i>) stay written into the informal skeletons, because those are unstressed
        /// positions where the long form would read as contrastive.
        /// </remarks>
        private string Pron(Case @case, bool formal)
        {
            try
            {
                return _composer.GetFullForm(new CzechWordRequest
                {
                    Lemma = formal ? "vy" : "ty",
                    WordCategory = WordCategory.Pronoun,
                    Case = @case,
                    Number = formal ? Number.Plural : Number.Singular,
                    Person = GPerson.Second,
                }).Form;
            }
            catch
            {
                return formal ? "vás" : "tebe";
            }
        }

        /// <summary>Declines a participant name into <paramref name="case"/> via a CzechWordRequest.</summary>
        private string Decline(Person person, Case @case)
        {
            try
            {
                return _composer.GetFullForm(NameOf(person, @case)).Form;
            }
            catch
            {
                // Best-effort preview only — never let a composer edge case break the caller.
                return person.Name;
            }
        }

        /// <summary>
        /// Picks a declension pattern for a participant's name.
        /// </summary>
        /// <remarks>
        /// Always resolves to a pattern rather than to an indeclinable request: GM's clause path
        /// dereferences the pattern and throws on <c>IsIndeclinable</c>, and the world's invented
        /// masculine names (Ignifer, Arbir, Stellir) would otherwise take the whole sentence down. The
        /// soft-consonant test picks <c>muž</c> over <c>pán</c>, as for Czech masculine animates.
        /// </remarks>
        private static string GuessPattern(Person person) => person switch
        {
            { IsFemale: true, Name: var n } when n.EndsWith("a") => "žena",
            { IsFemale: true, Name: var n } when n.EndsWith("e") => "růže",
            { IsFemale: true } => "žena",
            { Name: var n } when n.EndsWith("us") || EndsWithSoftConsonant(n) => "muž",
            _ => "pán",
        };

        /// <summary>True when the name ends in a soft consonant, which puts a masculine animate on <c>muž</c>.</summary>
        private static bool EndsWithSoftConsonant(string name)
            => name.Length > 0 && "šžčřcjďťň".Contains(char.ToLowerInvariant(name[^1]));
    }
}
