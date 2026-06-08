// EnvironmentalAffordanceEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using GameEngineTools.Characters.Engines.Interactions;
    using static ActionNames;

    /// <summary>
    /// Centralizes how the current surface, noise, and crowding shape candidate utility.
    /// </summary>
    internal sealed class EnvironmentalAffordanceEngine : IBehaviorModifierEngine
    {
        #region IBehaviorModifierEngine

        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var surface = context.HumanContext.Snapshot.InteractionSurface;
            var kind = surface.Kind;
            var noise = double.IsNaN(surface.Noise) ? 0.5 : surface.Noise;
            var crowding = double.IsNaN(surface.Crowding) ? 0.5 : surface.Crowding;
            var productiveMult = BehaviorMath.ProductiveSurfaceMultiplier(kind);
            var privateMult = BehaviorMath.PrivateSurfaceMultiplier(kind);

            // First apply the local multipliers for actions that depend directly on the current surface.
            BehaviorCandidateEditor.Multiply(candidates, Work, productiveMult);
            BehaviorCandidateEditor.Multiply(candidates, Create, productiveMult);
            BehaviorCandidateEditor.Multiply(candidates, SelfCare, privateMult);
            BehaviorCandidateEditor.Multiply(candidates, InviteIntimacy, privateMult);

            // Then derive movement pressure from the value lost by staying where the character is.
            var rawWork = BehaviorMath.Util(context.State.NeedCompetence, context.HumanContext.Personality.Motivation.Competence);
            var rawCreate = BehaviorMath.Util(context.State.NeedCompetence, context.HumanContext.Personality.Motivation.Curiosity);
            var workHere = rawWork * productiveMult;
            var createHere = rawCreate * productiveMult;
            var productiveLoss = Math.Max(0.0, Math.Max(rawWork - workHere, rawCreate - createHere));
            BehaviorCandidateEditor.Add(candidates, MoveToWork, kind != SurfaceKind.Work ? productiveLoss * 0.80 : 0.0);

            // Noise cognitive penalty: working memory degradation (Glass & Singer 1972).
            // Noise > 0.55 starts degrading complex cognitive tasks (WHO threshold mapping).
            var noiseCognitivePenalty = noise > 0.55
                ? (noise - 0.55) / (1.0 - 0.55) * context.Config.NoiseCognitivePenaltyMax
                : 0.0;

            if (noiseCognitivePenalty > 0.0)
            {
                BehaviorCandidateEditor.Multiply(candidates, Work, 1.0 - noiseCognitivePenalty);
                BehaviorCandidateEditor.Multiply(candidates, Create, 1.0 - noiseCognitivePenalty);
            }

            // Noise and crowding create low-stakes displacement pressure toward more suitable spaces.
            var noiseStress = Math.Max(0, noise - 0.5) * 2.0 * (context.HumanContext.Snapshot.Psychology.Stress / 100.0) * 20.0;
            var socialPull = context.State.NeedBelonging * context.HumanContext.Personality.Motivation.Affiliation * BehaviorMath.SocialSurfaceMultiplier(kind) * (1.0 - crowding) * 0.5;
            var restLoss = Math.Max(0, context.State.NeedRest * (1 - BehaviorMath.RestSurfaceMultiplier(kind)));

            BehaviorCandidateEditor.Add(candidates, MoveToSocial, socialPull);
            BehaviorCandidateEditor.Add(candidates, MoveToPrivate, noiseStress);
            BehaviorCandidateEditor.Add(candidates, MoveToRest, restLoss * 0.75 + noiseStress * 0.5 - context.State.NeedRest);

            // Seasonal and light modulation — summer sun pulls outdoors, winter/darkness pushes indoors
            ApplySeasonalAffordance(context, candidates);
        }

        /// <summary>
        /// Modulates candidate utilities based on the astronomical context (season, irradiance).
        /// Summer noon → bonus for moving to social and public spaces.
        /// Winter darkness → increased motivation for rest and privacy.
        /// </summary>
        private static void ApplySeasonalAffordance(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var celestial = context.HumanContext.Snapshot.Celestial;
            if (celestial is null)
                return;

            // Summer bonus: linear peak at SeasonFraction=0.25 (summer solstice), 0 in winter (0.75)
            // Combined with current irradiance (no effect without daylight)
            var summerFactor = Math.Max(0.0, 1.0 - Math.Abs(celestial.SeasonFraction - 0.25) * 4.0);
            var outdoorBonus = celestial.IrradianceFactor * summerFactor * 8.0;

            BehaviorCandidateEditor.Add(candidates, MoveToSocial, outdoorBonus * 0.6);
            BehaviorCandidateEditor.Add(candidates, MoveToPublic, outdoorBonus * 0.4);

            // Darkness pressure: night time × short day (winter nights are strongest)
            var hoursPerDay = context.HumanContext.Snapshot.Celestial?.DaylightHours is { } dl
                ? dl : 12.0;
            var shortDayFactor = Math.Max(0.0, 0.5 - hoursPerDay / 48.0); // max at 0 h, 0 at 24 h
            var darknessPressure = (1.0 - celestial.IrradianceFactor) * shortDayFactor * 10.0;

            BehaviorCandidateEditor.Add(candidates, MoveToRest, darknessPressure * 0.5);
            BehaviorCandidateEditor.Add(candidates, MoveToPrivate, darknessPressure * 0.3);
        }

        #endregion IBehaviorModifierEngine
    }
}
