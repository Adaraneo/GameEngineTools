// IMemory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Memory
{
    using Characters.Core;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.World.Utils.Time;

    public sealed record MemoryConfig(
        double BaseEncoding = 0.5,
        double SleepConsolidationBoost = 0.12,
        double ForgettingRate = 0.06,
        double PruneThreshold = 0.01,
        double ReinforcementBoost = 0.15,
        double EmotionDecayMod = 0.5,
        double StressDistortionWeight = 0.35)
    {
        public MemoryConfig() : this(0.5, 0.12, 0.06, 0.01, 0.15, 0.5, 0.35) { }
    }

    public sealed record MemoryIndex(
        IReadOnlyList<EpisodicMemory> Episodes);

    public sealed record EpisodicMemory(
        Guid Id, WDateTime When, string What, double Salience, EmotionalTag Emotion, double Strength,
        string? PerceivedWhat = null, double RecallConfidence = 1.0, double Distortion = 0.0,
        HumanId? OtherPerson = null, PersonBeliefEvidence? BeliefEvidence = null);

    public enum EmotionalTag
    { Neutral, Positive, Negative, Mixed }

    public interface IMemoryEngine : IEngine<MemoryIndex, MemoryConfig>
    {
        void Encode(EpisodicMemory episode, IHumanContext ctx, IEventCollector outbox);

        IReadOnlyList<EpisodicMemory> Recall(Func<EpisodicMemory, bool> predicate);

        MemoryRecallResult Recall(MemoryRecallQuery query, WDateTime now);

        DecisionWorkingSet BuildWorkingSet(MemoryRecallQuery query, WDateTime now);
    }

    // Události
    public sealed record MemoryEncoded(
        WDateTime OccurredAt,
        HumanId Human,
        Guid EpisodeId,
        double Strength,
        string? What = null,
        string? PerceivedWhat = null,
        HumanId? OtherPerson = null,
        PersonBeliefEvidence? BeliefEvidence = null) : IDomainEvent;
    public sealed record MemoryRecalled(WDateTime OccurredAt, HumanId Human, Guid EpisodeId) : IDomainEvent;
    public sealed record MemoryRecallEvaluated(
        WDateTime OccurredAt,
        HumanId Human,
        string? ActionName,
        HumanId? TargetHuman,
        int RecalledCount) : IDomainEvent;
    public sealed record ReflectionApplied(
        WDateTime OccurredAt,
        HumanId Human,
        string? ActionName,
        HumanId? TargetHuman,
        ReflectionSummaryKind Kind,
        double Strength) : IDomainEvent;
    public sealed record MemoryConsolidated(WDateTime OccurredAt, HumanId Human, int Count) : IDomainEvent;
}
