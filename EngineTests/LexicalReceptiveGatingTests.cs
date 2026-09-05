// LexicalReceptiveGatingTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Language;
    using GameEngineTools.Dialogue.Interpretation;
    using GameEngineTools.Dialogue.Semantics;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;

    /// <summary>
    /// Phase 2: a stock ironic phrase is only pre-packaged for someone who knows it.
    /// </summary>
    /// <remarks>
    /// Giora's Graded Salience lets a conventionally ironic phrase bypass the ToM/familiarity gate,
    /// because its ironic reading is the salient one. Salience is a property of a phrase <i>for
    /// someone</i>, though — the receptive gate scales the population-level conventionality by the
    /// listener's own grip on the lemma, so an unknown word cannot ride the bypass however common it is.
    /// </remarks>
    [TestClass]
    public class LexicalReceptiveGatingTests : TestBase
    {
        private static readonly HumanId Speaker = new(Guid.Parse("aaaaaaaa-6666-6666-6666-666666666666"));
        private static readonly HumanId Knows = new(Guid.Parse("bbbbbbbb-7777-7777-7777-777777777777"));
        private static readonly HumanId Stranger = new(Guid.Parse("cccccccc-8888-8888-8888-888888888888"));

        /// <summary>A conventionally ironic Czech phrase from the curated lexicon.</summary>
        private const string ConventionalIrony = "to se povedlo";

        private static readonly ForceShift IronicShift = new(IllocutionaryPoint.Expressive, Polarity.Negative);

        private static WDateTime Now => WDateTime.New(WDateOnly.New(100, 1, 1));

        private static SpeechAct IronicAct() =>
            SpeechAct.Relational(RelationalActKind.Validation, Speaker, Knows, Now)
                with
            {
                PredicateLemma = ConventionalIrony,
                Directness = Directness.Neutral,
                Polarity = Polarity.Affirmative,
                ForceShift = IronicShift,
            };

        /// <summary>A listener too literal-minded to decode irony unaided — only the bypass can save them.</summary>
        private static ListenerContext LowTom(HumanId id)
            => new(TheoryOfMindLevel: 1, FamiliarityWithSpeaker: 10, Hostility: 0.0, Resolver: null, ListenerId: id);

        private static DefaultSpeechActInterpreter Interpreter(ILexicalAcquisitionStore? store)
            => new(
                new SpeechActInterpreterConfig(EnableConnotationLayer: true),
                new CuratedConnotationLexicon(),
                store);

        // ──────────────────────────────────────────────────────────────────────
        // The gate
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Appraise_SamePhrase_DecodedByWhoKnowsIt_ReadLiterallyByWhoDoesNot()
        {
            var store = new DefaultLexicalAcquisitionStore();

            // One listener has met the phrase many times; the other has never heard it.
            for (var i = 0; i < 8; i++)
            {
                store.Reinforce(Knows, ConventionalIrony, Now, successfulUse: true, learnedFrom: Speaker);
            }

            var interpreter = Interpreter(store);
            var act = IronicAct();

            var knows = interpreter.Appraise(act, LowTom(Knows));
            var stranger = interpreter.Appraise(act, LowTom(Stranger));

            Assert.AreEqual(
                Polarity.Affirmative, knows.PerceivedPolarity,
                "someone who knows the phrase hears the irony, despite low ToM");
            Assert.AreEqual(
                Polarity.Negative, stranger.PerceivedPolarity,
                "someone meeting the words for the first time takes them at face value");
        }

        [TestMethod]
        public void Appraise_ForgottenPhrase_StopsRidingTheBypass()
        {
            var store = new DefaultLexicalAcquisitionStore();
            for (var i = 0; i < 8; i++)
            {
                store.Reinforce(Knows, ConventionalIrony, Now, successfulUse: true, learnedFrom: Speaker);
            }

            var interpreter = Interpreter(store);

            // Long after last hearing it, the phrase is no longer salient for this listener.
            var muchLater = new WDateTime(Now.WorldTicks + WTimeSpan.FromDays(1).Ticks * 400);
            var staleAct = IronicAct() with { OccurredAt = muchLater };

            Assert.AreEqual(
                Polarity.Negative,
                interpreter.Appraise(staleAct, LowTom(Knows)).PerceivedPolarity,
                "conventionality that has decayed out of memory cannot carry the bypass");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Everything below the gate must be untouched when the layer cannot speak
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Appraise_WithoutStore_IsIdenticalToPreAcquisitionBehaviour()
        {
            var withoutStore = Interpreter(store: null);
            var act = IronicAct();

            // No store at all: the phrase decodes on population conventionality alone, as before.
            Assert.AreEqual(Polarity.Affirmative, withoutStore.Appraise(act, LowTom(Knows)).PerceivedPolarity);
        }

        [TestMethod]
        public void Appraise_StoreWiredButNoListenerId_FallsBackToPopulationConventionality()
        {
            // Psychology and Memory build listener contexts through the shared factory, which supplies an
            // id — but a context built by hand (as older tests do) has none, and must not silently lose
            // the bypass.
            var anonymous = new ListenerContext(TheoryOfMindLevel: 1, FamiliarityWithSpeaker: 10, Hostility: 0.0);

            Assert.AreEqual(
                Polarity.Affirmative,
                Interpreter(new DefaultLexicalAcquisitionStore()).Appraise(IronicAct(), anonymous).PerceivedPolarity);
        }

        [TestMethod]
        public void Appraise_ConnotationLayerOff_GateHasNoEffectEitherWay()
        {
            // The gate lives inside the connotation block, so with the flag off there is nothing to gate
            // and a listener who knows the word must read exactly like one who does not.
            var store = new DefaultLexicalAcquisitionStore();
            for (var i = 0; i < 8; i++)
            {
                store.Reinforce(Knows, ConventionalIrony, Now, successfulUse: true, learnedFrom: Speaker);
            }

            var off = new DefaultSpeechActInterpreter(
                new SpeechActInterpreterConfig(EnableConnotationLayer: false),
                new CuratedConnotationLexicon(),
                store);

            var act = IronicAct();
            var knows = off.Appraise(act, LowTom(Knows));
            var stranger = off.Appraise(act, LowTom(Stranger));

            Assert.AreEqual(knows.PerceivedPolarity, stranger.PerceivedPolarity);
            Assert.AreEqual(knows.PerceivedPoint, stranger.PerceivedPoint);
            Assert.AreEqual(knows.ConnotationDelta, stranger.ConnotationDelta);
            Assert.AreEqual(
                Polarity.Negative, knows.PerceivedPolarity,
                "with the layer off, low ToM means the irony is missed by everyone");
        }

        [TestMethod]
        public void Appraise_HighTomListener_DecodesIronyWithoutKnowingThePhrase()
        {
            // The gate only governs the conventionality bypass. Someone with the perspective-taking to
            // work irony out unaided still does, vocabulary or not.
            var capable = new ListenerContext(
                TheoryOfMindLevel: 4, FamiliarityWithSpeaker: 90, Hostility: 0.0, Resolver: null, ListenerId: Stranger);

            Assert.AreEqual(
                Polarity.Affirmative,
                Interpreter(new DefaultLexicalAcquisitionStore()).Appraise(IronicAct(), capable).PerceivedPolarity);
        }
    }
}
