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
                (SpeechAct.SmallTalk, 1.0),
                (SpeechAct.Question, familiarity < 20 ? 0.35 : 0.85)
            };

            if (familiarity >= 20 || comfort >= 55)
            {
                weightedActs.Add((SpeechAct.Validation, ComputeValidationWeight(trust, comfort, closeness)));
            }

            if (trust >= 60 && comfort >= 58 && closeness >= 35)
            {
                weightedActs.Add((SpeechAct.SelfDisclosure, ComputeSelfDisclosureWeight(trust, comfort, closeness)));
            }

            if (trust >= 65 && comfort >= 62 && closeness >= 45)
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

        private static double ComputeValidationWeight(double trust, double comfort, double closeness)
            => 0.10
                + Math.Max(0.0, comfort - 55.0) * 0.015
                + Math.Max(0.0, trust - 55.0) * 0.010
                + Math.Max(0.0, closeness - 25.0) * 0.004;

        private static double ComputeSelfDisclosureWeight(double trust, double comfort, double closeness)
            => 0.08
                + Math.Max(0.0, trust - 60.0) * 0.018
                + Math.Max(0.0, comfort - 58.0) * 0.012
                + Math.Max(0.0, closeness - 35.0) * 0.006;

        private static double ComputeMetaWeight(double trust, double comfort, double closeness)
            => 0.04
                + Math.Max(0.0, trust - 65.0) * 0.012
                + Math.Max(0.0, comfort - 62.0) * 0.010
                + Math.Max(0.0, closeness - 45.0) * 0.006;

        private static bool CanInvite(
            InteractionSurface surface,
            double comfort,
            double closeness,
            double romanticInterest)
            => comfort >= 68
                && closeness >= 60
                && romanticInterest >= 65
                && (surface.HasPrivacy || surface.Kind is SurfaceKind.Social or SurfaceKind.Private);

        private static double ComputeInviteWeight(
            InteractionSurface surface,
            double comfort,
            double closeness,
            double romanticInterest)
        {
            var privacyBonus = surface.HasPrivacy ? 0.10 : 0.0;

            return 0.02
                + privacyBonus
                + Math.Max(0.0, romanticInterest - 65.0) * 0.010
                + Math.Max(0.0, comfort - 68.0) * 0.005
                + Math.Max(0.0, closeness - 60.0) * 0.004;
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
