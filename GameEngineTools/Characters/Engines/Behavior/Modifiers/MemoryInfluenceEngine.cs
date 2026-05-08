// MemoryInfluenceEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.World.Utils.Time;
    using static ActionNames;

    /// <summary>
    /// Applies candidate-specific memory influence from targeted recall and compact reflections.
    /// </summary>
    internal sealed class MemoryInfluenceEngine : IBehaviorModifierEngine
    {
        #region IBehaviorModifierEngine

        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var memory = context.HumanContext.Snapshot.Memory;
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var query = BuildQuery(candidate);
                if (query is null)
                {
                    continue;
                }

                var workingSet = MemoryCognition.BuildWorkingSet(memory, query, context.Now);
                RememberWorkingSet(context, candidate, workingSet);
                EmitRecallEvents(context, candidate, workingSet);
                var multiplier = ComputeMultiplier(candidate, workingSet);
                multiplier *= ComputeSemanticFallbackMultiplier(context, candidate);
                candidates[i] = candidate with { Utility = Math.Max(0.0, candidate.Utility * multiplier) };
            }

            // ToM knowledge modifier: known betrayals/negative acts reduce social utility;
            // known positive acts and self-disclosures give a slight boost.
            ApplyKnowledgeModifiers(context, candidates);
        }

        private static void ApplyKnowledgeModifiers(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var knowledge = context.HumanContext.Snapshot.Memory.Knowledge;
            if (knowledge.Count == 0) return;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate.Name != ReachOut) continue;
                if (candidate.SocialTargeting is not { } targeting) continue;

                var targetId = targeting.TargetHuman;
                var betrayalConf = knowledge
                    .Where(f => f.Subject == targetId && f.ActionKind == "Betrayal")
                    .Select(f => f.Confidence)
                    .DefaultIfEmpty(0.0).Max();
                var negativeConf = knowledge
                    .Where(f => f.Subject == targetId && f.ActionKind == "NegativeAct")
                    .Select(f => f.Confidence)
                    .DefaultIfEmpty(0.0).Max();
                var positiveConf = knowledge
                    .Where(f => f.Subject == targetId && (f.ActionKind == "PositiveAct" || f.ActionKind == "SelfDisclosure"))
                    .Select(f => f.Confidence)
                    .DefaultIfEmpty(0.0).Max();

                var knowledgeBias = positiveConf * 3.0 - betrayalConf * 8.0 - negativeConf * 4.0;
                if (Math.Abs(knowledgeBias) > 0.01)
                    candidates[i] = candidate with { Utility = Math.Max(0.0, candidate.Utility + knowledgeBias) };
            }
        }

        #endregion

        #region Query building

        private static MemoryRecallQuery? BuildQuery(BehaviorCandidate candidate)
            => candidate.Name switch
            {
                ReachOut => new MemoryRecallQuery(
                    candidate.SocialTargeting?.TargetHuman,
                    ReachOut,
                    candidate.SocialTargeting?.SpeechAct,
                    null,
                    RecencyWindow: WTimeSpan.FromDays(14),
                    Take: 4),
                InviteIntimacy => new MemoryRecallQuery(
                    candidate.SocialTargeting?.TargetHuman,
                    InviteIntimacy,
                    candidate.SocialTargeting?.SpeechAct,
                    null,
                    RecencyWindow: WTimeSpan.FromDays(21),
                    Take: 4),
                SelfCare => new MemoryRecallQuery(
                    null,
                    SelfCare,
                    null,
                    EmotionalTag.Negative,
                    RecencyWindow: WTimeSpan.FromDays(7),
                    Take: 4),
                _ => null
            };

        private static void RememberWorkingSet(BehaviorContext context, BehaviorCandidate candidate, DecisionWorkingSet workingSet)
        {
            if (context.DecisionWorkingSets is null)
            {
                return;
            }

            context.DecisionWorkingSets[BuildWorkingSetKey(candidate)] = workingSet;
        }

        private static string BuildWorkingSetKey(BehaviorCandidate candidate)
            => candidate.SocialTargeting is { } targeting
                ? $"action={candidate.Name}|target={targeting.TargetHuman.Value:N}|act={targeting.SpeechAct}"
                : $"action={candidate.Name}|target=none|act=none";

        #endregion

        #region Events

        private static void EmitRecallEvents(BehaviorContext context, BehaviorCandidate candidate, DecisionWorkingSet workingSet)
        {
            if (workingSet.RecalledEpisodes.Count == 0 && workingSet.Reflections.Count == 0)
            {
                return;
            }

            context.Outbox.Add(new MemoryRecallEvaluated(
                context.Now,
                context.HumanContext.Id,
                candidate.Name,
                workingSet.TargetHuman,
                workingSet.RecalledEpisodes.Count));

            foreach (var item in workingSet.RecalledEpisodes)
            {
                context.Outbox.Add(new MemoryRecalled(context.Now, context.HumanContext.Id, item.Episode.Id));
            }

            foreach (var reflection in workingSet.Reflections)
            {
                context.Outbox.Add(new ReflectionApplied(
                    context.Now,
                    context.HumanContext.Id,
                    candidate.Name,
                    reflection.TargetHuman,
                    reflection.Kind,
                    reflection.Strength));
            }
        }

        #endregion

        #region Influence scoring

        private static double ComputeMultiplier(BehaviorCandidate candidate, DecisionWorkingSet workingSet)
            => candidate.Name switch
            {
                ReachOut => ComputeReachOutMultiplier(workingSet),
                InviteIntimacy => ComputeInviteIntimacyMultiplier(workingSet),
                SelfCare => ComputeSelfCareMultiplier(workingSet),
                _ => 1.0
            };

        private static double ComputeReachOutMultiplier(DecisionWorkingSet workingSet)
        {
            var positive = SumEpisodeSignal(
                workingSet.RecalledEpisodes,
                item => item.Episode.Emotion == EmotionalTag.Positive && item.SituationMatched,
                0.17);
            var negative = SumEpisodeSignal(
                workingSet.RecalledEpisodes,
                item => (item.Episode.Emotion == EmotionalTag.Negative || item.Episode.Emotion == EmotionalTag.Mixed) && IsSocialEpisode(item),
                0.18);
            var safe = workingSet.Reflections
                .Where(summary => summary.Kind is ReflectionSummaryKind.SafeForReachOut or ReflectionSummaryKind.WarmForCasualContact)
                .Sum(summary => summary.Strength * 0.16);
            var costly = workingSet.Reflections
                .Where(summary => summary.Kind == ReflectionSummaryKind.RecentSocialCost)
                .Sum(summary => summary.Strength * 0.18);

            var boost = Math.Min(0.24, positive + safe);
            var penalty = Math.Min(0.32, negative + costly);
            return Math.Clamp(1.0 + boost - penalty, 0.68, 1.24);
        }

        private static double ComputeInviteIntimacyMultiplier(DecisionWorkingSet workingSet)
        {
            var positiveVulnerability = SumEpisodeSignal(
                workingSet.RecalledEpisodes,
                item => item.Episode.Emotion == EmotionalTag.Positive && item.SituationMatched,
                0.12);
            var negativeVulnerability = SumEpisodeSignal(
                workingSet.RecalledEpisodes,
                item => (item.Episode.Emotion == EmotionalTag.Negative || item.Episode.Emotion == EmotionalTag.Mixed) && item.SituationMatched,
                0.24);
            var rejectionPattern = workingSet.Reflections
                .Where(summary => summary.Kind == ReflectionSummaryKind.RejectsIntimacy)
                .Sum(summary => summary.Strength * 0.32);
            var safety = workingSet.Reflections
                .Where(summary => summary.Kind == ReflectionSummaryKind.SafeForReachOut)
                .Sum(summary => summary.Strength * 0.08);
            var socialCost = workingSet.Reflections
                .Where(summary => summary.Kind == ReflectionSummaryKind.RecentSocialCost)
                .Sum(summary => summary.Strength * 0.12);

            var boost = Math.Min(0.18, positiveVulnerability + safety);
            var penalty = Math.Min(0.58, negativeVulnerability + rejectionPattern + socialCost);
            return Math.Clamp(1.0 + boost - penalty, 0.35, 1.18);
        }

        private static double ComputeSelfCareMultiplier(DecisionWorkingSet workingSet)
        {
            var negativeLoad = SumEpisodeSignal(
                workingSet.RecalledEpisodes,
                item => item.Episode.Emotion is EmotionalTag.Negative or EmotionalTag.Mixed,
                0.15);
            var socialCost = workingSet.Reflections
                .Where(summary => summary.Kind == ReflectionSummaryKind.RecentSocialCost)
                .Sum(summary => summary.Strength * 0.22);

            var boost = Math.Min(0.32, negativeLoad + socialCost);
            return Math.Clamp(1.0 + boost, 1.0, 1.32);
        }

        private static double ComputeSemanticFallbackMultiplier(BehaviorContext context, BehaviorCandidate candidate)
        {
            if (candidate.Name is not (ReachOut or InviteIntimacy))
            {
                return 1.0;
            }

            var semantic = context.HumanContext.Snapshot.SemanticMemory ?? SemanticMemoryState.Empty;
            if (semantic.People.Count == 0)
            {
                return 1.0;
            }

            var relationships = context.HumanContext.Snapshot.Relationships.Edges;
            var profile = context.HumanContext.PsychologyProfile;
            var episodes = context.HumanContext.Snapshot.Memory.Episodes;
            var act = candidate.Name == InviteIntimacy ? SpeechAct.Invite : SpeechAct.SmallTalk;

            double expected;
            if (candidate.SocialTargeting is { } targeting)
            {
                expected = semantic.ExpectedAcceptance(
                    targeting.TargetHuman,
                    targeting.SpeechAct,
                    relationships.GetValueOrDefault(targeting.TargetHuman),
                    profile,
                    episodes);
            }
            else
            {
                expected = semantic.People.Keys.Max(other =>
                    semantic.ExpectedAcceptance(other, act, relationships.GetValueOrDefault(other), profile, episodes));
            }

            return candidate.Name switch
            {
                ReachOut when expected >= 0.62 => 1.0 + Math.Min(0.18, (expected - 0.5) * 0.6),
                ReachOut when expected <= 0.40 => 1.0 - Math.Min(0.22, (0.5 - expected) * 0.8),
                InviteIntimacy when expected >= 0.65 => 1.0 + Math.Min(0.15, (expected - 0.5) * 0.5),
                InviteIntimacy when expected <= 0.38 => 1.0 - Math.Min(0.24, (0.5 - expected) * 0.7),
                _ => 1.0
            };
        }

        private static double SumEpisodeSignal(
            IReadOnlyList<MemoryRecallItem> items,
            Func<MemoryRecallItem, bool> predicate,
            double scale)
            => Math.Min(
                0.35,
                items
                    .Where(predicate)
                    .Take(3)
                    .Sum(item => (((item.Episode.Strength * 0.45) + (item.Relevance * 0.35) + (item.RecencyWeight * 0.20)) * scale)));

        private static bool IsSocialEpisode(MemoryRecallItem item)
            => item.Episode.OtherPerson is not null
                || (item.Episode.PerceivedWhat ?? item.Episode.What).Contains("Interaction:", StringComparison.Ordinal)
                || (item.Episode.PerceivedWhat ?? item.Episode.What).Contains("Relation:", StringComparison.Ordinal);

        #endregion
    }
}
