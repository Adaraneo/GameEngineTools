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
            var composer = CzechGrammarServiceFactory
                .AddCzechGrammarServices(new ServiceCollection())
                .BuildServiceProvider()
                .GetRequiredService<CzechWordFormComposer>();
            return new TemporaryCzechActRealizer(composer);
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
    }
}
