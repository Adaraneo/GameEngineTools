// SemanticMemory.Math.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Matematické funkce sémantické paměti — predikce přijetí, scoring cílů a bias výpočty.
    /// </summary>
    public static class SemanticMemoryMath
    {
        /// <summary>
        /// Predikuje pravděpodobnost přijetí sociálního přístupu na základě beliefs.
        /// Zkrácený overload bez vztahového a psychologického kontextu.
        /// Vrátí hodnotu v [0.05, 0.95].
        /// </summary>
        public static double ExpectedAcceptance(
            SemanticMemoryState? state,
            HumanId other,
            SpeechAct act)
            => ExpectedAcceptance(state, other, act, null, null, null);

        /// <summary>
        /// Plná predikce přijetí — zahrnuje beliefs, vztahové metriky, psychologický profil
        /// a trend posledních epizod. Vrátí hodnotu v [0.05, 0.95].
        /// </summary>
        public static double ExpectedAcceptance(
            SemanticMemoryState? state,
            HumanId other,
            SpeechAct act,
            RelationshipEdge? relationship,
            PsychologicalProfile? profile,
            IReadOnlyList<EpisodicMemory>? episodes)
        {
            var warm = state?.GetStrength(other, PersonBeliefKind.Warm) ?? 0.0;
            var safe = state?.GetStrength(other, PersonBeliefKind.EmotionallySafe) ?? 0.0;
            var reliable = state?.GetStrength(other, PersonBeliefKind.Reliable) ?? 0.0;
            var rejecting = state?.GetStrength(other, PersonBeliefKind.Rejecting) ?? 0.0;
            var critical = state?.GetStrength(other, PersonBeliefKind.Critical) ?? 0.0;

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
            var relationshipBias = RelationshipBias(relationship, act);
            var recentBias = RecentEpisodeBias(episodes, other, act);
            var trendBias = PositiveTrendBias(relationship, episodes, other, act, profile);
            var profileBias = ProfileBias(profile, act, safe, rejecting, critical);
            return Math.Clamp(0.5 + positive - negative + relationshipBias + recentBias + trendBias + profileBias, 0.05, 0.95);
        }

        internal static double ScoreApproachTarget(
            SemanticMemoryState? state,
            HumanId other,
            RelationshipEdge? relationship,
            PsychologicalProfile? profile,
            IReadOnlyList<EpisodicMemory>? episodes,
            SpeechAct act)
        {
            var expected = ExpectedAcceptance(state, other, act, relationship, profile, episodes);
            var familiarity = (relationship?.Familiarity ?? 0.0) / 100.0;
            var closeness = (relationship?.Closeness ?? 0.0) / 100.0;
            var trust = (relationship?.Trust ?? 30.0) / 100.0;

            return Math.Clamp(
                expected * 0.60
                + familiarity * 0.12
                + closeness * 0.16
                + trust * 0.12,
                0.0,
                1.0);
        }

        private static double RelationshipBias(RelationshipEdge? relationship, SpeechAct act)
        {
            if (relationship is null)
            {
                return 0.0;
            }

            var trust = (relationship.Trust - 50.0) / 100.0;
            var comfort = (relationship.Comfort - 50.0) / 100.0;
            var closeness = relationship.Closeness / 100.0;

            return act switch
            {
                SpeechAct.SelfDisclosure or SpeechAct.Meta
                    => trust * 0.12 + comfort * 0.08 + closeness * 0.06,
                SpeechAct.Invite
                    => trust * 0.08 + comfort * 0.06 + closeness * 0.10,
                _
                    => trust * 0.05 + comfort * 0.04
            };
        }

        private static double RecentEpisodeBias(
            IReadOnlyList<EpisodicMemory>? episodes,
            HumanId other,
            SpeechAct act)
        {
            if (episodes is null || episodes.Count == 0)
            {
                return 0.0;
            }

            var recent = episodes
                .Where(e => e.OtherPerson == other && e.Strength > 0.35)
                .OrderByDescending(e => e.When)
                .Take(3)
                .ToList();

            if (recent.Count == 0)
            {
                return 0.0;
            }

            var positive = recent.Sum(e => e.Emotion == EmotionalTag.Positive ? e.Strength : 0.0);
            var negative = recent.Sum(e => e.Emotion == EmotionalTag.Negative ? e.Strength : 0.0);
            var threatPenalty = recent.Count(e => (e.PerceivedWhat ?? e.What).StartsWith("PerceivedThreat:", StringComparison.Ordinal)) * 0.04;
            var vulnerabilityMultiplier = act is SpeechAct.SelfDisclosure or SpeechAct.Meta or SpeechAct.Invite ? 1.25 : 0.85;

            return Math.Clamp((positive - negative) * 0.06 * vulnerabilityMultiplier - threatPenalty, -0.12, 0.12);
        }

        private static double PositiveTrendBias(
            RelationshipEdge? relationship,
            IReadOnlyList<EpisodicMemory>? episodes,
            HumanId other,
            SpeechAct act,
            PsychologicalProfile? profile)
        {
            var exposureBias = RelationshipExposureBias(relationship);
            var memoryTrend = MemoryTrendBias(episodes, other, act, profile);

            return Math.Clamp(exposureBias + memoryTrend, -0.10, 0.14);
        }

        private static double RelationshipExposureBias(RelationshipEdge? relationship)
        {
            if (relationship is null || relationship.PositiveInteractionCount <= 0)
            {
                return 0.0;
            }

            var exposure = Math.Clamp(Math.Log(1.0 + relationship.PositiveInteractionCount) / Math.Log(21.0), 0.0, 1.0);
            var safety = Math.Clamp(
                Math.Max(0.0, relationship.Trust - 48.0) / 52.0 * 0.35
                + Math.Max(0.0, relationship.Comfort - 45.0) / 55.0 * 0.40
                + relationship.Closeness / 100.0 * 0.25,
                0.0,
                1.0);

            return exposure * (0.02 + safety * 0.06);
        }

        private static double MemoryTrendBias(
            IReadOnlyList<EpisodicMemory>? episodes,
            HumanId other,
            SpeechAct act,
            PsychologicalProfile? profile)
        {
            if (episodes is null || episodes.Count == 0)
            {
                return 0.0;
            }

            var recent = episodes
                .Where(e => e.OtherPerson == other)
                .OrderByDescending(e => e.When)
                .Take(6)
                .ToList();

            if (recent.Count == 0)
            {
                return 0.0;
            }

            var weighted = 0.0;
            var total = 0.0;
            for (var i = 0; i < recent.Count; i++)
            {
                var episode = recent[i];
                var recency = 1.0 / (1.0 + i * 0.55);
                var strength = Math.Clamp(episode.Strength, 0.0, 1.0);
                var polarity = episode.Emotion switch
                {
                    EmotionalTag.Positive => 1.0,
                    EmotionalTag.Mixed => -0.35,
                    EmotionalTag.Negative => -1.0,
                    _ => 0.0
                };

                weighted += polarity * strength * recency;
                total += strength * recency;
            }

            if (total <= 0.0)
            {
                return 0.0;
            }

            var trend = Math.Clamp(weighted / total, -1.0, 1.0);
            var vulnerableAct = act is SpeechAct.SelfDisclosure or SpeechAct.Meta or SpeechAct.Invite;
            var ambivalence = Math.Clamp(profile?.Ambivalence ?? PsychologicalProfile.Default.Ambivalence, 0.0, 1.0);
            var positiveScale = vulnerableAct ? 0.08 + ambivalence * 0.04 : 0.06 + ambivalence * 0.025;
            var negativeScale = vulnerableAct ? 0.08 + ambivalence * 0.035 : 0.06 + ambivalence * 0.02;

            return trend >= 0.0
                ? trend * positiveScale
                : trend * negativeScale;
        }

        private static double ProfileBias(
            PsychologicalProfile? profile,
            SpeechAct act,
            double safe,
            double rejecting,
            double critical)
        {
            if (profile is null)
            {
                return 0.0;
            }

            var vulnerableAct = act is SpeechAct.SelfDisclosure or SpeechAct.Meta or SpeechAct.Invite;
            var selfProtective = profile.Coping is CopingStyle.Avoidant or CopingStyle.AggressiveCompensation
                ? 0.05 + profile.Narrative.ToughnessIdentity * 0.08
                : 0.0;
            var affiliative = profile.Coping == CopingStyle.PeoplePleasing
                ? 0.03 + profile.Narrative.BelongingIdentity * 0.05
                : profile.Narrative.BelongingIdentity * 0.02;

            if (!vulnerableAct)
            {
                return affiliative * 0.5 - Math.Max(0.0, rejecting - safe) * selfProtective * 0.3;
            }

            var guardedPenalty = (Math.Max(0.0, rejecting - safe) + critical * 0.5) * selfProtective;
            var approachBonus = safe * affiliative * 0.35;
            return Math.Clamp(approachBonus - guardedPenalty, -0.15, 0.08);
        }
    }
}
