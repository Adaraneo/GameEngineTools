// DefaultLexicalAcquisitionStore.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Language
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// In-memory half-life-regression store (Settles &amp; Meeder 2016).
    /// </summary>
    /// <remarks>
    /// <b>Not persisted.</b> The state lives outside <c>EnginesSnapshot</c>, so an exported and
    /// re-imported world comes back with every vocabulary empty. That is tolerable while the layer only
    /// observes — an empty store reads as "nobody knows anything yet", which is exactly the starting
    /// state — but it stops being tolerable once word choice depends on it, because characters would
    /// silently forget how they speak on load. Decide that before wiring production selection.
    /// </remarks>
    public sealed class DefaultLexicalAcquisitionStore : ILexicalAcquisitionStore
    {
        private readonly Dictionary<(HumanId Owner, string Lemma), LexicalAcquisition> _entries = new();
        private readonly LexicalAcquisitionConfig _config;

        /// <summary>Creates the store; <paramref name="config"/> defaults to the calibrated tunables.</summary>
        public DefaultLexicalAcquisitionStore(LexicalAcquisitionConfig? config = null)
            => _config = config ?? new LexicalAcquisitionConfig();

        /// <summary>The tunables in force.</summary>
        public LexicalAcquisitionConfig Config => _config;

        /// <summary>Number of distinct (character, lemma) pairs held — diagnostics and tests.</summary>
        public int Count => _entries.Count;

        /// <inheritdoc/>
        public LexicalAcquisition? TryGet(HumanId owner, string lemma)
            => _entries.TryGetValue((owner, lemma), out var entry) ? entry : null;

        /// <inheritdoc/>
        public double LexicalFamiliarity(HumanId owner, string lemma, WDateTime now)
            => TryGet(owner, lemma)?.LexicalFamiliarity(now) ?? 0.0;

        /// <inheritdoc/>
        public LexicalVocabulary SnapshotFor(HumanId owner)
        {
            var entries = new List<LexicalAcquisition>();
            foreach (var ((entryOwner, _), entry) in _entries)
            {
                if (entryOwner == owner)
                {
                    entries.Add(entry);
                }
            }

            // Stable order so a re-exported save is byte-comparable with the one before it.
            entries.Sort(static (a, b) => string.CompareOrdinal(a.Lemma, b.Lemma));
            return new LexicalVocabulary(entries);
        }

        /// <inheritdoc/>
        public void Restore(HumanId owner, LexicalVocabulary? vocabulary)
        {
            // Drop whatever is held for this character first, so restoring is a replacement rather than
            // a merge — importing a save must not leave traces of the world that was loaded before it.
            var stale = new List<(HumanId, string)>();
            foreach (var key in _entries.Keys)
            {
                if (key.Owner == owner)
                {
                    stale.Add(key);
                }
            }

            foreach (var key in stale)
            {
                _entries.Remove(key);
            }

            if (vocabulary is null)
            {
                return;
            }

            foreach (var entry in vocabulary.Entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Lemma))
                {
                    _entries[(owner, entry.Lemma)] = entry;
                }
            }
        }

        /// <inheritdoc/>
        public void Reinforce(
            HumanId owner,
            string lemma,
            WDateTime now,
            bool successfulUse,
            HumanId? learnedFrom,
            double gainMultiplier = 1.0)
        {
            if (string.IsNullOrWhiteSpace(lemma))
            {
                return;   // an act with no predicate teaches nothing
            }

            var key = (owner, lemma);
            var prior = _entries.TryGetValue(key, out var existing) ? existing : null;

            var timesSeen = (prior?.TimesSeen ?? 0) + 1;
            var timesCorrect = (prior?.TimesCorrect ?? 0) + (successfulUse ? 1 : 0);
            var timesIncorrect = (prior?.TimesIncorrect ?? 0) + (successfulUse ? 0 : 1);

            // Half-life regression: exposure and success lengthen retention, failure shortens it. The
            // square roots give diminishing returns, so the twentieth hearing adds far less than the second.
            var rawHalfLife = Math.Pow(
                2.0,
                _config.ThetaBias
                + (_config.ThetaSeen * Math.Sqrt(timesSeen) * gainMultiplier)
                + (_config.ThetaCorrect * Math.Sqrt(timesCorrect))
                + (_config.ThetaIncorrect * Math.Sqrt(timesIncorrect)));

            var halfLife = Math.Clamp(rawHalfLife, _config.MinHalfLifeDays, _config.MaxHalfLifeDays);

            _entries[key] = new LexicalAcquisition(
                Lemma: lemma,
                FirstEncountered: prior?.FirstEncountered ?? now,
                LastReinforced: now,
                HalfLifeDays: halfLife,
                TimesSeen: timesSeen,
                TimesCorrect: timesCorrect,
                TimesIncorrect: timesIncorrect,

                // Provenance is written once: the word belongs to whoever taught it, not to whoever
                // happened to use it most recently.
                LearnedFrom: prior?.LearnedFrom ?? learnedFrom);
        }
    }
}
