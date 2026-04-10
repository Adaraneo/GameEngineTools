// SemanticMemory.State.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Characters.Engines.Memory;

    public sealed record SemanticMemoryState(
        IReadOnlyDictionary<HumanId, PersonBeliefSet> People)
    {
        public static SemanticMemoryState Empty { get; } =
            new(new Dictionary<HumanId, PersonBeliefSet>());

        public PersonBeliefSet? GetBeliefs(HumanId other)
            => People.TryGetValue(other, out var beliefs) ? beliefs : null;

        public double GetStrength(HumanId other, PersonBeliefKind kind)
            => GetBeliefs(other)?.StrengthOf(kind) ?? 0.0;

        public double ExpectedAcceptance(HumanId other, SpeechAct act)
            => SemanticMemoryMath.ExpectedAcceptance(this, other, act);

        public double ExpectedAcceptance(
            HumanId other,
            SpeechAct act,
            RelationshipEdge? relationship,
            PsychologicalProfile? profile,
            IReadOnlyList<EpisodicMemory>? episodes = null)
            => SemanticMemoryMath.ExpectedAcceptance(this, other, act, relationship, profile, episodes);
    }
}
