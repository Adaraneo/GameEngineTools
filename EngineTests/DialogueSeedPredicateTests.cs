// DialogueSeedPredicateTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Dialogue.Seed;
    using GameEngineTools.Dialogue.Temporary;
    using GameEngineTools.World.Utils.Time;
    using Grammar.Czech;
    using Grammar.Czech.Services;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Covers the Phase-3 seed predicate mapping and the TEMPORARY CzechWordRequest realizer: the
    /// lexicon must have candidates for every act kind, and the stopgap realizer must produce
    /// correctly-declined Czech for the representative predicates.
    /// </summary>
    [TestClass]
    public class DialogueSeedPredicateTests
    {
        private static TemporaryCzechActRealizer BuildRealizer()
        {
            var grammar = CzechGrammarServiceFactory
                .AddCzechGrammarServices(new ServiceCollection())
                .BuildServiceProvider();
            return new TemporaryCzechActRealizer(
                grammar.GetRequiredService<CzechSentenceBuilder>(),
                grammar.GetRequiredService<CzechWordFormComposer>());
        }

        private static SpeechAct ActWithLemma(string imperfectiveLemma, RelationalActKind kind)
            => SpeechAct.Relational(kind, new HumanId(Guid.NewGuid()), new HumanId(Guid.NewGuid()), new WDateTime(0))
                with { PredicateLemma = imperfectiveLemma };

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
        public void TemporaryRealizer_MaleSpeakerFemaleAddressee_ProducesDeclinedCzech(
            string lemma, RelationalActKind kind, string expected)
        {
            var realizer = BuildRealizer();
            var act = ActWithLemma(lemma, kind);

            var text = realizer.Realize(
                act,
                new TemporaryCzechActRealizer.Person("Petr", IsFemale: false),
                new TemporaryCzechActRealizer.Person("Jana", IsFemale: true));

            Assert.AreEqual(expected, text);
        }

        // Mode-2 direct speech: vocative address + tykání/vykání per Register — what the character SAYS,
        // as opposed to the mode-1 narrative gloss above.
        [DataTestMethod]
        [DataRow("zvát", RelationalActKind.Invite, Register.Informal, "Jano, nezajdeš se mnou?")]
        [DataRow("zvát", RelationalActKind.Invite, Register.Formal, "Jano, nezašla byste se mnou?")]
        [DataRow("ptát se", RelationalActKind.Question, Register.Informal, "Jano, můžu se tě na něco zeptat?")]
        [DataRow("chválit", RelationalActKind.Validation, Register.Formal, "Jano, tohle se vám opravdu povedlo.")]
        [DataRow("souhlasit", RelationalActKind.Validation, Register.Informal, "Souhlasím s tebou, Jano.")]
        [DataRow("vyžadovat", RelationalActKind.Request, Register.Informal, "Jano, tohle po tobě chci.")]
        [DataRow("žebrat o", RelationalActKind.Request, Register.Informal, "Moc tě prosím, Jano…")]
        [DataRow("požádat", RelationalActKind.Request, Register.Formal, "Jano, mám na vás prosbu.")]
        public void TemporaryRealizer_DirectSpeech_UsesVocativeAndRegister(
            string lemma, RelationalActKind kind, Register register, string expected)
        {
            var realizer = BuildRealizer();
            var act = ActWithLemma(lemma, kind) with { Register = register };

            var text = realizer.RealizeDirectSpeech(
                act,
                new TemporaryCzechActRealizer.Person("Petr", IsFemale: false),
                new TemporaryCzechActRealizer.Person("Jana", IsFemale: true));

            Assert.AreEqual(expected, text);
        }

        [TestMethod]
        public void TemporaryRealizer_DirectSpeech_UnknownLemma_FallsBackToMarker()
        {
            var realizer = BuildRealizer();
            var act = ActWithLemma("blábolit", RelationalActKind.SmallTalk);

            var text = realizer.RealizeDirectSpeech(
                act,
                new TemporaryCzechActRealizer.Person("Petr", false),
                new TemporaryCzechActRealizer.Person("Jana", true));

            StringAssert.Contains(text, "SmallTalk");
        }

        [TestMethod]
        public void TemporaryRealizer_UnknownLemma_FallsBackToMarker()
        {
            var realizer = BuildRealizer();
            var act = ActWithLemma("blábolit", RelationalActKind.SmallTalk);

            var text = realizer.Realize(
                act,
                new TemporaryCzechActRealizer.Person("Petr", false),
                new TemporaryCzechActRealizer.Person("Jana", true));

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
        public void TemporaryRealizer_FemaleSpeakerMaleAddressee_AgreesAndDeclines(
            string lemma, RelationalActKind kind, string expected)
        {
            var realizer = BuildRealizer();

            var text = realizer.Realize(
                ActWithLemma(lemma, kind),
                new TemporaryCzechActRealizer.Person("Jana", IsFemale: true),
                new TemporaryCzechActRealizer.Person("Petr", IsFemale: false));

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
        public void TemporaryRealizer_InventedWorldName_DeclinesInsteadOfFallingBack(string name, string expected)
        {
            var realizer = BuildRealizer();

            var text = realizer.Realize(
                ActWithLemma("ptát se", RelationalActKind.Question),
                new TemporaryCzechActRealizer.Person("Jana", IsFemale: true),
                new TemporaryCzechActRealizer.Person(name, IsFemale: false));

            Assert.AreEqual(expected, text);
        }

        // 'mluvit' is the first predicate whose valency frame lives in GM's lexicon (ACT / ADDR s+7 /
        // PAT o+6), so its clause spec names only the functor and leaves case and preposition null —
        // GM fills both from the frame. If the lexicon regresses, this produces "Petr mluvil." (the
        // addressee silently vanishing) or the fallback marker, rather than a wrong-looking sentence.
        [DataTestMethod]
        [DataRow(false, "Petr mluvil s Janou.")]
        [DataRow(true, "Jana mluvila s Petrem.")]
        public void TemporaryRealizer_FramedPredicate_TakesCaseAndPrepositionFromValencyFrame(
            bool femaleSpeaker, string expected)
        {
            var realizer = BuildRealizer();
            var jana = new TemporaryCzechActRealizer.Person("Jana", IsFemale: true);
            var petr = new TemporaryCzechActRealizer.Person("Petr", IsFemale: false);

            var text = realizer.Realize(
                ActWithLemma("mluvit", RelationalActKind.Meta),
                femaleSpeaker ? jana : petr,
                femaleSpeaker ? petr : jana);

            Assert.AreEqual(expected, text);
        }

        // Sweep guard: every seed predicate must realize in BOTH gender directions. Catches a clause
        // spec whose verb class or case sends GM down the fallback path, which a spot-check would miss.
        [TestMethod]
        public void TemporaryRealizer_EverySeedPredicate_RealizesInBothDirections()
        {
            var realizer = BuildRealizer();
            var jana = new TemporaryCzechActRealizer.Person("Jana", IsFemale: true);
            var petr = new TemporaryCzechActRealizer.Person("Petr", IsFemale: false);

            foreach (var (kind, candidates) in SeedPredicateLexicon.Predicates)
            {
                foreach (var candidate in candidates)
                {
                    var act = ActWithLemma(candidate.LemmaImperfective, kind);

                    foreach (var (speaker, addressee) in new[] { (petr, jana), (jana, petr) })
                    {
                        var narrative = realizer.Realize(act, speaker, addressee);
                        Assert.IsFalse(
                            narrative.StartsWith('['),
                            $"'{candidate.LemmaImperfective}' ({speaker.Name}→{addressee.Name}) fell back to the marker: {narrative}");
                        StringAssert.EndsWith(narrative, ".", $"'{candidate.LemmaImperfective}' is not a finished sentence: {narrative}");

                        var spoken = realizer.RealizeDirectSpeech(act, speaker, addressee);
                        Assert.IsFalse(
                            spoken.StartsWith('['),
                            $"'{candidate.LemmaImperfective}' has no direct-speech skeleton ({speaker.Name}→{addressee.Name}).");
                    }
                }
            }
        }
    }
}
