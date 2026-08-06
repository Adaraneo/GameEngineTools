// LexicalAcquisitionTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Language;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Phase 0 of lexical acquisition: the half-life-regression store on its own, consuming nothing and
    /// changing no behaviour.
    /// </summary>
    /// <remarks>
    /// Times are built from <see cref="WTimeSpan"/> offsets rather than raw tick counts, because the
    /// world calendar is 10 months × 36 days × 26 hours — a "day" here is not 24 hours, and a test that
    /// hard-codes ticks would silently mean something else.
    /// </remarks>
    [TestClass]
    public class LexicalAcquisitionTests : TestBase
    {
        private static readonly HumanId Speaker = new(Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"));
        private static readonly HumanId Listener = new(Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222"));
        private const string Lemma = "chválit";

        private static WDateTime Start => WDateTime.New(WDateOnly.New(100, 1, 1));

        private static WDateTime Later(double days)
            => new(Start.WorldTicks + (long)(WTimeSpan.FromDays(1).Ticks * days));

        // ──────────────────────────────────────────────────────────────────────
        // Decay and reinforcement
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void LexicalFamiliarity_UnknownLemma_IsZero()
        {
            var store = new DefaultLexicalAcquisitionStore();

            Assert.AreEqual(0.0, store.LexicalFamiliarity(Speaker, Lemma, Start));
            Assert.IsNull(store.TryGet(Speaker, Lemma), "an unseen word leaves no record at all");
        }

        [TestMethod]
        public void LexicalFamiliarity_AtMomentOfReinforcement_IsFull()
        {
            var store = new DefaultLexicalAcquisitionStore();
            store.Reinforce(Speaker, Lemma, Start, successfulUse: true, learnedFrom: null);

            Assert.AreEqual(1.0, store.LexicalFamiliarity(Speaker, Lemma, Start), 1e-9);
        }

        [TestMethod]
        public void LexicalFamiliarity_DecaysWithTimeSinceLastUse()
        {
            var store = new DefaultLexicalAcquisitionStore();
            store.Reinforce(Speaker, Lemma, Start, successfulUse: true, learnedFrom: null);

            var soon = store.LexicalFamiliarity(Speaker, Lemma, Later(0.5));
            var later = store.LexicalFamiliarity(Speaker, Lemma, Later(5));
            var muchLater = store.LexicalFamiliarity(Speaker, Lemma, Later(60));

            Assert.IsTrue(soon > later, $"recall must fade ({soon:F3} → {later:F3})");
            Assert.IsTrue(later > muchLater, $"and keep fading ({later:F3} → {muchLater:F3})");
            Assert.IsTrue(muchLater >= 0.0 && soon <= 1.0, "familiarity stays inside [0,1]");
        }

        [TestMethod]
        public void LexicalFamiliarity_AtOneHalfLife_IsOneHalf()
        {
            var store = new DefaultLexicalAcquisitionStore();
            store.Reinforce(Speaker, Lemma, Start, successfulUse: true, learnedFrom: null);

            var halfLife = store.TryGet(Speaker, Lemma)!.HalfLifeDays;

            // The defining property of the model — p = 2^(−Δ/h), so Δ = h gives exactly one half.
            Assert.AreEqual(0.5, store.LexicalFamiliarity(Speaker, Lemma, Later(halfLife)), 1e-6);
        }

        [TestMethod]
        public void Reinforce_Repetition_LengthensHalfLife()
        {
            var store = new DefaultLexicalAcquisitionStore();

            store.Reinforce(Speaker, Lemma, Start, successfulUse: true, learnedFrom: null);
            var afterOne = store.TryGet(Speaker, Lemma)!.HalfLifeDays;

            for (var i = 1; i <= 5; i++)
            {
                store.Reinforce(Speaker, Lemma, Later(i), successfulUse: true, learnedFrom: null);
            }

            var afterSix = store.TryGet(Speaker, Lemma)!.HalfLifeDays;
            Assert.IsTrue(afterSix > afterOne, $"practice must stick ({afterOne:F3} → {afterSix:F3} days)");
        }

        [TestMethod]
        public void Reinforce_FailedUse_ErodesRelativeToSuccessfulUse()
        {
            var success = new DefaultLexicalAcquisitionStore();
            var failure = new DefaultLexicalAcquisitionStore();

            for (var i = 0; i < 4; i++)
            {
                success.Reinforce(Speaker, Lemma, Later(i), successfulUse: true, learnedFrom: null);
                failure.Reinforce(Speaker, Lemma, Later(i), successfulUse: false, learnedFrom: null);
            }

            Assert.IsTrue(
                success.TryGet(Speaker, Lemma)!.HalfLifeDays > failure.TryGet(Speaker, Lemma)!.HalfLifeDays,
                "a word that keeps failing must not be retained as well as one that keeps landing");
        }

        [TestMethod]
        public void Reinforce_CountersTrackSeenCorrectAndIncorrect()
        {
            var store = new DefaultLexicalAcquisitionStore();
            store.Reinforce(Speaker, Lemma, Start, successfulUse: true, learnedFrom: null);
            store.Reinforce(Speaker, Lemma, Later(1), successfulUse: false, learnedFrom: null);
            store.Reinforce(Speaker, Lemma, Later(2), successfulUse: true, learnedFrom: null);

            var entry = store.TryGet(Speaker, Lemma)!;
            Assert.AreEqual(3, entry.TimesSeen);
            Assert.AreEqual(2, entry.TimesCorrect);
            Assert.AreEqual(1, entry.TimesIncorrect);
            Assert.AreEqual(Start.WorldTicks, entry.FirstEncountered.WorldTicks, "first encounter is not overwritten");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Bounds
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Reinforce_HalfLife_StaysWithinConfiguredBounds()
        {
            var config = new LexicalAcquisitionConfig(MinHalfLifeDays: 0.01, MaxHalfLifeDays: 274.0);
            var store = new DefaultLexicalAcquisitionStore(config);

            // Hammering the word cannot push retention past the ceiling…
            for (var i = 0; i < 400; i++)
            {
                store.Reinforce(Speaker, Lemma, Later(i), successfulUse: true, learnedFrom: null);
            }

            Assert.IsTrue(store.TryGet(Speaker, Lemma)!.HalfLifeDays <= config.MaxHalfLifeDays);

            // …and failing at it cannot push retention below the floor.
            var failing = new DefaultLexicalAcquisitionStore(config);
            for (var i = 0; i < 400; i++)
            {
                failing.Reinforce(Listener, Lemma, Later(i), successfulUse: false, learnedFrom: null);
            }

            Assert.IsTrue(failing.TryGet(Listener, Lemma)!.HalfLifeDays >= config.MinHalfLifeDays);
        }

        [TestMethod]
        public void Reinforce_BlankLemma_IsIgnored()
        {
            var store = new DefaultLexicalAcquisitionStore();
            store.Reinforce(Speaker, "", Start, successfulUse: true, learnedFrom: null);
            store.Reinforce(Speaker, "   ", Start, successfulUse: true, learnedFrom: null);

            Assert.AreEqual(0, store.Count, "an act carrying no predicate teaches nothing");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Provenance
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Reinforce_LearnedFrom_IsWrittenOnceAndNeverOverwritten()
        {
            var third = new HumanId(Guid.Parse("cccccccc-3333-3333-3333-333333333333"));
            var store = new DefaultLexicalAcquisitionStore();

            store.Reinforce(Listener, Lemma, Start, successfulUse: true, learnedFrom: Speaker);
            store.Reinforce(Listener, Lemma, Later(1), successfulUse: true, learnedFrom: third);

            Assert.AreEqual(
                Speaker,
                store.TryGet(Listener, Lemma)!.LearnedFrom,
                "the word belongs to whoever taught it, not to whoever used it most recently");
        }

        [TestMethod]
        public void Reinforce_VocabulariesAreIndependentPerCharacter()
        {
            var store = new DefaultLexicalAcquisitionStore();
            store.Reinforce(Speaker, Lemma, Start, successfulUse: true, learnedFrom: null);

            Assert.IsTrue(store.LexicalFamiliarity(Speaker, Lemma, Start) > 0.0);
            Assert.AreEqual(
                0.0,
                store.LexicalFamiliarity(Listener, Lemma, Start),
                "one character learning a word must not teach it to everyone else");
        }

        [TestMethod]
        public void Reinforce_GainMultiplier_AcceleratesRetention()
        {
            var plain = new DefaultLexicalAcquisitionStore();
            var amplified = new DefaultLexicalAcquisitionStore();

            plain.Reinforce(Listener, Lemma, Start, successfulUse: true, learnedFrom: Speaker, gainMultiplier: 1.0);
            amplified.Reinforce(Listener, Lemma, Start, successfulUse: true, learnedFrom: Speaker, gainMultiplier: 2.0);

            Assert.IsTrue(
                amplified.TryGet(Listener, Lemma)!.HalfLifeDays > plain.TryGet(Listener, Lemma)!.HalfLifeDays,
                "a socially amplified exposure must stick harder than a neutral one");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Cadence independence — the reason familiarity is computed lazily
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void LexicalFamiliarity_DependsOnElapsedTime_NotOnHowOftenItWasQueried()
        {
            var polled = new DefaultLexicalAcquisitionStore();
            var quiet = new DefaultLexicalAcquisitionStore();

            polled.Reinforce(Speaker, Lemma, Start, successfulUse: true, learnedFrom: null);
            quiet.Reinforce(Speaker, Lemma, Start, successfulUse: true, learnedFrom: null);

            // Simulate a Player-tier character being read every tick and a Background one being ignored.
            for (var i = 1; i <= 50; i++)
            {
                polled.LexicalFamiliarity(Speaker, Lemma, Later(i * 0.1));
            }

            Assert.AreEqual(
                quiet.LexicalFamiliarity(Speaker, Lemma, Later(5)),
                polled.LexicalFamiliarity(Speaker, Lemma, Later(5)),
                1e-12,
                "LOD cadence must not change how fast a word is forgotten");
        }
    }
}
