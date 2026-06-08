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

    /// <summary>
    /// Social-approach intent — determines which SpeechAct is used and how strict
    /// the psychological-blocking conditions are.
    /// </summary>
    public enum SocialTargetMode
    {
        /// <summary>Casual social contact (SmallTalk). Never psychologically blocked.</summary>
        ReachOut,

        /// <summary>Opening up to vulnerability (SelfDisclosure). Blocked when EmotionallySafe is low.</summary>
        Vulnerability,

        /// <summary>Intimate approach (Invite). Blocked by sociosexuality and orientation.</summary>
        Intimacy
    }

    /// <summary>
    /// Result of evaluating a single candidate for social approach.
    /// Contains the score, the acceptance prediction and diagnostic fields.
    /// </summary>
    public sealed record SocialTargetScore(
        HumanId Target,
        /// <summary>Overall target-suitability score [0.0–1.0]. 0.0 if psychologically blocked.</summary>
        double Score,
        /// <summary>Predicted probability of the approach being accepted [0.05–0.95].</summary>
        double ExpectedAcceptance,
        /// <summary>The SpeechAct used during evaluation.</summary>
        SpeechAct EvaluatedAct,
        /// <summary>Degree of safety for vulnerability [0.0–1.0].</summary>
        double VulnerabilitySafety,
        /// <summary>Estimated risk of rejection [0.0–1.0].</summary>
        double RejectionRisk,
        bool PsychologicallyBlocked = false,
        string? Reason = null);

    /// <summary>
    /// Static scoring subsystem for selecting and ranking social targets
    /// based on semantic memory, relationships and the psychological profile.
    /// </summary>
    public static class SemanticTargeting
    {
        /// <summary>
        /// Evaluates a single candidate from the initiator → target perspective.
        /// </summary>
        public static SocialTargetScore ScoreTarget(
            IHuman initiator,
            IHuman target,
            SocialTargetMode mode)
            => ScoreTarget(initiator.Id, initiator.Personality.Sociosexuality, initiator.PsychologyProfile, initiator.AttractionProfile, initiator.Snapshot.Relationships, initiator.Snapshot.Memory, initiator.Snapshot.SemanticMemory, target.Id, mode, target.Biology);

        /// <summary>
        /// Evaluates a single candidate from the initiator (context) → target (id) perspective.
        /// </summary>
        public static SocialTargetScore ScoreTarget(
            IHumanContext initiator,
            HumanId target,
            SocialTargetMode mode)
            => ScoreTarget(initiator.Id, initiator.Personality.Sociosexuality, initiator.PsychologyProfile, initiator.AttractionProfile, initiator.Snapshot.Relationships, initiator.Snapshot.Memory, initiator.Snapshot.SemanticMemory, target, mode, null);

        /// <summary>
        /// Sorts candidates by score descending. Returns at most <paramref name="take"/> results.
        /// Deterministic — ties broken by <see cref="HumanId.Value"/>.
        /// </summary>
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

        /// <summary>
        /// Selects the best candidate by score. Returns <see langword="null"/> if the list is empty.
        /// </summary>
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

        /// <summary>
        /// Picks a target by softmax-sampling over the candidate scores instead of taking the
        /// single best (<see cref="ChooseTarget"/>). Warm targets stay strongly preferred, but
        /// neutral strangers get a non-zero chance — this breaks the deterministic argmax
        /// monopoly that otherwise freezes a neutral-start population at a few bonded pairs.
        /// </summary>
        /// <param name="initiator">The initiator choosing whom to approach.</param>
        /// <param name="candidates">Perceived, co-located candidates.</param>
        /// <param name="mode">Reach-out vs intimacy targeting mode.</param>
        /// <param name="rng">Random source for the weighted pick.</param>
        /// <param name="temperature">
        /// Softmax temperature. Lower → greedier (closer to <see cref="ChooseTarget"/>);
        /// higher → more exploratory. Default 0.25 keeps a strong warm-target bias.
        /// </param>
        /// <returns>The chosen target, or <see langword="null"/> if no eligible candidate exists.</returns>
        public static IHuman? ChooseTargetWeighted(
            IHuman initiator,
            IReadOnlyList<IHuman> candidates,
            SocialTargetMode mode,
            Random rng,
            double temperature = 0.25)
        {
            // Score every candidate; a blocked target always scores 0 (see ScoreTarget),
            // so the Score gate also covers PsychologicallyBlocked.
            var scored = new List<(IHuman Human, double Weight)>(candidates.Count);
            var totalWeight = 0.0;

            foreach (var candidate in candidates)
            {
                var result = ScoreTarget(initiator, candidate, mode);
                if (result.Score <= 0.0)
                    continue;

                // Softmax weight: temperature sharpens (low) or flattens (high) the score preference.
                var weight = Math.Exp(result.Score / temperature);
                scored.Add((candidate, weight));
                totalWeight += weight;
            }

            if (scored.Count == 0)
                return null;

            // Weighted roulette pick over the surviving candidates.
            var roll = rng.NextDouble() * totalWeight;
            foreach (var (human, weight) in scored)
            {
                roll -= weight;
                if (roll <= 0.0)
                    return human;
            }

            return scored[^1].Human;   // floating-point safety net
        }

        private static SocialTargetScore ScoreTarget(
            HumanId initiatorId,
            Sociosexuality sociosexuality,
            PsychologicalProfile profile,
            AttractionProfile? attractionProfile,
            RelationshipState relationships,
            MemoryIndex memoryIndex,
            SemanticMemoryState? semanticMemory,
            HumanId target,
            SocialTargetMode mode,
            SexBiology? targetBiology)
        {
            var act = mode switch
            {
                SocialTargetMode.Intimacy => SpeechAct.Invite,
                SocialTargetMode.Vulnerability => SpeechAct.SelfDisclosure,
                _ => SpeechAct.SmallTalk
            };

            var relationship = relationships.Edges.GetValueOrDefault(target);
            var effectiveTargetBiology = targetBiology ?? relationship?.TargetBiology;
            var memory = memoryIndex.Episodes;
            var expected = SemanticMemoryMath.ExpectedAcceptance(semanticMemory, target, act, relationship, profile, memory);
            var baseScore = SemanticMemoryMath.ScoreApproachTarget(semanticMemory, target, relationship, profile, memory, act);
            var safe = semanticMemory?.GetStrength(target, PersonBeliefKind.EmotionallySafe) ?? 0.0;
            var warm = semanticMemory?.GetStrength(target, PersonBeliefKind.Warm) ?? 0.0;
            var rejecting = semanticMemory?.GetStrength(target, PersonBeliefKind.Rejecting) ?? 0.0;
            var critical = semanticMemory?.GetStrength(target, PersonBeliefKind.Critical) ?? 0.0;
            var vulnerabilitySafety = Math.Clamp(expected * 0.55 + safe * 0.30 + warm * 0.15, 0.0, 1.0);
            var rejectionRisk = Math.Clamp((1.0 - expected) * 0.55 + rejecting * 0.30 + critical * 0.15, 0.0, 1.0);
            var blocked = IsPsychologicallyBlocked(mode, sociosexuality, profile, vulnerabilitySafety, rejectionRisk, relationship);
            var sociosexualityAdjustment = mode == SocialTargetMode.Intimacy
                ? SociosexualityBehaviorMath.IntimacyTargetScoreAdjustment(sociosexuality, relationship, vulnerabilitySafety, rejectionRisk, expected)
                : 0.0;
            var orientationMultiplier = mode == SocialTargetMode.Intimacy && effectiveTargetBiology is not null
                ? SexualOrientationBehaviorMath.IntimacyTargetScoreMultiplier(attractionProfile, effectiveTargetBiology)
                : 1.0;
            var score = blocked
                ? 0.0
                : Math.Clamp((baseScore + RecentSalience(memory, target) * 0.10 + sociosexualityAdjustment) * orientationMultiplier, 0.0, 1.0);
            var reason = blocked
                ? $"blocked:{mode}:risk={rejectionRisk:0.00}:safe={vulnerabilitySafety:0.00}"
                : $"mode={mode};expected={expected:0.00};safe={vulnerabilitySafety:0.00};risk={rejectionRisk:0.00};socio={sociosexuality};orientation={orientationMultiplier:0.00}";

            return new SocialTargetScore(target, score, expected, act, vulnerabilitySafety, rejectionRisk, blocked, reason);
        }

        private static bool IsPsychologicallyBlocked(
            SocialTargetMode mode,
            Sociosexuality sociosexuality,
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
                return SociosexualityBehaviorMath.BlocksIntimacy(sociosexuality, relationship, vulnerabilitySafety, rejectionRisk);
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
