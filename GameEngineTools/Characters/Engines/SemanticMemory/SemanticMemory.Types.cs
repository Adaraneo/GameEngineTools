// SemanticMemory.Types.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    public enum PersonBeliefKind
    { Rejecting, EmotionallySafe, Reliable, Warm, Critical }

    public sealed record PersonBeliefEvidence(
        HumanId Other,
        PersonBeliefKind Kind,
        double Weight,
        string Source);

    public sealed record PersonBelief(
        HumanId Other,
        PersonBeliefKind Kind,
        double Strength,
        double Stability,
        int EvidenceCount,
        WDateTime LastUpdatedAt,
        string? LastEvidenceSource = null);

    public sealed record PersonBeliefSet(
        HumanId Other,
        IReadOnlyDictionary<PersonBeliefKind, PersonBelief> Beliefs)
    {
        public double StrengthOf(PersonBeliefKind kind)
            => Beliefs.TryGetValue(kind, out var belief) ? belief.Strength : 0.0;
    }
}
