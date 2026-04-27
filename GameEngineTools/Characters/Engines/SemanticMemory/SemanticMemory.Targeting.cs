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
    /// Záměr sociálního přístupu — určuje, jaký SpeechAct se použije a jak přísné
    /// jsou podmínky psychologického blokování.
    /// </summary>
    public enum SocialTargetMode
    {
        /// <summary>Nezávazný sociální kontakt (SmallTalk). Nikdy není psychologicky blokován.</summary>
        ReachOut,
        /// <summary>Otevření se vulnerabilitě (SelfDisclosure). Blokováno při nízkém EmotionallySafe.</summary>
        Vulnerability,
        /// <summary>Intimní přístupu (Invite). Blokováno sociosexualitou a orientací.</summary>
        Intimacy
    }

    /// <summary>
    /// Výsledek ohodnocení jednoho kandidáta pro sociální přístupu.
    /// Obsahuje skóre, predikci přijetí a diagnostická pole.
    /// </summary>
    public sealed record SocialTargetScore(
        HumanId Target,
        /// <summary>Celkové skóre vhodnosti cíle [0.0–1.0]. 0.0 pokud je psychologicky blokován.</summary>
        double Score,
        /// <summary>Predikovaná pravděpodobnost přijetí přístupu [0.05–0.95].</summary>
        double ExpectedAcceptance,
        /// <summary>SpeechAct použitý při hodnocení.</summary>
        SpeechAct EvaluatedAct,
        /// <summary>Míra bezpečnosti pro vulnerability [0.0–1.0].</summary>
        double VulnerabilitySafety,
        /// <summary>Odhadované riziko odmítnutí [0.0–1.0].</summary>
        double RejectionRisk,
        bool PsychologicallyBlocked = false,
        string? Reason = null);

    /// <summary>
    /// Statický scoring subsystém pro výběr a seřazení sociálních cílů
    /// na základě sémantické paměti, vztahů a psychologického profilu.
    /// </summary>
    public static class SemanticTargeting
    {
        /// <summary>
        /// Ohodnotí jednoho kandidáta z pohledu initiator → target.
        /// </summary>
        public static SocialTargetScore ScoreTarget(
            IHuman initiator,
            IHuman target,
            SocialTargetMode mode)
            => ScoreTarget(initiator.Id, initiator.Personality.Sociosexuality, initiator.PsychologyProfile, initiator.AttractionProfile, initiator.Snapshot.Relationships, initiator.Snapshot.Memory, initiator.Snapshot.SemanticMemory, target.Id, mode, target.Biology);

        /// <summary>
        /// Ohodnotí jednoho kandidáta z pohledu initiator (context) → target (id).
        /// </summary>
        public static SocialTargetScore ScoreTarget(
            IHumanContext initiator,
            HumanId target,
            SocialTargetMode mode)
            => ScoreTarget(initiator.Id, initiator.Personality.Sociosexuality, initiator.PsychologyProfile, initiator.AttractionProfile, initiator.Snapshot.Relationships, initiator.Snapshot.Memory, initiator.Snapshot.SemanticMemory, target, mode, null);

        /// <summary>
        /// Seřadí kandidáty dle skóre sestupně. Vrací nejvýše <paramref name="take"/> výsledků.
        /// Deterministické — tie-break podle <see cref="HumanId.Value"/>.
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
        /// Vybere nejlepšího kandidáta dle skóre. Vrátí <see langword="null"/> pokud je seznam prázdný.
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
