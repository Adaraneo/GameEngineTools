// SemanticMemory.Types.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Kind of semantic belief about a person.
    /// Each kind corresponds to one dimension of the subjective model of how another person behaves.
    /// </summary>
    public enum PersonBeliefKind
    {
        /// <summary>The person repeatedly refuses contact or interaction. Blocks social approach.</summary>
        Rejecting,

        /// <summary>The person accepts vulnerability without judgment. A precondition for SelfDisclosure/Meta/Invite.</summary>
        EmotionallySafe,

        /// <summary>The person keeps promises, helps and repairs harm. The basis of trust.</summary>
        Reliable,

        /// <summary>The person responds with warmth and positivity. Raises the baseline for all contact.</summary>
        Warm,

        /// <summary>The person criticizes, neglects or ignores. Suppresses vulnerability.</summary>
        Critical
    }

    /// <summary>
    /// Direct evidence for a specific belief kind — supplied by the interaction or relationship engine.
    /// Complements pattern inference from episodic memory.
    /// </summary>
    public sealed record PersonBeliefEvidence(
        HumanId Other,
        PersonBeliefKind Kind,
        double Weight,
        string Source);

    /// <summary>
    /// A single belief about a specific person along one dimension (<see cref="Kind"/>).
    /// Strength grows with evidence and falls through natural decay and contradiction pressure.
    /// Stability slows decay and increases resistance to new signals.
    /// </summary>
    public sealed record PersonBelief(
        HumanId Other,
        PersonBeliefKind Kind,
        /// <summary>Current belief strength [0.0–1.0].</summary>
        double Strength,
        /// <summary>Stability — how hard the belief is to change [0.0–0.95].</summary>
        double Stability,
        /// <summary>Total number of pieces of evidence that supported this belief.</summary>
        int EvidenceCount,
        /// <summary>Time of the last update — a proxy for last contact with the person.</summary>
        WDateTime LastUpdatedAt,
        /// <summary>Source of the last update (for diagnostics).</summary>
        string? LastEvidenceSource = null);

    /// <summary>
    /// A collection of all beliefs about one specific person.
    /// </summary>
    public sealed record PersonBeliefSet(
        HumanId Other,
        IReadOnlyDictionary<PersonBeliefKind, PersonBelief> Beliefs)
    {
        /// <summary>
        /// Returns the Strength for the given <paramref name="kind"/>, or 0.0 if the belief does not exist.
        /// </summary>
        public double StrengthOf(PersonBeliefKind kind)
            => Beliefs.TryGetValue(kind, out var belief) ? belief.Strength : 0.0;
    }
}
