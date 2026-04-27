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
        private const double MinimumRecallRelevance = 0.20;

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

        public static DecisionWorkingSet BuildWorkingSet(
            MemoryIndex memory, MemoryRecallQuery query, WDateTime now, double threshold = 0.65)
        {
            if (query.CognitiveBurden.HasValue && query.CognitiveBurden.Value > threshold)
                return BuildSystem1WorkingSet(memory, query, now);

            return BuildSystem2WorkingSet(memory, query, now);
        }

        private static DecisionWorkingSet BuildSystem2WorkingSet(
            MemoryIndex memory, MemoryRecallQuery query, WDateTime now)
        {
            var recall = Recall(memory, query, now);
            var reflectionEpisodes = CollectReflectionEpisodes(memory, query, now);
            var reflections = BuildReflections(query, reflectionEpisodes, now);
            return new DecisionWorkingSet(
                query.TargetHuman,
                query.ActionName,
                query.InteractionAct,
                recall.Items,
                reflections,
                IsSystem1: false);
        }

        // System 1: kognitivní zátěž překračuje threshold — přeskočí episodický recall,
        // zachová pouze reflection summaries odvozené ze semantic beliefs.
        private static DecisionWorkingSet BuildSystem1WorkingSet(
            MemoryIndex memory, MemoryRecallQuery query, WDateTime now)
        {
            var reflectionEpisodes = CollectReflectionEpisodes(memory, query, now);
            var reflections = BuildReflections(query, reflectionEpisodes, now);
            return new DecisionWorkingSet(
                query.TargetHuman,
                query.ActionName,
                query.InteractionAct,
                RecalledEpisodes: Array.Empty<MemoryRecallItem>(),
                Reflections: reflections,
                IsSystem1: true);
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

            var targetMatched = query.TargetHuman is not null && episode.OtherPerson == query.TargetHuman;
            if (query.TargetHuman is not null && !targetMatched)
            {
                return null;
            }

            var situationScore = ComputeSituationScore(episode, query);
            if (query.ActionName is not null && situationScore <= 0.15)
            {
                return null;
            }

            if (query.EmotionalValence is not null && !IsEmotionCompatible(episode.Emotion, query.EmotionalValence.Value, query.ActionName))
            {
                return null;
            }

            var emotionalMatched = query.EmotionalValence is null || episode.Emotion == query.EmotionalValence;
            var emotionScore = query.EmotionalValence is null
                ? EmotionalIntensity(episode.Emotion)
                : emotionalMatched ? 1.0 : 0.0;
            var recencyWeight = ComputeRecencyWeight(age, query.RecencyWindow);
            var targetScore = ComputeTargetScore(episode, query);
            var confidenceScore = Math.Clamp(episode.RecallConfidence, 0.0, 1.0);
            // Salience is the primary encoding signal (encoding specificity principle, Tulving).
            // Strength is derived from decay and reinforcement — secondary to initial salience.
            var neuroticismBias = ComputeNeuroticismMoodBias(
                episode.Emotion, query.CurrentValence, query.NeuroticismScore);
            var relevance = Math.Clamp(
                (targetScore    * 0.30) +
                (situationScore * 0.24) +
                (recencyWeight  * 0.18) +
                (Math.Clamp(episode.Salience, 0.0, 1.0) * 0.16) +
                (Math.Clamp(episode.Strength, 0.0, 1.0) * 0.08) +
                (emotionScore   * 0.08) +
                (confidenceScore * 0.04) +
                neuroticismBias,
                0.0, 1.0);

            if (relevance < MinimumRecallRelevance)
            {
                return null;
            }

            return new MemoryRecallItem(
                episode,
                Math.Round(relevance, 6),
                targetMatched,
                situationScore >= 0.55,
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
                return header.StartsWith("Interaction:", StringComparison.Ordinal) ? 0.55 : IsSocialEpisode(episode) ? 0.28 : 0.10;
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
                SelfCare when episode.Emotion == EmotionalTag.Negative || episode.Emotion == EmotionalTag.Mixed
                    => IsSocialEpisode(episode) ? 0.82 : 0.60,
                SelfCare => 0.05,
                _ when IsSocialEpisode(episode) => 0.22,
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
            return Math.Pow(ratio, 0.70);
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

        private static IReadOnlyList<EpisodicMemory> CollectReflectionEpisodes(MemoryIndex memory, MemoryRecallQuery query, WDateTime now)
        {
            var window = query.RecencyWindow ?? WTimeSpan.FromDays(21);
            return memory.Episodes
                .Select(Reconstruct)
                .Where(episode => WTimeSpan.Abs(now - episode.When) <= window)
                .Where(episode => query.TargetHuman is null || episode.OtherPerson == query.TargetHuman)
                .Where(episode => ComputeSituationScore(episode, query) >= 0.20 || IsSocialEpisode(episode))
                .OrderByDescending(episode => episode.When.WorldTicks)
                .ThenBy(episode => episode.Id)
                .Take(10)
                .ToList();
        }

        private static IReadOnlyList<ReflectionSummary> BuildReflections(MemoryRecallQuery query, IReadOnlyList<EpisodicMemory> episodes, WDateTime now)
        {
            var reflections = new List<ReflectionSummary>();
            var targetEpisodes = episodes.ToList();

            if (query.TargetHuman is not null)
            {
                var warmthSignals = WeightedEpisodeScore(targetEpisodes, IsWarmLowStakesEpisode, now);
                var safeSignals = WeightedEpisodeScore(targetEpisodes, IsSafeEpisode, now);
                var intimacyRejections = WeightedEpisodeScore(targetEpisodes, IsRejectedIntimacyEpisode, now);
                var positiveVulnerability = WeightedEpisodeScore(targetEpisodes, IsPositiveVulnerabilityEpisode, now);
                var recentNegativeSocial = WeightedEpisodeScore(targetEpisodes, episode =>
                    IsSocialEpisode(episode) &&
                    (episode.Emotion == EmotionalTag.Negative || episode.Emotion == EmotionalTag.Mixed), now);
                var recentPositiveSocial = WeightedEpisodeScore(targetEpisodes, episode =>
                    IsWarmLowStakesEpisode(episode), now);

                if (warmthSignals >= 1.5)
                {
                    var strength = Math.Clamp(0.10 + (warmthSignals * 0.12) - (recentNegativeSocial * 0.05), 0.0, 0.65);
                    if (strength >= 0.20)
                    {
                    reflections.Add(new ReflectionSummary(
                        ReflectionSummaryKind.WarmForCasualContact,
                        query.TargetHuman,
                        strength,
                        (int)Math.Round(warmthSignals),
                        "Repeated low-stakes contact felt warm."));
                    }
                }

                if (safeSignals >= 1.5)
                {
                    var strength = Math.Clamp(0.08 + (safeSignals * 0.14) + (recentPositiveSocial * 0.03) - (recentNegativeSocial * 0.06), 0.0, 0.70);
                    if (strength >= 0.22)
                    {
                    reflections.Add(new ReflectionSummary(
                        ReflectionSummaryKind.SafeForReachOut,
                        query.TargetHuman,
                        strength,
                        (int)Math.Round(safeSignals),
                        "Recent contact with this person has been safe enough for outreach."));
                    }
                }

                #region edit

                var strongRecentRejection = targetEpisodes
                    .Where(e =>
                        IsRejectedIntimacyEpisode(e) &&
                        now - e.When <= WTimeSpan.FromDays(2) &&
                        e.Strength >= 0.90 &&
                        e.Salience >= 0.85)
                    .ToList();

                if (strongRecentRejection.Count >= 1 &&
                    !reflections.Any(r => r.Kind == ReflectionSummaryKind.RejectsIntimacy))
                {
                    var episode = strongRecentRejection[0];
                    var strength = Math.Clamp(0.18 + (episode.Strength * 0.18), 0.18, 0.38);

                    reflections.Add(new ReflectionSummary(
                        ReflectionSummaryKind.RejectsIntimacy,
                        query.TargetHuman,
                        strength,
                        1,
                        "A recent intense rejection still weighs on vulnerable contact."));
                }

                #endregion

                if (intimacyRejections >= 1.5)
                {
                    var strength = Math.Clamp(0.14 + (intimacyRejections * 0.18) - (positiveVulnerability * 0.10), 0.0, 0.78);
                    if (strength >= 0.25)
                    {
                    reflections.Add(new ReflectionSummary(
                        ReflectionSummaryKind.RejectsIntimacy,
                        query.TargetHuman,
                        strength,
                        (int)Math.Round(intimacyRejections),
                        "Repeated vulnerable contact was rejected or emotionally costly."));
                    }
                }

                if (recentNegativeSocial >= 1.5)
                {
                    var strength = Math.Clamp(0.08 + (recentNegativeSocial * 0.14) - (recentPositiveSocial * 0.05), 0.0, 0.68);
                    if (strength >= 0.20)
                    {
                    reflections.Add(new ReflectionSummary(
                        ReflectionSummaryKind.RecentSocialCost,
                        query.TargetHuman,
                        strength,
                        (int)Math.Round(recentNegativeSocial),
                        "Recent interactions with this person have been emotionally costly."));
                    }
                }
            }
            else
            {
                var recentNegativeSocial = WeightedEpisodeScore(targetEpisodes, episode =>
                    (episode.Emotion == EmotionalTag.Negative || episode.Emotion == EmotionalTag.Mixed) &&
                    IsSocialEpisode(episode), now);

                if (recentNegativeSocial >= 1.5)
                {
                    var strength = Math.Clamp(0.10 + (recentNegativeSocial * 0.14), 0.0, 0.62);
                    reflections.Add(new ReflectionSummary(
                        ReflectionSummaryKind.RecentSocialCost,
                        null,
                        strength,
                        (int)Math.Round(recentNegativeSocial),
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

        private static bool IsPositiveVulnerabilityEpisode(EpisodicMemory episode)
        {
            var perceived = episode.PerceivedWhat ?? episode.What;
            var header = MemoryWhatParser.GetHeader(perceived);
            return episode.Emotion == EmotionalTag.Positive &&
                   (header.StartsWith("Interaction:Invite:Accepted", StringComparison.Ordinal)
                    || header.StartsWith("Interaction:SelfDisclosure:Accepted", StringComparison.Ordinal)
                    || header.StartsWith("Interaction:Validation:Accepted", StringComparison.Ordinal)
                    || header.StartsWith("Interaction:Meta:Accepted", StringComparison.Ordinal));
        }

        // Mood repair (low N) vs. negativní spirála (high N) — Bower 1981.
        // Při dobré náladě není žádný bias bez ohledu na N.
        private static double ComputeNeuroticismMoodBias(
            EmotionalTag episodeEmotion, double currentValence, double neuroticism)
        {
            if (currentValence >= 0.0) return 0.0;

            var isPositive = episodeEmotion == EmotionalTag.Positive;
            var isNegative = episodeEmotion is EmotionalTag.Negative or EmotionalTag.Mixed;

            if (neuroticism < 0.4)
                return isPositive ? +0.10 : 0.0;

            if (neuroticism > 0.6)
                return isPositive ? -0.08 : isNegative ? +0.06 : 0.0;

            return 0.0;
        }

        private static bool IsEmotionCompatible(EmotionalTag episodeEmotion, EmotionalTag queryEmotion, string? actionName)
            => actionName switch
            {
                SelfCare => queryEmotion == EmotionalTag.Negative
                    ? episodeEmotion is EmotionalTag.Negative or EmotionalTag.Mixed
                    : episodeEmotion == queryEmotion,
                _ => episodeEmotion == queryEmotion
            };

        private static double ComputeTargetScore(EpisodicMemory episode, MemoryRecallQuery query)
        {
            if (query.TargetHuman is not null)
            {
                return episode.OtherPerson == query.TargetHuman ? 1.0 : 0.0;
            }

            if (query.ActionName is SelfCare)
            {
                return episode.OtherPerson is not null && IsSocialEpisode(episode) ? 0.70 : IsSocialEpisode(episode) ? 0.45 : 0.18;
            }

            if (query.ActionName is ReachOut or InviteIntimacy)
            {
                return episode.OtherPerson is not null ? 0.72 : IsSocialEpisode(episode) ? 0.30 : 0.08;
            }

            return episode.OtherPerson is not null ? 0.55 : 0.15;
        }

        private static double WeightedEpisodeScore(
            IReadOnlyList<EpisodicMemory> episodes, 
            Func<EpisodicMemory, bool> predicate, 
            WDateTime now)
        {
            return episodes
                .Where(predicate)
                .Sum(e =>
                {
                    var ageDays = (now - e.When).TotalDays;
                    return 1.0 / (1.0 + ageDays / 7.0); // half-life ~7 dní
                });
        }

        #endregion Episode helpers
    }
}
