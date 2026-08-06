// CzechSpeechActRealizer.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Realization
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
    /// Renders speech acts into Czech through GrammarModular's sentence layer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The rule this file is built around: no inflected form is ever written by hand.</b> Every verb
    /// form, every declined name and pronoun, the placement of the <i>se</i>/<i>si</i> clitic, preposition
    /// vocalisation, agreement and word order come from GM. What is authored here is structure — which
    /// verb, in which person, negated or not, with which complement — plus the occasional uninflected
    /// particle. That line is what keeps the Czech correct as the grammar library improves, rather than
    /// frozen at whatever was typed into a table.
    /// </para>
    /// <para>
    /// Two readings of one act. <see cref="Narrate"/> is the observer's third-person past-tense account;
    /// <see cref="Utter"/> is what the speaker actually says, first person to second, carrying address
    /// and register. Neither ever re-enters simulation state.
    /// </para>
    /// <para>
    /// Where GM's valency lexicon has a frame for a predicate, the case and preposition are read off it
    /// (<c>mluvit</c> today). Everything else supplies them explicitly, which is the documented fallback
    /// for a verb without a frame; those entries shrink to a bare functor as the lexicon grows.
    /// </para>
    /// </remarks>
    public sealed class CzechSpeechActRealizer : ISpeechActRealizer
    {
        #region Narration model

        /// <summary>Grammatical metadata for narrating one predicate — never a surface form.</summary>
        private readonly record struct ClauseSpec(
            string VerbLemma,
            string Pattern,
            VerbAspect Aspect,
            ReflexiveType Reflexive,
            FgdFunctor? AddresseeFunctor,
            Case? AddresseeCase,
            string? Preposition);

        // Verb classes verified against the composer rather than guessed — GM documents GuessVerbClass
        // as unreliable, so they are pinned here and locked down by tests.
        //
        // Functor choice matters as the lexicon grows: GM licenses inner participants (ACT, PAT, ADDR,
        // ORIG, EFF) only through a frame and throws otherwise, while free modifications combine with
        // any verb. A companion introduced by "s" + instrumental is accompaniment, so it takes ACMP —
        // both the right analysis and the one that cannot start throwing when a frame appears.
        private static readonly IReadOnlyDictionary<string, ClauseSpec> Narrations = new Dictionary<string, ClauseSpec>
        {
            ["povídat si"] = new("povídat", "trida5", VerbAspect.Imperfective, ReflexiveType.ReflexivumTantum_Si, FgdFunctor.ACMP, Case.Instrumental, "s"),
            ["bavit se"] = new("bavit", "trida4", VerbAspect.Imperfective, ReflexiveType.DerivedReflexive_Se, FgdFunctor.ACMP, Case.Instrumental, "s"),
            ["ptát se"] = new("zeptat", "trida5", VerbAspect.Perfective, ReflexiveType.ReflexivumTantum_Se, FgdFunctor.ADDR, Case.Genitive, null),
            ["vyptávat se"] = new("vyptávat", "trida5", VerbAspect.Imperfective, ReflexiveType.ReflexivumTantum_Se, FgdFunctor.ADDR, Case.Genitive, null),
            ["svěřovat se"] = new("svěřit", "trida4", VerbAspect.Perfective, ReflexiveType.DerivedReflexive_Se, FgdFunctor.ADDR, Case.Dative, null),
            ["přiznávat"] = new("přiznat", "trida5", VerbAspect.Perfective, ReflexiveType.None, FgdFunctor.ADDR, Case.Dative, null),
            ["chválit"] = new("pochválit", "trida4", VerbAspect.Perfective, ReflexiveType.None, FgdFunctor.PAT, Case.Accusative, null),
            ["oceňovat"] = new("ocenit", "trida4", VerbAspect.Perfective, ReflexiveType.None, FgdFunctor.PAT, Case.Accusative, null),
            ["souhlasit"] = new("souhlasit", "trida4", VerbAspect.Imperfective, ReflexiveType.None, FgdFunctor.ACMP, Case.Instrumental, "s"),
            ["odmítat"] = new("odmítnout", "trida2", VerbAspect.Perfective, ReflexiveType.None, null, null, null),
            ["ohrazovat se"] = new("ohradit", "trida4", VerbAspect.Perfective, ReflexiveType.DerivedReflexive_Se, null, null, null),
            ["žertovat"] = new("žertovat", "trida3", VerbAspect.Imperfective, ReflexiveType.None, FgdFunctor.ACMP, Case.Instrumental, "s"),
            ["vtipkovat"] = new("vtipkovat", "trida3", VerbAspect.Imperfective, ReflexiveType.None, null, null, null),
            ["rozebírat"] = new("rozebrat", "trida5", VerbAspect.Perfective, ReflexiveType.None, FgdFunctor.ACMP, Case.Instrumental, "s"),

            // The one predicate whose frame is in GM's lexicon (ACT / ADDR s+7 / PAT o+6): it names only
            // the functor and lets the frame supply case and preposition. This is the shape every row
            // above moves to as the lexicon grows.
            ["mluvit"] = new("mluvit", "trida4", VerbAspect.Imperfective, ReflexiveType.None, FgdFunctor.ADDR, null, null),

            ["zvát"] = new("pozvat", "trida5", VerbAspect.Perfective, ReflexiveType.None, FgdFunctor.PAT, Case.Accusative, null),
            ["navrhovat"] = new("navrhnout", "trida2", VerbAspect.Perfective, ReflexiveType.None, FgdFunctor.ADDR, Case.Dative, null),
            ["požádat"] = new("požádat", "trida5", VerbAspect.Perfective, ReflexiveType.None, FgdFunctor.ADDR, Case.Accusative, null),
            ["vyžadovat"] = new("vyžadovat", "trida3", VerbAspect.Imperfective, ReflexiveType.None, FgdFunctor.ADDR, Case.Genitive, "od"),
            ["žebrat o"] = new("žebrat", "trida5", VerbAspect.Imperfective, ReflexiveType.None, FgdFunctor.ADDR, Case.Genitive, "u"),
        };

        #endregion Narration model

        #region Utterance model

        /// <summary>Who the utterance's verb is about: the speaker, or the person addressed.</summary>
        private enum Voiced
        {
            /// <summary>First person — "souhlasím", "zeptám se".</summary>
            Speaker,

            /// <summary>Second person, singular or plural by register — "máš" / "máte".</summary>
            Addressee,

            /// <summary>First person plural, for joint proposals — "promluvíme si".</summary>
            Together,
        }

        /// <summary>Where the address falls relative to the clause.</summary>
        private enum Address
        {
            /// <summary>"Jano, …".</summary>
            Front,

            /// <summary>"…, Jano".</summary>
            Back,
        }

        /// <summary>What the clause's one complement is, if any.</summary>
        private enum ComplementKind
        {
            None,

            /// <summary>A pronoun standing for the person addressed — ty/vy by register.</summary>
            Addressee,

            /// <summary>A pronoun standing for the speaker — já.</summary>
            Speaker,

            /// <summary>A concrete noun, e.g. "chvilku".</summary>
            Noun,
        }

        /// <summary>
        /// Structure of one utterance. Everything here is grammatical description or an uninflected
        /// particle — the surface forms are GM's to produce.
        /// </summary>
        private readonly record struct UtteranceSpec(
            string VerbLemma,
            string Pattern,
            VerbAspect Aspect,
            ReflexiveType Reflexive,
            Voiced Subject,
            ComplementKind Complement = ComplementKind.None,
            FgdFunctor ComplementFunctor = FgdFunctor.PAT,
            Case? ComplementCase = null,
            string? ComplementPreposition = null,
            string? NounLemma = null,
            string? NounPattern = null,
            bool Negated = false,
            Modus Mood = Modus.Indicative,
            string? Adverb = null,
            Address Address = Address.Back,
            string Terminator = ".");

        // Deliberately restricted to constructions GM renders correctly and completely: a verb in any
        // person/mood/polarity, first- and second-person pronouns, the addressee's name, one optional
        // adverb, one optional concrete noun. No modal-plus-infinitive and no indefinite pronouns —
        // those would need forms this file is not allowed to write by hand.
        private static readonly IReadOnlyDictionary<string, UtteranceSpec> Utterances = new Dictionary<string, UtteranceSpec>
        {
            // SmallTalk — "Jano, jak se máš?" / "Bavíš se, Jano?"
            ["povídat si"] = new("mít", "mít", VerbAspect.Imperfective, ReflexiveType.ReflexivumTantum_Se,
                Voiced.Addressee, Adverb: "jak", Address: Address.Front, Terminator: "?"),
            ["bavit se"] = new("bavit", "trida4", VerbAspect.Imperfective, ReflexiveType.DerivedReflexive_Se,
                Voiced.Addressee, Terminator: "?"),

            // Question — "Zeptám se, Jano." / "Povídej, Jano."
            // No pronoun complement: the addressee here would be genitive, and GM's genitive yields the
            // long stressed form ("tebe"), which reads as contrastive — "I'll ask YOU, not him".
            ["ptát se"] = new("zeptat", "trida5", VerbAspect.Perfective, ReflexiveType.ReflexivumTantum_Se,
                Voiced.Speaker),
            ["vyptávat se"] = new("povídat", "trida5", VerbAspect.Imperfective, ReflexiveType.None,
                Voiced.Addressee, Mood: Modus.Imperative),

            // SelfDisclosure — "Svěřím se ti, Jano." / "Přiznám se ti, Jano."
            ["svěřovat se"] = new("svěřit", "trida4", VerbAspect.Perfective, ReflexiveType.DerivedReflexive_Se,
                Voiced.Speaker, ComplementKind.Addressee, FgdFunctor.ADDR, Case.Dative),
            ["přiznávat"] = new("přiznat", "trida5", VerbAspect.Perfective, ReflexiveType.DerivedReflexive_Se,
                Voiced.Speaker, ComplementKind.Addressee, FgdFunctor.ADDR, Case.Dative),

            // Validation — "Chválím tě, Jano." / "Vážím si tě, Jano." / "Souhlasím s tebou, Jano."
            ["chválit"] = new("chválit", "trida4", VerbAspect.Imperfective, ReflexiveType.None,
                Voiced.Speaker, ComplementKind.Addressee, FgdFunctor.PAT, Case.Accusative),
            ["oceňovat"] = new("vážit", "trida4", VerbAspect.Imperfective, ReflexiveType.ReflexivumTantum_Si,
                Voiced.Speaker, ComplementKind.Addressee, FgdFunctor.PAT, Case.Genitive),
            ["souhlasit"] = new("souhlasit", "trida4", VerbAspect.Imperfective, ReflexiveType.None,
                Voiced.Speaker, ComplementKind.Addressee, FgdFunctor.ACMP, Case.Instrumental, "s"),

            // Boundary — "Odmítám, Jano." / "Ohrazuji se, Jano."
            ["odmítat"] = new("odmítat", "trida5", VerbAspect.Imperfective, ReflexiveType.None, Voiced.Speaker),
            ["ohrazovat se"] = new("ohrazovat", "trida3", VerbAspect.Imperfective, ReflexiveType.DerivedReflexive_Se,
                Voiced.Speaker),

            // Humor — "Žertuji, Jano." / "Vtipkuji, Jano."
            ["žertovat"] = new("žertovat", "trida3", VerbAspect.Imperfective, ReflexiveType.None, Voiced.Speaker),
            ["vtipkovat"] = new("vtipkovat", "trida3", VerbAspect.Imperfective, ReflexiveType.None, Voiced.Speaker),

            // Meta — "Promluvíme si, Jano." / "Máš chvilku, Jano?"
            ["rozebírat"] = new("promluvit", "trida4", VerbAspect.Perfective, ReflexiveType.ReflexivumTantum_Si,
                Voiced.Together),
            ["mluvit"] = new("mít", "mít", VerbAspect.Imperfective, ReflexiveType.None,
                Voiced.Addressee, ComplementKind.Noun, FgdFunctor.PAT, Case.Accusative,
                NounLemma: "chvilka", NounPattern: "žena", Terminator: "?"),

            // Invite — "Jano, nezajdeš se mnou?" / "Sejdeme se, Jano?"
            ["zvát"] = new("zajít", "trida1", VerbAspect.Perfective, ReflexiveType.None,
                Voiced.Addressee, ComplementKind.Speaker, FgdFunctor.ACMP, Case.Instrumental, "s",
                Negated: true, Address: Address.Front, Terminator: "?"),
            ["navrhovat"] = new("sejít", "trida1", VerbAspect.Perfective, ReflexiveType.ReflexivumTantum_Se,
                Voiced.Together, Terminator: "?"),

            // Request — power-graded: "Požádám tě" / "Žádám tě" / "Moc tě prosím".
            ["požádat"] = new("požádat", "trida5", VerbAspect.Perfective, ReflexiveType.None,
                Voiced.Speaker, ComplementKind.Addressee, FgdFunctor.PAT, Case.Accusative),
            ["vyžadovat"] = new("žádat", "trida5", VerbAspect.Imperfective, ReflexiveType.None,
                Voiced.Speaker, ComplementKind.Addressee, FgdFunctor.PAT, Case.Accusative),
            // The intensifier goes INSIDE the clause. Left outside it would strand the clitic
            // ("Moc prosím tě") — nothing belonging to the sentence may sit in front of the builder.
            ["žebrat o"] = new("prosit", "trida4", VerbAspect.Imperfective, ReflexiveType.None,
                Voiced.Speaker, ComplementKind.Addressee, FgdFunctor.PAT, Case.Accusative,
                Adverb: "moc", Terminator: "…"),
        };

        #endregion Utterance model

        private readonly CzechSentenceBuilder _builder;
        private readonly CzechWordFormComposer _composer;

        /// <summary>Creates the realizer over GM's sentence builder and word-form composer.</summary>
        /// <param name="builder">Builds a clause into a surface sentence.</param>
        /// <param name="composer">Single-word forms — the vocative address.</param>
        public CzechSpeechActRealizer(CzechSentenceBuilder builder, CzechWordFormComposer composer)
        {
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
            _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        }

        /// <inheritdoc/>
        public string Narrate(SpeechAct act, Participant speaker, Participant addressee)
        {
            ArgumentNullException.ThrowIfNull(act);

            if (!Narrations.TryGetValue(act.PredicateLemma, out var spec))
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

                if (spec.AddresseeFunctor is { } functor)
                {
                    // A null case means the predicate has a valency frame: GM reads the case AND the
                    // preposition off the frame, which is where that belongs.
                    var word = NameOf(addressee, spec.AddresseeCase);

                    elements.Add(spec.Preposition is null
                        ? ClauseElement.Of(word, functor)
                        : ClauseElement.Of(spec.Preposition, word, functor));
                }

                return _builder.Build(new CzechClause
                {
                    Predicate = new CzechWordRequest
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
                    },
                    Elements = elements,
                });
            }
            catch
            {
                // Player-facing only — a builder edge case must never break the caller.
                return Fallback(act, speaker, addressee);
            }
        }

        /// <inheritdoc/>
        public string Utter(SpeechAct act, Participant speaker, Participant addressee)
        {
            ArgumentNullException.ThrowIfNull(act);

            if (!Utterances.TryGetValue(act.PredicateLemma, out var spec))
            {
                return Fallback(act, speaker, addressee);
            }

            try
            {
                return BuildUtterance(spec, act.Register == Register.Formal, speaker, addressee);
            }
            catch
            {
                return Fallback(act, speaker, addressee);
            }
        }

        /// <summary>
        /// Assembles one utterance: GM builds the clause, this places the address and punctuation around it.
        /// </summary>
        /// <remarks>
        /// The clause is built with an empty terminator so punctuation can be decided here — a trailing
        /// address has to land inside the sentence ("Bavíš se, Jano?"), which is not something the
        /// builder can know about.
        /// </remarks>
        private string BuildUtterance(UtteranceSpec spec, bool formal, Participant speaker, Participant addressee)
        {
            // Vykání is second person PLURAL — that is the whole of the distinction, and it carries
            // through to the pronoun (ty ⇒ vy) as well as the verb.
            var addresseeNumber = formal ? Number.Plural : Number.Singular;

            var (person, number) = spec.Subject switch
            {
                Voiced.Speaker => (GPerson.First, Number.Singular),
                Voiced.Together => (GPerson.First, Number.Plural),
                _ => (GPerson.Second, addresseeNumber),
            };

            var predicate = new CzechWordRequest
            {
                Lemma = spec.VerbLemma,
                WordCategory = WordCategory.Verb,
                Pattern = spec.Pattern,
                Aspect = spec.Aspect,
                Modus = spec.Mood,
                Tense = spec.Mood == Modus.Imperative ? null : Tense.Present,
                Voice = Voice.Active,
                Person = person,
                Number = number,
                IsNegative = spec.Negated,
                ReflexiveType = spec.Reflexive,
            };

            var elements = new List<ClauseElement>();

            // An interrogative adverb has to sit inside the clause, not in front of it: the clitic
            // cluster follows the first constituent, so "jak" outside would strand it ("Jak máš se?").
            if (spec.Adverb is { } adverb)
            {
                elements.Add(ClauseElement.Of(
                    new CzechWordRequest { Lemma = adverb, WordCategory = WordCategory.Adverb, IsIndeclinable = true },
                    FgdFunctor.MANN,
                    InformationStatus.Contrastive));
            }

            if (ComplementOf(spec, addresseeNumber) is { } complement)
            {
                elements.Add(spec.ComplementPreposition is null
                    ? ClauseElement.Of(complement, spec.ComplementFunctor)
                    : ClauseElement.Of(spec.ComplementPreposition, complement, spec.ComplementFunctor));
            }

            var clause = _builder.Build(new CzechClause
            {
                Predicate = predicate,
                Elements = elements,
                Terminator = string.Empty,
            });

            var vocative = Decline(addressee, Case.Vocative);

            return spec.Address == Address.Front
                ? $"{vocative}, {Decapitalise(clause)}{spec.Terminator}"
                : $"{clause}, {vocative}{spec.Terminator}";
        }

        /// <summary>Builds the clause's single complement, or null when it has none.</summary>
        private static CzechWordRequest? ComplementOf(UtteranceSpec spec, Number addresseeNumber) => spec.Complement switch
        {
            ComplementKind.Addressee => Pronoun(
                addresseeNumber == Number.Plural ? "vy" : "ty",
                GPerson.Second,
                addresseeNumber,
                spec.ComplementCase,
                spec.ComplementPreposition is not null),

            ComplementKind.Speaker => Pronoun(
                "já", GPerson.First, Number.Singular, spec.ComplementCase, spec.ComplementPreposition is not null),

            ComplementKind.Noun => new CzechWordRequest
            {
                Lemma = spec.NounLemma!,
                WordCategory = WordCategory.Noun,
                Pattern = spec.NounPattern,
                Gender = Gender.Feminine,
                Number = Number.Singular,
                Case = spec.ComplementCase,
                IsAnimate = false,
            },

            _ => null,
        };

        private static CzechWordRequest Pronoun(
            string lemma, GPerson person, Number number, Case? @case, bool afterPreposition) => new()
            {
                Lemma = lemma,
                WordCategory = WordCategory.Pronoun,
                Person = person,
                Number = number,
                Case = @case,
                IsAfterPreposition = afterPreposition,
            };

        private static string Fallback(SpeechAct act, Participant speaker, Participant addressee)
            => $"[{act.RelationalKind}] {speaker.Name} → {addressee.Name}";

        /// <summary>Builds the word request for a participant's name; a null case leaves it to the frame.</summary>
        private static CzechWordRequest NameOf(Participant person, Case? @case) => new()
        {
            Lemma = person.Name,
            WordCategory = WordCategory.Noun,
            Case = @case,
            Number = Number.Singular,
            Gender = person.IsFemale ? Gender.Feminine : Gender.Masculine,
            IsAnimate = true,
            Pattern = GuessPattern(person),
        };

        private string Decline(Participant person, Case @case)
        {
            try
            {
                return _composer.GetFullForm(NameOf(person, @case)).Form;
            }
            catch
            {
                return person.Name;
            }
        }

        /// <summary>
        /// Picks a declension pattern for a participant's name.
        /// </summary>
        /// <remarks>
        /// Always resolves to a pattern rather than an indeclinable request: GM's clause path
        /// dereferences the pattern and throws on <c>IsIndeclinable</c>, and the world's invented
        /// masculine names (Ignifer, Arbir, Stellir) would otherwise take the whole sentence down. The
        /// soft-consonant test picks <c>muž</c> over <c>pán</c>, as for Czech masculine animates.
        /// </remarks>
        private static string GuessPattern(Participant person) => person switch
        {
            { IsFemale: true, Name: var n } when n.EndsWith("a") => "žena",
            { IsFemale: true, Name: var n } when n.EndsWith("e") => "růže",
            { IsFemale: true } => "žena",
            { Name: var n } when n.EndsWith("us") || EndsWithSoftConsonant(n) => "muž",
            _ => "pán",
        };

        private static bool EndsWithSoftConsonant(string name)
            => name.Length > 0 && "šžčřcjďťň".Contains(char.ToLowerInvariant(name[^1]));

        private static string Decapitalise(string value)
            => value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value[1..];
    }
}
