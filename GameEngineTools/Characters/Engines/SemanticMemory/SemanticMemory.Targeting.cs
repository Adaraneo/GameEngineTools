// SemanticMemory.Targeting.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using System;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;

    public enum SocialTargetMode
    { ReachOut, Vulnerability, Intimacy }

    public sealed record SocialTargetScore(
        HumanId Target,
        double Score,
        double ExpectedAcceptance,
        SpeechAct EvaluatedAct,
        double VulnerabilitySafety,
        double RejectionRisk,
        bool PsychologicallyBlocked = false,
        string? Reason = null);

    public static class SemanticTargeting
    {
        public static SocialTargetScore ScoreTarget(
            IHuman initiator,
            IHuman target,
            SocialTargetMode mode)
            => ScoreTarget(initiator.Id, initiator.PsychologyProfile, initiator.Snapshot.Relationships, initiator.Snapshot.Memory, initiator.Snapshot.SemanticMemory, target.Id, mode);

        public static SocialTargetScore ScoreTarget(
            IHumanContext initiator,
            HumanId target,
            SocialTargetMode mode)
            => ScoreTarget(initiator.Id, initiator.PsychologyProfile, initiator.Snapshot.Relationships, initiator.Snapshot.Memory, initiator.Snapshot.SemanticMemory, target, mode);

        public static IReadOnlyList<SocialTargetScore> RankTargets(
            IHumanContext initiator,
            IEnumerable<HumanId> candidates,
            SocialTargetMode mode,
            int take = 3)
        {
            return candidates
                .Distinct()
                .Where(id => id != initiator.Id)
                .Select(id => ScoreTarget(initiator, id, mode))
                .OrderByDescending(score => score.Score)
                .ThenBy(score => score.Target.Value)
                .Take(Math.Max(1, take))
                .ToList();
        }

        public static IHuman? ChooseTarget(
            IHuman initiator,
            IReadOnlyList<IHuman> candidates,
            SocialTargetMode mode)
        {
            return candidates
                .Select(candidate => (Candidate: candidate, Score: ScoreTarget(initiator, candidate, mode)))
                .OrderByDescending(entry => entry.Score.Score)
                .ThenBy(entry => entry.Candidate.Id.Value)
                .Select(entry => entry.Candidate)
                .FirstOrDefault();
        }

        private static SocialTargetScore ScoreTarget(
            HumanId initiatorId,
            PsychologicalProfile profile,
            RelationshipState relationships,
            MemoryIndex memoryIndex,
            SemanticMemoryState? semanticMemory,
            HumanId target,
            SocialTargetMode mode)
        {
            var act = mode switch
            {
                SocialTargetMode.Intimacy => SpeechAct.Invite,
                SocialTargetMode.Vulnerability => SpeechAct.SelfDisclosure,
                _ => SpeechAct.SmallTalk
            };

            var relationship = relationships.Edges.GetValueOrDefault(target);
            var memory = memoryIndex.Episodes;
            var expected = SemanticMemoryMath.ExpectedAcceptance(semanticMemory, target, act, relationship, profile, memory);
            var baseScore = SemanticMemoryMath.ScoreApproachTarget(semanticMemory, target, relationship, profile, memory, act);
            var safe = semanticMemory?.GetStrength(target, PersonBeliefKind.EmotionallySafe) ?? 0.0;
            var warm = semanticMemory?.GetStrength(target, PersonBeliefKind.Warm) ?? 0.0;
            var rejecting = semanticMemory?.GetStrength(target, PersonBeliefKind.Rejecting) ?? 0.0;
            var critical = semanticMemory?.GetStrength(target, PersonBeliefKind.Critical) ?? 0.0;
            var vulnerabilitySafety = Math.Clamp(expected * 0.55 + safe * 0.30 + warm * 0.15, 0.0, 1.0);
            var rejectionRisk = Math.Clamp((1.0 - expected) * 0.55 + rejecting * 0.30 + critical * 0.15, 0.0, 1.0);
            var blocked = IsPsychologicallyBlocked(mode, profile, vulnerabilitySafety, rejectionRisk, relationship);
            var score = blocked ? 0.0 : Math.Clamp(baseScore + RecentSalience(memory, target) * 0.10, 0.0, 1.0);
            var reason = blocked
                ? $"blocked:{mode}:risk={rejectionRisk:0.00}:safe={vulnerabilitySafety:0.00}"
                : $"mode={mode};expected={expected:0.00};safe={vulnerabilitySafety:0.00};risk={rejectionRisk:0.00}";

            return new SocialTargetScore(target, score, expected, act, vulnerabilitySafety, rejectionRisk, blocked, reason);
        }

        private static bool IsPsychologicallyBlocked(
            SocialTargetMode mode,
            PsychologicalProfile profile,
            double vulnerabilitySafety,
            double rejectionRisk,
            RelationshipEdge? relationship)
        {
            if (mode == SocialTargetMode.ReachOut)
            {
                return false;
            }

            var closeness = (relationship?.Closeness ?? 0.0) / 100.0;
            if (mode == SocialTargetMode.Intimacy)
            {
                return vulnerabilitySafety < 0.38 || rejectionRisk > 0.72 || closeness < 0.40;
            }

            var selfProtective = profile.Coping is CopingStyle.Avoidant or CopingStyle.AggressiveCompensation;
            return vulnerabilitySafety < (selfProtective ? 0.48 : 0.36) || rejectionRisk > (selfProtective ? 0.62 : 0.72);
        }

        private static double RecentSalience(IReadOnlyList<EpisodicMemory> episodes, HumanId target)
        {
            return Math.Clamp(
                episodes
                    .Where(e => e.OtherPerson == target)
                    .OrderByDescending(e => e.When)
                    .Take(2)
                    .Sum(e => e.Strength * (e.Emotion == EmotionalTag.Positive ? 1.0 : e.Emotion == EmotionalTag.Negative ? -1.0 : 0.25)),
                -1.0,
                1.0);
        }
    }
}
