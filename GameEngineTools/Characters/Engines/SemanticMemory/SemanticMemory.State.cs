// SemanticMemory.State.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;

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
    }
}
