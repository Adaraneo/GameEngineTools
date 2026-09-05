// DialogueSeedPredicateTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Dialogue.Realization;
    using GameEngineTools.Dialogue.Seed;
    using GameEngineTools.World.Utils.Time;
    using Grammar.Czech;
    using Grammar.Czech.Services;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;

    /// <summary>
    /// Covers the seed predicate mapping and <see cref="CzechSpeechActRealizer"/>: the lexicon must
    /// have a candidate for every act kind, and both readings of an act — the observer's account and
    /// the words themselves — must come out as correct Czech.
    ///
    /// Every expected string here is GM's output, not a form written by hand. That is the point: if
    /// the grammar library changes how it conjugates or declines, these fail loudly rather than
    /// letting the world quietly start speaking badly.
    /// </summary>
    [TestClass]
    public class DialogueSeedPredicateTests
    {
        private static ISpeechActRealizer BuildRealizer()
        {
            var grammar = CzechGrammarServiceFactory
                .AddCzechGrammarServices(new ServiceCollection())
                .BuildServiceProvider();
            return new CzechSpeechActRealizer(
                grammar.GetRequiredService<CzechSentenceBuilder>(),
                grammar.GetRequiredService<CzechWordFormComposer>());
        }

        private static SpeechAct ActWithLemma(string imperfectiveLemma, RelationalActKind kind)
            => SpeechAct.Relational(kind, new HumanId(Guid.NewGuid()), new HumanId(Guid.NewGuid()), new WDateTime(0))
                with
            { PredicateLemma = imperfectiveLemma };

        [TestMethod]
        public void SeedLexicon_CoversEveryRelationalActKind_WithAtLeastOneCandidate()
        {
            foreach (RelationalActKind kind in Enum.GetValues(typeof(RelationalActKind)))
            {
                Assert.IsTrue(
                    SeedPredicateLexicon.Predicates.TryGetValue(kind, out var candidates) && candidates.Count > 0,
                    $"No seed predicate for {kind}.");
            }
        }

        // Full coverage across cases the GM composer declines: accusative, genitive, dative (the
        // feminine dative-ě "Janě" was fixed in GrammarModular preview.17), instrumental, no-argument.
        [DataTestMethod]
        [DataRow("zvát", RelationalActKind.Invite, "Petr pozval Janu.")]
        [DataRow("ptát se", RelationalActKind.Question, "Petr se zeptal Jany.")]
        [DataRow("svěřovat se", RelationalActKind.SelfDisclosure, "Petr se svěřil Janě.")]
        [DataRow("chválit", RelationalActKind.Validation, "Petr pochválil Janu.")]
        [DataRow("souhlasit", RelationalActKind.Validation, "Petr souhlasil s Janou.")]
        [DataRow("odmítat", RelationalActKind.Boundary, "Petr odmítl.")]
        [DataRow("požádat", RelationalActKind.Request, "Petr požádal Janu.")]
        [DataRow("vyžadovat", RelationalActKind.Request, "Petr vyžadoval od Jany.")]
        [DataRow("žebrat o", RelationalActKind.Request, "Petr žebral u Jany.")]
        public void Realizer_MaleSpeakerFemaleAddressee_ProducesDeclinedCzech(
            string lemma, RelationalActKind kind, string expected)
        {
            var realizer = BuildRealizer();
            var act = ActWithLemma(lemma, kind);

            var text = realizer.Narrate(
                act,
                new Participant("Petr", IsFemale: false),
                new Participant("Jana", IsFemale: true));

            Assert.AreEqual(expected, text);
        }

        // What the character SAYS, as opposed to the account of it above. Vykání is second person
        // PLURAL throughout — verb and pronoun alike — which is the whole of the distinction.
        [DataTestMethod]
        [DataRow("zvát", RelationalActKind.Invite, Register.Informal, "Jano, nezajdeš se mnou?")]
        [DataRow("zvát", RelationalActKind.Invite, Register.Formal, "Jano, nezajdete se mnou?")]
        [DataRow("povídat si", RelationalActKind.SmallTalk, Register.Informal, "Jano, jak se máš?")]
        [DataRow("povídat si", RelationalActKind.SmallTalk, Register.Formal, "Jano, jak se máte?")]
        [DataRow("mluvit", RelationalActKind.Meta, Register.Informal, "Máš chvilku, Jano?")]
        [DataRow("vyptávat se", RelationalActKind.Question, Register.Formal, "Povídejte, Jano.")]
        [DataRow("chválit", RelationalActKind.Validation, Register.Formal, "Chválím vás, Jano.")]
        [DataRow("souhlasit", RelationalActKind.Validation, Register.Informal, "Souhlasím s tebou, Jano.")]
        [DataRow("vyžadovat", RelationalActKind.Request, Register.Informal, "Žádám tě, Jano.")]
        [DataRow("žebrat o", RelationalActKind.Request, Register.Informal, "Moc tě prosím, Jano…")]
        [DataRow("požádat", RelationalActKind.Request, Register.Formal, "Požádám vás, Jano.")]
        public void Realizer_DirectSpeech_UsesVocativeAndRegister(
            string lemma, RelationalActKind kind, Register register, string expected)
        {
            var realizer = BuildRealizer();
            var act = ActWithLemma(lemma, kind) with { Register = register };

            var text = realizer.Utter(
                act,
                new Participant("Petr", IsFemale: false),
                new Participant("Jana", IsFemale: true));

            Assert.AreEqual(expected, text);
        }

        [TestMethod]
        public void Realizer_DirectSpeech_UnknownLemma_FallsBackToMarker()
        {
            var realizer = BuildRealizer();
            var act = ActWithLemma("blábolit", RelationalActKind.SmallTalk);

            var text = realizer.Utter(
                act,
                new Participant("Petr", false),
                new Participant("Jana", true));

            StringAssert.Contains(text, "SmallTalk");
        }

        [TestMethod]
        public void Realizer_UnknownLemma_FallsBackToMarker()
        {
            var realizer = BuildRealizer();
            var act = ActWithLemma("blábolit", RelationalActKind.SmallTalk);

            var text = realizer.Narrate(
                act,
                new Participant("Petr", false),
                new Participant("Jana", true));

            StringAssert.Contains(text, "SmallTalk");
        }

        // ------------------------------------------------------------------
        // Mode 1 is built as a GM CzechClause, so the sentence layer — not this repo — owns verb
        // conjugation, gender agreement, name declension and clitic placement. These lock that down.
        // ------------------------------------------------------------------

        // The mirrored direction of the table above: the predicate must agree with a FEMALE speaker
        // and the MALE addressee must decline. Masculine addressees previously fell out of the
        // realizer entirely (they resolved to an indeclinable request, which GM's clause path throws on).
        [DataTestMethod]
        [DataRow("zvát", RelationalActKind.Invite, "Jana pozvala Petra.")]
        [DataRow("ptát se", RelationalActKind.Question, "Jana se zeptala Petra.")]
        [DataRow("svěřovat se", RelationalActKind.SelfDisclosure, "Jana se svěřila Petrovi.")]
        [DataRow("chválit", RelationalActKind.Validation, "Jana pochválila Petra.")]
        [DataRow("souhlasit", RelationalActKind.Validation, "Jana souhlasila s Petrem.")]
        [DataRow("odmítat", RelationalActKind.Boundary, "Jana odmítla.")]
        [DataRow("vyžadovat", RelationalActKind.Request, "Jana vyžadovala od Petra.")]
        [DataRow("žebrat o", RelationalActKind.Request, "Jana žebrala u Petra.")]
        public void Realizer_FemaleSpeakerMaleAddressee_AgreesAndDeclines(
            string lemma, RelationalActKind kind, string expected)
        {
            var realizer = BuildRealizer();

            var text = realizer.Narrate(
                ActWithLemma(lemma, kind),
                new Participant("Jana", IsFemale: true),
                new Participant("Petr", IsFemale: false));

            Assert.AreEqual(expected, text);
        }

        // The world's names are invented (Ignifer, Ventus, Arbmov, …), not dictionary Czech. Each must
        // still decline into a sentence rather than degrade to the bracketed fallback marker.
        [DataTestMethod]
        [DataRow("Mendominátor", "Jana se zeptala Mendominátora.")]
        [DataRow("Ventus", "Jana se zeptala Ventuse.")]
        [DataRow("Ignifer", "Jana se zeptala Ignifera.")]
        [DataRow("Arbmov", "Jana se zeptala Arbmova.")]
        [DataRow("Stellir", "Jana se zeptala Stellira.")]
        public void Realizer_InventedWorldName_DeclinesInsteadOfFallingBack(string name, string expected)
        {
            var realizer = BuildRealizer();

            var text = realizer.Narrate(
                ActWithLemma("ptát se", RelationalActKind.Question),
                new Participant("Jana", IsFemale: true),
                new Participant(name, IsFemale: false));

            Assert.AreEqual(expected, text);
        }

        // 'mluvit' is the first predicate whose valency frame lives in GM's lexicon (ACT / ADDR s+7 /
        // PAT o+6), so its clause spec names only the functor and leaves case and preposition null —
        // GM fills both from the frame. If the lexicon regresses, this produces "Petr mluvil." (the
        // addressee silently vanishing) or the fallback marker, rather than a wrong-looking sentence.
        [DataTestMethod]
        [DataRow(false, "Petr mluvil s Janou.")]
        [DataRow(true, "Jana mluvila s Petrem.")]
        public void Realizer_FramedPredicate_TakesCaseAndPrepositionFromValencyFrame(
            bool femaleSpeaker, string expected)
        {
            var realizer = BuildRealizer();
            var jana = new Participant("Jana", IsFemale: true);
            var petr = new Participant("Petr", IsFemale: false);

            var text = realizer.Narrate(
                ActWithLemma("mluvit", RelationalActKind.Meta),
                femaleSpeaker ? jana : petr,
                femaleSpeaker ? petr : jana);

            Assert.AreEqual(expected, text);
        }

        // Sweep guard: every seed predicate must realize in BOTH gender directions. Catches a clause
        // spec whose verb class or case sends GM down the fallback path, which a spot-check would miss.
        [TestMethod]
        public void Realizer_EverySeedPredicate_RealizesInBothDirections()
        {
            var realizer = BuildRealizer();
            var jana = new Participant("Jana", IsFemale: true);
            var petr = new Participant("Petr", IsFemale: false);

            foreach (var (kind, candidates) in SeedPredicateLexicon.Predicates)
            {
                foreach (var candidate in candidates)
                {
                    var act = ActWithLemma(candidate.LemmaImperfective, kind);

                    foreach (var (speaker, addressee) in new[] { (petr, jana), (jana, petr) })
                    {
                        var narrative = realizer.Narrate(act, speaker, addressee);
                        Assert.IsFalse(
                            narrative.StartsWith('['),
                            $"'{candidate.LemmaImperfective}' ({speaker.Name}→{addressee.Name}) fell back to the marker: {narrative}");
                        StringAssert.EndsWith(narrative, ".", $"'{candidate.LemmaImperfective}' is not a finished sentence: {narrative}");

                        var spoken = realizer.Utter(act, speaker, addressee);
                        Assert.IsFalse(
                            spoken.StartsWith('['),
                            $"'{candidate.LemmaImperfective}' has no direct-speech skeleton ({speaker.Name}→{addressee.Name}).");
                    }
                }
            }
        }
    }
}
