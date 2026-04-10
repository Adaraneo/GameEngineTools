// SemanticMemory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
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

    public sealed record SemanticMemoryConfig(
        double LearningRate = 0.18,
        double ContradictionRate = 0.08,
        double DecayPerDay = 0.01,
        double StabilityGainPerEvidence = 0.08)
    {
        public SemanticMemoryConfig() : this(0.18, 0.08, 0.01, 0.08) { }
    }

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

    public interface ISemanticMemoryEngine : IEngine<SemanticMemoryState, SemanticMemoryConfig>
    { }

    public sealed record SemanticBeliefUpdated(
        WDateTime OccurredAt,
        HumanId Human,
        HumanId Other,
        PersonBeliefKind Kind,
        double Strength,
        int EvidenceCount) : IDomainEvent;

    public static class SemanticMemoryMath
    {
        public static double ExpectedAcceptance(
            SemanticMemoryState? state,
            HumanId other,
            SpeechAct act)
        {
            if (state is null)
            {
                return 0.5;
            }

            var warm = state.GetStrength(other, PersonBeliefKind.Warm);
            var safe = state.GetStrength(other, PersonBeliefKind.EmotionallySafe);
            var reliable = state.GetStrength(other, PersonBeliefKind.Reliable);
            var rejecting = state.GetStrength(other, PersonBeliefKind.Rejecting);
            var critical = state.GetStrength(other, PersonBeliefKind.Critical);

            var vulnerabilityWeight = act switch
            {
                SpeechAct.SelfDisclosure => 1.25,
                SpeechAct.Meta => 1.10,
                SpeechAct.Invite => 1.20,
                SpeechAct.Validation => 1.0,
                _ => 0.8
            };

            var positive = warm * 0.28 + safe * 0.32 * vulnerabilityWeight + reliable * 0.22;
            var negative = rejecting * 0.34 * vulnerabilityWeight + critical * 0.24;
            return Math.Clamp(0.5 + positive - negative, 0.05, 0.95);
        }
    }
}
