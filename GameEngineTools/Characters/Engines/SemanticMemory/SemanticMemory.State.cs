// SemanticMemory.State.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Immutable snapshot of semantic memory — the character's beliefs about everyone it knows.
    /// </summary>
    public sealed record SemanticMemoryState(
        IReadOnlyDictionary<HumanId, PersonBeliefSet> People)
    {
        /// <summary>Empty state for new characters with no beliefs.</summary>
        public static SemanticMemoryState Empty { get; } =
            new(new Dictionary<HumanId, PersonBeliefSet>());

        /// <summary>
        /// Returns the BeliefSet for the given person, or <see langword="null"/> if the character does not know them.
        /// </summary>
        public PersonBeliefSet? GetBeliefs(HumanId other)
            => People.TryGetValue(other, out var beliefs) ? beliefs : null;

        /// <summary>
        /// Returns the Strength of the given belief kind for the person, or 0.0 if the belief does not exist.
        /// </summary>
        public double GetStrength(HumanId other, PersonBeliefKind kind)
            => GetBeliefs(other)?.StrengthOf(kind) ?? 0.0;

        /// <summary>
        /// Predicts the probability that a social approach is accepted by the given person.
        /// A shortened overload without relationship context or psychological profile.
        /// </summary>
        public double ExpectedAcceptance(HumanId other, SpeechAct act)
            => SemanticMemoryMath.ExpectedAcceptance(this, other, act);

        /// <summary>
        /// Predicts the probability that a social approach is accepted, with full context.
        /// Includes relationship metrics, the psychological profile and the trend of recent episodes.
        /// </summary>
        public double ExpectedAcceptance(
            HumanId other,
            SpeechAct act,
            RelationshipEdge? relationship,
            PsychologicalProfile? profile,
            IReadOnlyList<EpisodicMemory>? episodes = null)
            => SemanticMemoryMath.ExpectedAcceptance(this, other, act, relationship, profile, episodes);
    }
}
