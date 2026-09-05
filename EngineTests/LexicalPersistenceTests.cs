// LexicalPersistenceTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Language;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Vocabulary survives save and load.
    /// </summary>
    /// <remarks>
    /// Without this the whole layer quietly resets on import: characters would come back having
    /// forgotten how they speak, which shows up as unexplained behaviour rather than as an error.
    /// </remarks>
    [TestClass]
    public class LexicalPersistenceTests : TestBase
    {
        private static readonly HumanId Alice = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-000000000001"));
        private static readonly HumanId Bob = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-000000000002"));
        private static readonly HumanId Ghost = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-00000000dead"));

        private static WDateTime Start => WDateTime.New(WDateOnly.New(100, 1, 1));

        /// <summary>The same converter set <c>GeneratedFile</c> serialises character saves with.</summary>
        private static JsonSerializerOptions SaveOptions() => new()
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            Converters =
            {
                new HumanIdJsonConverter(),
                new WDateTimeJsonConverter(),
                new WTimeSpanJsonConverter(),
            },
        };

        // ──────────────────────────────────────────────────────────────────────
        // Projection and restore
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void SnapshotFor_ReturnsOnlyThatCharactersWords()
        {
            var store = new DefaultLexicalAcquisitionStore();
            store.Reinforce(Alice, "chválit", Start, successfulUse: true, learnedFrom: null);
            store.Reinforce(Alice, "mluvit", Start, successfulUse: true, learnedFrom: null);
            store.Reinforce(Bob, "žebrat o", Start, successfulUse: true, learnedFrom: Alice);

            var alice = store.SnapshotFor(Alice);

            CollectionAssert.AreEqual(
                new[] { "chválit", "mluvit" },
                alice.Entries.Select(e => e.Lemma).ToArray(),
                "one character's vocabulary, in a stable order");
        }

        [TestMethod]
        public void SnapshotFor_UnknownCharacter_IsEmptyNotNull()
            => Assert.AreEqual(0, new DefaultLexicalAcquisitionStore().SnapshotFor(Alice).Entries.Count);

        [TestMethod]
        public void Restore_ReplacesRatherThanMerges()
        {
            var store = new DefaultLexicalAcquisitionStore();
            store.Reinforce(Alice, "from-previous-world", Start, successfulUse: true, learnedFrom: null);

            store.Restore(Alice, new LexicalVocabulary(new[]
            {
                new LexicalAcquisition("chválit", Start, Start, HalfLifeDays: 4.0, TimesSeen: 3),
            }));

            var restored = store.SnapshotFor(Alice);
            Assert.AreEqual(1, restored.Entries.Count, "loading a save must not leave traces of the world before it");
            Assert.AreEqual("chválit", restored.Entries[0].Lemma);
        }

        [TestMethod]
        public void Restore_Null_LeavesTheCharacterKnowingNothing()
        {
            // How a save written before the field existed reads.
            var store = new DefaultLexicalAcquisitionStore();
            store.Reinforce(Alice, "chválit", Start, successfulUse: true, learnedFrom: null);

            store.Restore(Alice, null);

            Assert.AreEqual(0, store.SnapshotFor(Alice).Entries.Count);
            Assert.AreEqual(0.0, store.LexicalFamiliarity(Alice, "chválit", Start));
        }

        [TestMethod]
        public void Restore_DoesNotDisturbOtherCharacters()
        {
            var store = new DefaultLexicalAcquisitionStore();
            store.Reinforce(Bob, "mluvit", Start, successfulUse: true, learnedFrom: null);

            store.Restore(Alice, LexicalVocabulary.Empty);

            Assert.IsTrue(store.LexicalFamiliarity(Bob, "mluvit", Start) > 0.0);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Through the save format
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Vocabulary_SurvivesJsonRoundTrip_WithDecayIntact()
        {
            var store = new DefaultLexicalAcquisitionStore();
            for (var i = 0; i < 6; i++)
            {
                store.Reinforce(Bob, "chválit", Start, successfulUse: true, learnedFrom: Alice);
            }

            var beforeSave = store.SnapshotFor(Bob);
            var json = JsonSerializer.Serialize(beforeSave, SaveOptions());
            var afterLoad = JsonSerializer.Deserialize<LexicalVocabulary>(json, SaveOptions())!;

            var restored = new DefaultLexicalAcquisitionStore();
            restored.Restore(Bob, afterLoad);

            var entry = restored.SnapshotFor(Bob).Entries.Single();
            var original = beforeSave.Entries.Single();

            Assert.AreEqual(original.Lemma, entry.Lemma);
            Assert.AreEqual(original.HalfLifeDays, entry.HalfLifeDays, 1e-9, "retention must not shift across a save");
            Assert.AreEqual(original.TimesSeen, entry.TimesSeen);
            Assert.AreEqual(original.TimesCorrect, entry.TimesCorrect);
            Assert.AreEqual(original.LastReinforced.WorldTicks, entry.LastReinforced.WorldTicks, "WDateTime survives");
            Assert.AreEqual(Alice, entry.LearnedFrom, "nullable HumanId survives");

            // The point of persisting at all: decay carries on from where it was, rather than restarting.
            var later = new WDateTime(Start.WorldTicks + (WTimeSpan.FromDays(1).Ticks * 2));
            Assert.AreEqual(
                store.LexicalFamiliarity(Bob, "chválit", later),
                restored.LexicalFamiliarity(Bob, "chválit", later),
                1e-9);
        }

        [TestMethod]
        public void Vocabulary_WithNoProvenance_RoundTripsAsNull()
        {
            // A word the character produced rather than learned carries LearnedFrom = null.
            var store = new DefaultLexicalAcquisitionStore();
            store.Reinforce(Alice, "mluvit", Start, successfulUse: true, learnedFrom: null);

            var json = JsonSerializer.Serialize(store.SnapshotFor(Alice), SaveOptions());
            var loaded = JsonSerializer.Deserialize<LexicalVocabulary>(json, SaveOptions())!;

            Assert.IsNull(loaded.Entries.Single().LearnedFrom);
        }

        [TestMethod]
        public void Vocabulary_ProvenanceToAMissingCharacter_IsTolerated()
        {
            // Provenance is a record of where a word came from, not a live reference: exporting part of a
            // world, or the teacher dying, must not make the vocabulary unloadable.
            var vocabulary = new LexicalVocabulary(new[]
            {
                new LexicalAcquisition("chválit", Start, Start, HalfLifeDays: 4.0, TimesSeen: 2, LearnedFrom: Ghost),
            });

            var json = JsonSerializer.Serialize(vocabulary, SaveOptions());
            var loaded = JsonSerializer.Deserialize<LexicalVocabulary>(json, SaveOptions())!;

            var store = new DefaultLexicalAcquisitionStore();
            store.Restore(Alice, loaded);

            Assert.IsTrue(store.LexicalFamiliarity(Alice, "chválit", Start) > 0.0, "the word still works");
            Assert.AreEqual(Ghost, store.SnapshotFor(Alice).Entries.Single().LearnedFrom);
        }

        [TestMethod]
        public void Vocabulary_IsStoredAsLemmas_NotLexiconIndices()
        {
            // Guards the deliberate choice: an index would be smaller but only meaningful against a table
            // that stays stable forever — reorder the seed lexicon and every save silently shifts.
            var store = new DefaultLexicalAcquisitionStore();
            store.Reinforce(Alice, "vyžadovat", Start, successfulUse: true, learnedFrom: null);

            var json = JsonSerializer.Serialize(store.SnapshotFor(Alice), SaveOptions());
            var loaded = JsonSerializer.Deserialize<LexicalVocabulary>(json, SaveOptions())!;

            // Asserted on the decoded value rather than the raw text: System.Text.Json escapes non-ASCII
            // by default, so the file holds "vyžadovat" — still the word, just spelled for a
            // transport that assumes ASCII.
            Assert.AreEqual(
                "vyžadovat",
                loaded.Entries.Single().Lemma,
                "the saved form carries the word itself, recoverable without consulting any lexicon");
            StringAssert.Contains(json, "Lemma", "and stores it under a named field, not as a bare index");
        }

        [TestMethod]
        public void SnapshotFor_IsStableAcrossRepeatedExports()
        {
            var store = new DefaultLexicalAcquisitionStore();
            foreach (var lemma in new[] { "mluvit", "chválit", "žebrat o", "požádat" })
            {
                store.Reinforce(Alice, lemma, Start, successfulUse: true, learnedFrom: null);
            }

            var first = JsonSerializer.Serialize(store.SnapshotFor(Alice), SaveOptions());
            var second = JsonSerializer.Serialize(store.SnapshotFor(Alice), SaveOptions());

            Assert.AreEqual(first, second, "re-exporting an unchanged world must produce an unchanged file");
        }
    }
}
