// LexicalProductionGatingTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Language;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Dialogue.Planning;
    using GameEngineTools.Dialogue.Seed;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Phase 4: a speaker reaches for words they actually have.
    /// </summary>
    /// <remarks>
    /// Acquisition <i>filters</i> rather than outvotes. Dominance says which word the speaker wants;
    /// vocabulary only says whether they can reach for it. Blending the two proportionally would let a
    /// domineering speaker come out pleading, which is deliberate, tested behaviour and must survive.
    /// </remarks>
    [TestClass]
    public class LexicalProductionGatingTests : TestBase
    {
        private static readonly HumanId Speaker = new(Guid.Parse("aaaaaaaa-9999-9999-9999-999999999999"));
        private static readonly HumanId Addressee = new(Guid.Parse("bbbbbbbb-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

        private static WDateTime Now => WDateTime.New(WDateOnly.New(100, 1, 1));

        private static SpeechActRequest Request(
            RelationalActKind intent,
            double power = 0.5,
            double agreeableness = 0.5,
            long tickOffset = 0)
            => new(
                intent,
                EntityRef.ForHuman(Speaker, "S"),
                EntityRef.ForHuman(Addressee, "A"),
                new WDateTime(Now.WorldTicks + tickOffset),
                Closeness: 40,
                Familiarity: 40,
                Agreeableness: agreeableness,
                Style: CommunicationStyle.Direct,
                Power: power,
                Urgency: 0.0);

        /// <summary>Drills a lemma until it is comfortably above the production threshold.</summary>
        private static void Learn(DefaultLexicalAcquisitionStore store, string lemma)
        {
            for (var i = 0; i < 10; i++)
            {
                store.Reinforce(Speaker, lemma, Now, successfulUse: true, learnedFrom: null);
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Blocking regression 1 — an empty vocabulary changes nothing
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Plan_EmptyStore_ChoosesExactlyAsAPlannerWithNoStoreAtAll()
        {
            var withoutStore = new DefaultSpeechActPlanner();
            var withEmptyStore = new DefaultSpeechActPlanner(null, new DefaultLexicalAcquisitionStore());

            // Sweep every act kind and many timestamps: the stable hash keys off time, so this covers
            // every bucket the pre-acquisition selection could land in.
            foreach (RelationalActKind intent in Enum.GetValues(typeof(RelationalActKind)))
            {
                for (long t = 0; t < 60; t++)
                {
                    var request = Request(intent, tickOffset: t);

                    Assert.AreEqual(
                        withoutStore.Plan(request).PredicateLemma,
                        withEmptyStore.Plan(request).PredicateLemma,
                        $"a character who knows nothing yet must speak exactly as before ({intent}, t={t})");
                }
            }
        }

        [TestMethod]
        public void Plan_EmptyStore_PreservesPowerDrivenRequestSelection()
        {
            var planner = new DefaultSpeechActPlanner(null, new DefaultLexicalAcquisitionStore());

            // The three Request verbs span the dominance range: žebrat o (−0.9), požádat (−0.2),
            // vyžadovat (+0.8). With no vocabulary to filter on, felt power alone decides.
            var domineering = planner.Plan(Request(RelationalActKind.Request, power: 1.0, agreeableness: 0.0));
            var powerless = planner.Plan(Request(RelationalActKind.Request, power: 0.0, agreeableness: 1.0));

            Assert.AreEqual("vyžadovat", domineering.PredicateLemma);
            Assert.AreEqual("žebrat o", powerless.PredicateLemma);
        }

        // ──────────────────────────────────────────────────────────────────────
        // The point of the phase
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Plan_DomineeringSpeakerWhoLacksTheWord_FallsBackToOneTheyHave()
        {
            var store = new DefaultLexicalAcquisitionStore();
            Learn(store, "požádat");   // they know how to ask — but not how to demand

            var planner = new DefaultSpeechActPlanner(null, store);
            var act = planner.Plan(Request(RelationalActKind.Request, power: 1.0, agreeableness: 0.0));

            Assert.AreEqual(
                "požádat",
                act.PredicateLemma,
                "wanting to demand is not the same as having the word for it");
        }

        [TestMethod]
        public void Plan_DomineeringSpeakerWhoHasTheWord_StillDemands()
        {
            var store = new DefaultLexicalAcquisitionStore();
            Learn(store, "požádat");
            Learn(store, "vyžadovat");

            var planner = new DefaultSpeechActPlanner(null, store);
            var act = planner.Plan(Request(RelationalActKind.Request, power: 1.0, agreeableness: 0.0));

            Assert.AreEqual(
                "vyžadovat",
                act.PredicateLemma,
                "vocabulary filters the choice; it must not overturn who the speaker is");
        }

        [TestMethod]
        public void Plan_WellDrilledLemma_DominatesTheSpeakersOrdinaryWordChoice()
        {
            // Validation has three candidates and no dominance spread, so availability alone shapes it.
            var store = new DefaultLexicalAcquisitionStore();
            Learn(store, "chválit");

            var planner = new DefaultSpeechActPlanner(null, store);

            var chosen = new Dictionary<string, int>();
            for (long t = 0; t < 300; t++)
            {
                var lemma = planner.Plan(Request(RelationalActKind.Validation, tickOffset: t)).PredicateLemma;
                chosen[lemma] = chosen.GetValueOrDefault(lemma) + 1;
            }

            var praise = chosen.GetValueOrDefault("chválit");
            Assert.IsTrue(
                praise > 150,
                $"the word they actually have should carry most of the time (got {praise}/300)");
            Assert.IsTrue(
                chosen.Count > 1,
                "but it must not collapse to a single word — the others stay reachable");
        }

        [TestMethod]
        public void Plan_IsDeterministic_WithAVocabularyAsWithout()
        {
            var store = new DefaultLexicalAcquisitionStore();
            Learn(store, "chválit");
            var planner = new DefaultSpeechActPlanner(null, store);

            var request = Request(RelationalActKind.Validation, tickOffset: 17);
            var first = planner.Plan(request).PredicateLemma;

            for (var i = 0; i < 20; i++)
            {
                Assert.AreEqual(first, planner.Plan(request).PredicateLemma, "no RNG anywhere in the path");
            }
        }

        [TestMethod]
        public void Plan_ForgottenWord_LosesOutToOneStillInUse()
        {
            var store = new DefaultLexicalAcquisitionStore();
            Learn(store, "vyžadovat");
            Learn(store, "požádat");

            var planner = new DefaultSpeechActPlanner(null, store);
            var domineering = Request(RelationalActKind.Request, power: 1.0, agreeableness: 0.0);

            Assert.AreEqual("vyžadovat", planner.Plan(domineering).PredicateLemma, "both words are fresh");

            // A year on, "vyžadovat" has gone unused while "požádat" was kept in circulation.
            var yearLater = new WDateTime(Now.WorldTicks + (WTimeSpan.FromDays(1).Ticks * 400));
            for (var i = 0; i < 10; i++)
            {
                store.Reinforce(Speaker, "požádat", yearLater, successfulUse: true, learnedFrom: null);
            }

            var later = new SpeechActRequest(
                domineering.Intent, domineering.Speaker, domineering.Addressee, yearLater,
                domineering.Closeness, domineering.Familiarity, domineering.Agreeableness,
                domineering.Style, domineering.Power, domineering.Urgency);

            Assert.AreEqual(
                "požádat",
                planner.Plan(later).PredicateLemma,
                "a word left unused for a year is no longer on the tip of the tongue");
        }

        [TestMethod]
        public void Plan_SpeakerWhoHasForgottenEverything_StillSpeaks()
        {
            // The floor exists because SeedPredicateLexicon is a small closed set, not an open
            // dictionary: knowing none of the candidates must not leave a character mute.
            var store = new DefaultLexicalAcquisitionStore();
            Learn(store, "vyžadovat");
            var planner = new DefaultSpeechActPlanner(null, store);

            var yearLater = new WDateTime(Now.WorldTicks + (WTimeSpan.FromDays(1).Ticks * 400));
            var request = new SpeechActRequest(
                RelationalActKind.Request,
                EntityRef.ForHuman(Speaker, "S"), EntityRef.ForHuman(Addressee, "A"), yearLater,
                Closeness: 40, Familiarity: 40, Agreeableness: 0.0,
                Style: CommunicationStyle.Direct, Power: 1.0, Urgency: 0.0);

            Assert.IsFalse(
                string.IsNullOrEmpty(planner.Plan(request).PredicateLemma),
                "an empty vocabulary falls back to the plain choice rather than producing nothing");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Blocking regression 2 — equal weights must reproduce hash % count
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Plan_EqualAvailability_ReproducesThePlainHashDistribution()
        {
            // Every candidate for an intent drilled equally ⇒ equal weights. The weighted choice must
            // then land on exactly the same candidate the bare modulo would have, bucket for bucket —
            // otherwise wiring a vocabulary silently reshuffles everyone's ordinary word choice.
            var store = new DefaultLexicalAcquisitionStore();
            foreach (var candidate in SeedPredicateLexicon.Predicates[RelationalActKind.Validation])
            {
                Learn(store, candidate.LemmaImperfective);
            }

            var plain = new DefaultSpeechActPlanner();
            var weighted = new DefaultSpeechActPlanner(null, store);

            for (long t = 0; t < 200; t++)
            {
                var request = Request(RelationalActKind.Validation, tickOffset: t);
                Assert.AreEqual(
                    plain.Plan(request).PredicateLemma,
                    weighted.Plan(request).PredicateLemma,
                    $"equal availability must reduce to the pre-acquisition choice (t={t})");
            }
        }
    }
}
