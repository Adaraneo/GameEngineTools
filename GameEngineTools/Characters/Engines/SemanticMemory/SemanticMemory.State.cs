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
        IReadOnlyDictionary<HumanId, PersonBeliefSet> People,

        /// <summary>
        /// Capped list of past significant-other imprints, retained independently of
        /// <see cref="People"/> so they survive <see cref="DefaultSemanticMemoryEngine.ForgetPerson"/>.
        /// Capped at <see cref="SemanticMemoryConfig.MaxSignificantOtherImprints"/>-equivalent
        /// (<see cref="Relationships.RelationshipsConfig.MaxSignificantOtherImprints"/>, default 3) —
        /// keeps transference resemblance-checking O(imprints), not O(all known people), for
        /// scalability with large NPC populations (Topic C decision gate c).
        /// </summary>
        IReadOnlyList<SignificantOtherImprint>? SignificantOthers = null)
    {
        /// <summary>Empty state for new characters with no beliefs.</summary>
        public static SemanticMemoryState Empty { get; } =
            new(new Dictionary<HumanId, PersonBeliefSet>(), Array.Empty<SignificantOtherImprint>());

        /// <summary>Non-null accessor — <see cref="SignificantOthers"/> may be null on states restored before this field existed.</summary>
        public IReadOnlyList<SignificantOtherImprint> SignificantOthersOrEmpty
            => SignificantOthers ?? Array.Empty<SignificantOtherImprint>();

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
