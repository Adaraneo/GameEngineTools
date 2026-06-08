// MemoryRecall.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Memory
{
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Query for decision-time episodic recall.
    /// </summary>
    public sealed record MemoryRecallQuery(
        HumanId? TargetHuman = null,
        string? ActionName = null,
        SpeechAct? InteractionAct = null,
        EmotionalTag? EmotionalValence = null,
        WTimeSpan? RecencyWindow = null,
        int Take = 4,
        double CurrentValence = 0.0,
        double NeuroticismScore = 0.5,
        double? CognitiveBurden = null,
        double DaysInNegativeMood = 0.0);

    /// <summary>
    /// Ranked episodic recall item with compact scoring diagnostics.
    /// </summary>
    public sealed record MemoryRecallItem(
        EpisodicMemory Episode,
        double Relevance,
        bool TargetMatched,
        bool SituationMatched,
        bool EmotionalMatched,
        double RecencyWeight);

    /// <summary>
    /// Ranked recall result for one decision-time query.
    /// </summary>
    public sealed record MemoryRecallResult(
        MemoryRecallQuery Query,
        IReadOnlyList<MemoryRecallItem> Items);

    /// <summary>
    /// Compact reflection kinds derived from repeated episodic patterns.
    /// </summary>
    public enum ReflectionSummaryKind
    {
        /// <summary>The target is generally safe to reach out to.</summary>
        SafeForReachOut = 0,
        /// <summary>The target tends to reject intimacy.</summary>
        RejectsIntimacy,
        /// <summary>Recent interactions with the target carried a social cost.</summary>
        RecentSocialCost,
        /// <summary>The target is warm toward casual contact.</summary>
        WarmForCasualContact
    }

    /// <summary>
    /// Lightweight reflection over repeated episodes relevant for the current decision.
    /// </summary>
    public sealed record ReflectionSummary(
        ReflectionSummaryKind Kind,
        HumanId? TargetHuman,
        double Strength,
        int EvidenceCount,
        string Explanation);

    /// <summary>
    /// Small transient decision-time memory context.
    /// </summary>
    public sealed record DecisionWorkingSet(
        HumanId? TargetHuman,
        string? ActionName,
        SpeechAct? InteractionAct,
        IReadOnlyList<MemoryRecallItem> RecalledEpisodes,
        IReadOnlyList<ReflectionSummary> Reflections,
        bool IsSystem1 = false);
}
