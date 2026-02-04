// IMemory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Memory
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    public sealed record MemoryConfig(double BaseEncoding, double SleepConsolidationBoost, double ForgettingRate);

    public sealed record MemoryIndex(
        IReadOnlyList<EpisodicMemory> Episodes,
        IReadOnlyDictionary<string, SemanticFact> Semantics);

    public sealed record EpisodicMemory(
        Guid Id, WDateTime When, string What, double Salience, EmotionalTag Emotion, double Strength);

    public sealed record SemanticFact(string Key, string Value, double Confidence);

    public enum EmotionalTag { Neutral, Positive, Negative, Mixed }

    public interface IMemoryEngine : IEngine<MemoryIndex, MemoryConfig>
    {
        void Encode(EpisodicMemory episode, IHumanContext ctx, IEventCollector outbox);
        IReadOnlyList<EpisodicMemory> Recall(Func<EpisodicMemory, bool> predicate);
    }

    // Události
    public sealed record MemoryEncoded(WDateTime OccurredAt, HumanId Human, Guid EpisodeId, double Strength) : IDomainEvent;
    public sealed record MemoryRecalled(WDateTime OccurredAt, HumanId Human, Guid EpisodeId) : IDomainEvent;
    public sealed record MemoryConsolidated(WDateTime OccurredAt, HumanId Human, int Count) : IDomainEvent;
}
