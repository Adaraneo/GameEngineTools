// ILexicalAcquisitionStore.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Language
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Per-character, per-lemma vocabulary. A missing entry means the character does not know the word
    /// (familiarity 0) — absence is the default, so an empty store leaves every consumer at its
    /// pre-acquisition behaviour.
    /// </summary>
    /// <remarks>
    /// Shared across characters rather than owned by one, and carries no per-tick step, so it is not an
    /// <c>IEngine&lt;TState, TConfig&gt;</c> and does not belong among the per-character engine
    /// registrations.
    /// </remarks>
    public interface ILexicalAcquisitionStore
    {
        /// <summary>
        /// The tunables in force. Exposed because consumers gate on the thresholds (θ_R, θ_P,
        /// comprehension) and must read the same calibration the store learned under.
        /// </summary>
        LexicalAcquisitionConfig Config { get; }

        /// <summary>The stored acquisition state, or <c>null</c> when the word was never encountered.</summary>
        LexicalAcquisition? TryGet(HumanId owner, string lemma);

        /// <summary>
        /// Records one exposure to <paramref name="lemma"/> and lengthens (or erodes) its half-life.
        /// </summary>
        /// <param name="owner">Whose vocabulary is being updated.</param>
        /// <param name="lemma">The word encountered.</param>
        /// <param name="now">When it happened.</param>
        /// <param name="successfulUse">
        /// True when the exposure landed — the listener understood it, or the speaker used it and was
        /// not rebuffed. False exposures still count as seen, but pull the half-life down.
        /// </param>
        /// <param name="learnedFrom">
        /// Who the word came from, for a listener. Recorded only on first encounter, so provenance
        /// remains the person it was actually learned from rather than whoever used it most recently.
        /// </param>
        /// <param name="gainMultiplier">
        /// Social amplification of the exposure (see Phase 3); 1.0 is a neutral encounter.
        /// </param>
        void Reinforce(
            HumanId owner,
            string lemma,
            WDateTime now,
            bool successfulUse,
            HumanId? learnedFrom,
            double gainMultiplier = 1.0);

        /// <summary>Recall probability in [0, 1]; 0 for a word this character has never met.</summary>
        double LexicalFamiliarity(HumanId owner, string lemma, WDateTime now);

        /// <summary>
        /// Everything <paramref name="owner"/> knows, for saving. Empty when they know nothing.
        /// </summary>
        /// <remarks>
        /// The store is shared across characters and is not an <c>IEngine</c>, so it has no
        /// <c>State</c>/<c>RestoreState</c> of its own — persistence goes through this pair instead.
        /// </remarks>
        LexicalVocabulary SnapshotFor(HumanId owner);

        /// <summary>
        /// Replaces <paramref name="owner"/>'s vocabulary with a saved one, discarding anything held for
        /// them already. A null or empty vocabulary leaves them knowing nothing, which is exactly how a
        /// save written before this field existed reads.
        /// </summary>
        void Restore(HumanId owner, LexicalVocabulary? vocabulary);
    }
}
