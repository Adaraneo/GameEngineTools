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
            return SelectSpeechAct(edge, initiator.Snapshot.InteractionSurface, rng);
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
        {
            ArgumentNullException.ThrowIfNull(rng);

            var familiarity = edge?.Familiarity ?? 0.0;
            var trust = edge?.Trust ?? 50.0;
            var comfort = edge?.Comfort ?? 45.0;
            var closeness = edge?.Closeness ?? 0.0;
            var romanticInterest = edge?.RomanticInterest ?? 0.0;

            var weightedActs = new List<(SpeechAct Act, double Weight)>
            {
                (SpeechAct.SmallTalk, ComputeSmallTalkWeight(familiarity, comfort, closeness))
            };

            if (familiarity >= 8 || comfort >= 47)
            {
                weightedActs.Add((SpeechAct.Question, ComputeQuestionWeight(familiarity, comfort, closeness)));
            }

            if (familiarity >= 12 || comfort >= 50)
            {
                weightedActs.Add((SpeechAct.Validation, ComputeValidationWeight(trust, comfort, closeness)));
            }

            if (trust >= 52 && comfort >= 50 && closeness >= 15)
            {
                weightedActs.Add((SpeechAct.SelfDisclosure, ComputeSelfDisclosureWeight(trust, comfort, closeness)));
            }

            if (trust >= 56 && comfort >= 53 && closeness >= 22)
            {
                weightedActs.Add((SpeechAct.Meta, ComputeMetaWeight(trust, comfort, closeness)));
            }

            if (CanInvite(surface, comfort, closeness, romanticInterest))
            {
                weightedActs.Add((SpeechAct.Invite, ComputeInviteWeight(surface, comfort, closeness, romanticInterest)));
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

        private static double ComputeSmallTalkWeight(double familiarity, double comfort, double closeness)
        {
            if (familiarity < 8 && comfort < 47)
            {
                return 1.35;
            }

            if (closeness < 20)
            {
                return 1.05;
            }

            if (closeness < 40)
            {
                return 0.85;
            }

            return 0.65;
        }

        private static double ComputeQuestionWeight(double familiarity, double comfort, double closeness)
            => 0.18
                + Math.Max(0.0, familiarity - 8.0) * 0.020
                + Math.Max(0.0, comfort - 47.0) * 0.010
                + Math.Max(0.0, closeness - 10.0) * 0.004;

        private static double ComputeValidationWeight(double trust, double comfort, double closeness)
            => 0.06
                + Math.Max(0.0, comfort - 50.0) * 0.018
                + Math.Max(0.0, trust - 50.0) * 0.012
                + Math.Max(0.0, closeness - 12.0) * 0.005;

        private static double ComputeSelfDisclosureWeight(double trust, double comfort, double closeness)
            => 0.03
                + Math.Max(0.0, trust - 52.0) * 0.020
                + Math.Max(0.0, comfort - 50.0) * 0.015
                + Math.Max(0.0, closeness - 15.0) * 0.008;

        private static double ComputeMetaWeight(double trust, double comfort, double closeness)
            => 0.02
                + Math.Max(0.0, trust - 56.0) * 0.016
                + Math.Max(0.0, comfort - 53.0) * 0.012
                + Math.Max(0.0, closeness - 22.0) * 0.008;

        private static bool CanInvite(
            InteractionSurface surface,
            double comfort,
            double closeness,
            double romanticInterest)
            => comfort >= 58
                && closeness >= 35
                && romanticInterest >= 30
                && (surface.HasPrivacy || surface.Kind is SurfaceKind.Social or SurfaceKind.Private);

        private static double ComputeInviteWeight(
            InteractionSurface surface,
            double comfort,
            double closeness,
            double romanticInterest)
        {
            var privacyBonus = surface.HasPrivacy ? 0.08 : 0.0;

            return 0.008
                + privacyBonus
                + Math.Max(0.0, romanticInterest - 30.0) * 0.006
                + Math.Max(0.0, comfort - 58.0) * 0.004
                + Math.Max(0.0, closeness - 35.0) * 0.003;
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
