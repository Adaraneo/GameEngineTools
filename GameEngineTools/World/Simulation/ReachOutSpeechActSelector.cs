// ReachOutSpeechActSelector.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Simulation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Selects a concrete <see cref="SpeechAct"/> for a generic ReachOut action.
    /// </summary>
    /// <remarks>
    /// The selector keeps early contact safe and gradually unlocks warmer,
    /// more vulnerable acts as familiarity, comfort, trust, and closeness grow.
    /// Invite remains rare and heavily gated.
    /// </remarks>
    public static class ReachOutSpeechActSelector
    {
        #region Public API

        /// <summary>
        /// Chooses a speech act for the initiating character toward the target.
        /// </summary>
        /// <param name="initiator">Character initiating the reach-out.</param>
        /// <param name="target">Target of the social approach.</param>
        /// <param name="now">Current simulation time, reserved for future context-sensitive tuning.</param>
        /// <param name="rng">Random source used for conservative weighted selection.</param>
        /// <returns>A decision containing the chosen act and the relationship context used.</returns>
        public static ReachOutSpeechActSelection SelectSpeechAct(
            IHuman initiator,
            IHuman target,
            WDateTime now,
            Random rng)
        {
            _ = now;

            var edge = initiator.Snapshot.Relationships.Edges.GetValueOrDefault(target.Id);
            return SelectSpeechAct(
                edge,
                initiator.Snapshot.InteractionSurface,
                rng,
                initiator.Snapshot.SemanticMemory,
                target.Id,
                initiator.PsychologyProfile,
                initiator.Snapshot.Memory.Episodes,
                initiator.AttractionProfile,
                target.Biology);
        }

        /// <summary>
        /// Chooses a speech act from a directional relationship edge and current interaction surface.
        /// </summary>
        /// <param name="edge">Directional relationship edge, or <c>null</c> for strangers.</param>
        /// <param name="surface">Current interaction surface of the initiator.</param>
        /// <param name="rng">Random source used for conservative weighted selection.</param>
        /// <returns>A decision containing the chosen act and the relationship context used.</returns>
        public static ReachOutSpeechActSelection SelectSpeechAct(
            RelationshipEdge? edge,
            InteractionSurface surface,
            Random rng)
            => SelectSpeechAct(edge, surface, rng, null, null, null, null);

        public static ReachOutSpeechActSelection SelectSpeechAct(
            RelationshipEdge? edge,
            InteractionSurface surface,
            Random rng,
            SemanticMemoryState? semanticMemory,
            HumanId? targetId,
            Characters.Traits.PsychologicalProfile? profile = null,
            IReadOnlyList<Characters.Engines.Memory.EpisodicMemory>? episodes = null,
            AttractionProfile? attractionProfile = null,
            SexBiology? targetBiology = null)
        {
            ArgumentNullException.ThrowIfNull(rng);

            var familiarity = edge?.Familiarity ?? 0.0;
            var trust = edge?.Trust ?? 50.0;
            var comfort = edge?.Comfort ?? 45.0;
            var closeness = edge?.Closeness ?? 0.0;
            var romanticInterest = edge?.RomanticInterest ?? 0.0;
            var expectedAcceptance = targetId is { } other
                ? SemanticMemoryMath.ExpectedAcceptance(semanticMemory, other, SpeechAct.SmallTalk, edge, profile, episodes)
                : 0.5;
            var warmBelief = targetId is { } warmOther && semanticMemory is not null
                ? semanticMemory.GetStrength(warmOther, PersonBeliefKind.Warm)
                : 0.0;
            var safeBelief = targetId is { } safeOther && semanticMemory is not null
                ? semanticMemory.GetStrength(safeOther, PersonBeliefKind.EmotionallySafe)
                : 0.0;
            var orientationMultiplier = SexualOrientationBehaviorMath.IntimacyTargetScoreMultiplier(attractionProfile, targetBiology);

            var weightedActs = new List<(SpeechAct Act, double Weight)>
            {
                (SpeechAct.SmallTalk, ComputeSmallTalkWeight(familiarity, comfort, closeness, expectedAcceptance))
            };

            weightedActs.Add((SpeechAct.Question, ComputeQuestionWeight(familiarity, comfort, closeness, expectedAcceptance)));

            if (familiarity >= 10 || comfort >= 48 || warmBelief >= 0.35)
            {
                weightedActs.Add((SpeechAct.Validation, ComputeValidationWeight(trust, comfort, closeness, warmBelief, safeBelief)));
            }

            if ((trust >= 50 && comfort >= 50 && closeness >= 6) || safeBelief >= 0.42)
            {
                weightedActs.Add((SpeechAct.SelfDisclosure, ComputeSelfDisclosureWeight(trust, comfort, closeness, safeBelief)));
            }

            if ((trust >= 52 && comfort >= 52 && closeness >= 8) || safeBelief >= 0.48)
            {
                weightedActs.Add((SpeechAct.Meta, ComputeMetaWeight(trust, comfort, closeness, safeBelief)));
            }

            if (CanInvite(surface, comfort, closeness, romanticInterest, orientationMultiplier) || expectedAcceptance >= 0.66)
            {
                weightedActs.Add((SpeechAct.Invite, ComputeInviteWeight(surface, comfort, closeness, romanticInterest, expectedAcceptance) * orientationMultiplier));
            }

            var chosen = PickWeightedRandom(weightedActs, rng);

            return new ReachOutSpeechActSelection(
                chosen,
                familiarity,
                trust,
                comfort,
                closeness,
                romanticInterest,
                surface.HasPrivacy);
        }

        #endregion Public API

        #region Private helpers

        private static double ComputeSmallTalkWeight(double familiarity, double comfort, double closeness, double expectedAcceptance)
        {
            if (familiarity < 10 && comfort < 48)
            {
                return 1.45 + Math.Max(0.0, 0.5 - expectedAcceptance) * 0.8;
            }

            if (closeness < 8)
            {
                return 1.10;
            }

            if (closeness < 14)
            {
                return 0.90;
            }

            return 0.75;
        }

        private static double ComputeQuestionWeight(double familiarity, double comfort, double closeness, double expectedAcceptance)
            => 0.28
                + Math.Max(0.0, familiarity - 8.0) * 0.018
                + Math.Max(0.0, comfort - 47.0) * 0.010
                + Math.Max(0.0, closeness - 6.0) * 0.006
                + Math.Max(0.0, expectedAcceptance - 0.5) * 0.12;

        private static double ComputeValidationWeight(double trust, double comfort, double closeness, double warmBelief, double safeBelief)
            => 0.06
                + Math.Max(0.0, comfort - 48.0) * 0.018
                + Math.Max(0.0, trust - 50.0) * 0.012
                + Math.Max(0.0, closeness - 6.0) * 0.006
                + warmBelief * 0.18
                + safeBelief * 0.10;

        private static double ComputeSelfDisclosureWeight(double trust, double comfort, double closeness, double safeBelief)
            => 0.03
                + Math.Max(0.0, trust - 50.0) * 0.018
                + Math.Max(0.0, comfort - 50.0) * 0.015
                + Math.Max(0.0, closeness - 6.0) * 0.010
                + safeBelief * 0.20;

        private static double ComputeMetaWeight(double trust, double comfort, double closeness, double safeBelief)
            => 0.02
                + Math.Max(0.0, trust - 52.0) * 0.015
                + Math.Max(0.0, comfort - 52.0) * 0.012
                + Math.Max(0.0, closeness - 8.0) * 0.010
                + safeBelief * 0.16;

        private static bool CanInvite(
            InteractionSurface surface,
            double comfort,
            double closeness,
            double romanticInterest,
            double orientationMultiplier)
            => comfort >= 55
                && closeness >= 12
                && romanticInterest * orientationMultiplier >= 10
                && (surface.HasPrivacy || surface.Kind is SurfaceKind.Social or SurfaceKind.Private);

        private static double ComputeInviteWeight(
            InteractionSurface surface,
            double comfort,
            double closeness,
            double romanticInterest,
            double expectedAcceptance)
        {
            var privacyBonus = surface.HasPrivacy ? 0.08 : 0.0;

            return 0.008
                + privacyBonus
                + Math.Max(0.0, romanticInterest - 10.0) * 0.004
                + Math.Max(0.0, comfort - 55.0) * 0.003
                + Math.Max(0.0, closeness - 12.0) * 0.002
                + Math.Max(0.0, expectedAcceptance - 0.5) * 0.14;
        }

        private static SpeechAct PickWeightedRandom(IReadOnlyList<(SpeechAct Act, double Weight)> weightedActs, Random rng)
        {
            var filtered = weightedActs.Where(a => a.Weight > 0.0).ToList();
            if (filtered.Count == 0)
            {
                return SpeechAct.SmallTalk;
            }

            var totalWeight = filtered.Sum(a => a.Weight);
            var threshold = rng.NextDouble() * totalWeight;
            var accumulated = 0.0;

            foreach (var candidate in filtered)
            {
                accumulated += candidate.Weight;
                if (accumulated >= threshold)
                {
                    return candidate.Act;
                }
            }

            return filtered[^1].Act;
        }

        #endregion Private helpers
    }

    /// <summary>
    /// Captures the selected speech act together with the relationship context used to choose it.
    /// </summary>
    public sealed record ReachOutSpeechActSelection(
        SpeechAct Act,
        double Familiarity,
        double Trust,
        double Comfort,
        double Closeness,
        double RomanticInterest,
        bool HasPrivacy);
}
