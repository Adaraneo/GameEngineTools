// MemoryCognition.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Memory
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.World.Utils.Time;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Shared decision-time cognition helpers over episodic memory snapshots.
    /// </summary>
    internal static class MemoryCognition
    {
        #region Public API

        public static MemoryRecallResult Recall(MemoryIndex memory, MemoryRecallQuery query, WDateTime now)
        {
            var items = memory.Episodes
                .Select(Reconstruct)
                .Select(episode => ScoreRecallItem(episode, query, now))
                .Where(item => item is not null)
                .Select(item => item!)
                .OrderByDescending(item => item.Relevance)
                .ThenByDescending(item => item.Episode.When.WorldTicks)
                .ThenBy(item => item.Episode.Id)
                .Take(Math.Max(1, query.Take))
                .ToList();

            return new MemoryRecallResult(query, items);
        }

        public static DecisionWorkingSet BuildWorkingSet(MemoryIndex memory, MemoryRecallQuery query, WDateTime now)
        {
            var recall = Recall(memory, query, now);
            var reflections = BuildReflections(query, recall.Items, now);
            return new DecisionWorkingSet(
                query.TargetHuman,
                query.ActionName,
                query.InteractionAct,
                recall.Items,
                reflections);
        }

        #endregion Public API

        #region Scoring

        private static MemoryRecallItem? ScoreRecallItem(EpisodicMemory episode, MemoryRecallQuery query, WDateTime now)
        {
            var age = WTimeSpan.Abs(now - episode.When);
            if (query.RecencyWindow is { } recencyWindow && age > recencyWindow)
            {
                return null;
            }

            var targetMatched = query.TargetHuman is null || episode.OtherPerson == query.TargetHuman;
            if (query.TargetHuman is not null && !targetMatched && episode.OtherPerson is not null)
            {
                return null;
            }

            var situationScore = ComputeSituationScore(episode, query);
            if (query.ActionName is not null && situationScore <= 0.0 && targetMatched && query.TargetHuman is null)
            {
                return null;
            }

            var emotionalMatched = query.EmotionalValence is null || episode.Emotion == query.EmotionalValence;
            var emotionScore = query.EmotionalValence is null
                ? EmotionalIntensity(episode.Emotion)
                : emotionalMatched ? 1.0 : 0.15;
            var recencyWeight = ComputeRecencyWeight(age, query.RecencyWindow);
            var targetScore = targetMatched && query.TargetHuman is not null ? 1.0 : episode.OtherPerson is null ? 0.25 : 0.0;
            var relevance =
                (targetScore * 0.34) +
                (situationScore * 0.24) +
                (recencyWeight * 0.22) +
                (Math.Clamp(episode.Strength, 0.0, 1.0) * 0.12) +
                (Math.Clamp(episode.Salience, 0.0, 1.0) * 0.08) +
                (emotionScore * 0.10);

            if (relevance <= 0.12)
            {
                return null;
            }

            return new MemoryRecallItem(
                episode,
                Math.Round(relevance, 6),
                targetMatched && query.TargetHuman is not null,
                situationScore >= 0.50,
                emotionalMatched,
                Math.Round(recencyWeight, 6));
        }

        private static double ComputeSituationScore(EpisodicMemory episode, MemoryRecallQuery query)
        {
            var perceived = episode.PerceivedWhat ?? episode.What;
            var header = MemoryWhatParser.GetHeader(perceived);
            var actionName = query.ActionName;

            if (query.InteractionAct is { } interactionAct &&
                header.StartsWith($"Interaction:{interactionAct}:", StringComparison.Ordinal))
            {
                return 1.0;
            }

            if (string.IsNullOrWhiteSpace(actionName))
            {
                return header.StartsWith("Interaction:", StringComparison.Ordinal) ? 0.45 : 0.20;
            }

            if (header == $"Action:{actionName}")
            {
                return 1.0;
            }

            return actionName switch
            {
                ReachOut when header.StartsWith("Interaction:SmallTalk:", StringComparison.Ordinal)
                    || header.StartsWith("Interaction:Question:", StringComparison.Ordinal)
                    || header.StartsWith("Interaction:Humor:", StringComparison.Ordinal)
                    => 0.92,
                ReachOut when IsWarmLowStakesEpisode(episode) => 0.70,
                InviteIntimacy when header.StartsWith("Interaction:SelfDisclosure:", StringComparison.Ordinal)
                    || header.StartsWith("Interaction:Validation:", StringComparison.Ordinal)
                    || header.StartsWith("Interaction:Meta:", StringComparison.Ordinal)
                    || header.StartsWith("Interaction:Invite:", StringComparison.Ordinal)
                    => 0.95,
                InviteIntimacy when IsRejectedIntimacyEpisode(episode) => 0.85,
                SelfCare when episode.Emotion == EmotionalTag.Negative => IsSocialEpisode(episode) ? 0.75 : 0.55,
                _ when IsSocialEpisode(episode) => 0.35,
                _ => 0.0
            };
        }

        private static double ComputeRecencyWeight(WTimeSpan age, WTimeSpan? recencyWindow)
        {
            var window = recencyWindow ?? WTimeSpan.FromDays(14);
            if (window.Ticks <= 0)
            {
                return 0.0;
            }

            var ratio = Math.Clamp(1.0 - ((double)age.Ticks / window.Ticks), 0.0, 1.0);
            return Math.Sqrt(ratio);
        }

        private static double EmotionalIntensity(EmotionalTag emotion)
            => emotion switch
            {
                EmotionalTag.Negative => 1.0,
                EmotionalTag.Mixed => 0.8,
                EmotionalTag.Positive => 0.7,
                _ => 0.35
            };

        #endregion Scoring

        #region Reflection

        private static IReadOnlyList<ReflectionSummary> BuildReflections(MemoryRecallQuery query, IReadOnlyList<MemoryRecallItem> recalledItems, WDateTime now)
        {
            var reflections = new List<ReflectionSummary>();
            var targetEpisodes = recalledItems
                .Where(item => query.TargetHuman is null || item.Episode.OtherPerson == query.TargetHuman)
                .Select(item => item.Episode)
                .ToList();

            if (query.TargetHuman is not null)
            {
                var warmthSignals = targetEpisodes.Count(IsWarmLowStakesEpisode);
                var safeSignals = targetEpisodes.Count(IsSafeEpisode);
                var intimacyRejections = targetEpisodes.Count(IsRejectedIntimacyEpisode);
                var recentNegativeSocial = targetEpisodes.Count(episode =>
                    IsSocialEpisode(episode) &&
                    episode.Emotion == EmotionalTag.Negative &&
                    now - episode.When <= WTimeSpan.FromDays(5));

                if (warmthSignals >= 2)
                {
                    reflections.Add(new ReflectionSummary(
                        ReflectionSummaryKind.WarmForCasualContact,
                        query.TargetHuman,
                        Math.Min(1.0, (warmthSignals * 0.18) + 0.12),
                        warmthSignals,
                        "Repeated low-stakes contact felt warm."));
                }

                if (safeSignals >= 2)
                {
                    reflections.Add(new ReflectionSummary(
                        ReflectionSummaryKind.SafeForReachOut,
                        query.TargetHuman,
                        Math.Min(1.0, (safeSignals * 0.20) + 0.10),
                        safeSignals,
                        "Recent contact with this person has been safe enough for outreach."));
                }

                if (intimacyRejections >= 2)
                {
                    reflections.Add(new ReflectionSummary(
                        ReflectionSummaryKind.RejectsIntimacy,
                        query.TargetHuman,
                        Math.Min(1.0, (intimacyRejections * 0.22) + 0.16),
                        intimacyRejections,
                        "Repeated vulnerable contact was rejected or emotionally costly."));
                }

                if (recentNegativeSocial >= 2)
                {
                    reflections.Add(new ReflectionSummary(
                        ReflectionSummaryKind.RecentSocialCost,
                        query.TargetHuman,
                        Math.Min(1.0, (recentNegativeSocial * 0.16) + 0.12),
                        recentNegativeSocial,
                        "Recent interactions with this person have been emotionally costly."));
                }
            }
            else
            {
                var recentNegativeSocial = recalledItems.Count(item =>
                    item.Episode.Emotion == EmotionalTag.Negative &&
                    IsSocialEpisode(item.Episode) &&
                    now - item.Episode.When <= WTimeSpan.FromDays(5));

                if (recentNegativeSocial >= 2)
                {
                    reflections.Add(new ReflectionSummary(
                        ReflectionSummaryKind.RecentSocialCost,
                        null,
                        Math.Min(1.0, (recentNegativeSocial * 0.18) + 0.12),
                        recentNegativeSocial,
                        "Recent social contact has been emotionally costly overall."));
                }
            }

            return reflections
                .OrderByDescending(summary => summary.Strength)
                .ThenByDescending(summary => summary.EvidenceCount)
                .Take(3)
                .ToList();
        }

        #endregion Reflection

        #region Episode helpers

        private static EpisodicMemory Reconstruct(EpisodicMemory episode)
        {
            if (episode.Distortion <= 0.01)
            {
                return episode with { PerceivedWhat = episode.PerceivedWhat ?? episode.What };
            }

            return episode with
            {
                PerceivedWhat = episode.PerceivedWhat ?? episode.What,
                RecallConfidence = Math.Clamp(episode.RecallConfidence - (episode.Distortion * 0.15), 0.1, 1.0)
            };
        }

        private static bool IsSocialEpisode(EpisodicMemory episode)
            => (episode.PerceivedWhat ?? episode.What).Contains("Interaction:", StringComparison.Ordinal)
                || (episode.PerceivedWhat ?? episode.What).Contains("Relation:", StringComparison.Ordinal)
                || episode.OtherPerson is not null;

        private static bool IsWarmLowStakesEpisode(EpisodicMemory episode)
        {
            var perceived = episode.PerceivedWhat ?? episode.What;
            var header = MemoryWhatParser.GetHeader(perceived);
            return episode.Emotion == EmotionalTag.Positive &&
                   (header.StartsWith("Interaction:SmallTalk:Accepted", StringComparison.Ordinal)
                    || header.StartsWith("Interaction:Question:Accepted", StringComparison.Ordinal)
                    || header.StartsWith("Interaction:Humor:Accepted", StringComparison.Ordinal)
                    || header.StartsWith("Relation:MicroPositive", StringComparison.Ordinal)
                    || perceived.StartsWith("PerceivedWarmth:", StringComparison.Ordinal));
        }

        private static bool IsSafeEpisode(EpisodicMemory episode)
        {
            var perceived = episode.PerceivedWhat ?? episode.What;
            var header = MemoryWhatParser.GetHeader(perceived);
            return episode.Emotion == EmotionalTag.Positive &&
                   (header.StartsWith("Interaction:SmallTalk:Accepted", StringComparison.Ordinal)
                    || header.StartsWith("Interaction:Question:Accepted", StringComparison.Ordinal)
                    || header.StartsWith("Interaction:Validation:Accepted", StringComparison.Ordinal)
                    || header.StartsWith("Interaction:SelfDisclosure:Accepted", StringComparison.Ordinal)
                    || header.StartsWith("Relation:Repair:Accepted", StringComparison.Ordinal));
        }

        private static bool IsRejectedIntimacyEpisode(EpisodicMemory episode)
        {
            var perceived = episode.PerceivedWhat ?? episode.What;
            var header = MemoryWhatParser.GetHeader(perceived);
            return episode.Emotion != EmotionalTag.Positive &&
                   (header.StartsWith("Interaction:Invite:Rejected", StringComparison.Ordinal)
                    || header.StartsWith("Interaction:SelfDisclosure:Rejected", StringComparison.Ordinal)
                    || header.StartsWith("Interaction:Validation:Rejected", StringComparison.Ordinal)
                    || header.StartsWith("Interaction:Meta:Rejected", StringComparison.Ordinal)
                    || header.StartsWith("Relation:Repair:Rejected", StringComparison.Ordinal)
                    || perceived.StartsWith("PerceivedThreat:Interaction:Invite", StringComparison.Ordinal));
        }

        #endregion Episode helpers
    }
}
