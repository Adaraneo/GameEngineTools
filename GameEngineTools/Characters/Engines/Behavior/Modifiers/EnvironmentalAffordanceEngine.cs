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

            // Noise and crowding create low-stakes displacement pressure toward more suitable spaces.
            var noiseStress = Math.Max(0, noise - 0.5) * 2.0 * (context.HumanContext.Snapshot.Psychology.Stress / 100.0) * 20.0;
            var socialPull = context.State.NeedBelonging * context.HumanContext.Personality.Motivation.Affiliation * BehaviorMath.SocialSurfaceMultiplier(kind) * (1.0 - crowding) * 0.5;
            var restLoss = Math.Max(0, context.State.NeedRest * (1 - BehaviorMath.RestSurfaceMultiplier(kind)));

            BehaviorCandidateEditor.Add(candidates, MoveToSocial, socialPull);
            BehaviorCandidateEditor.Add(candidates, MoveToPrivate, noiseStress);
            BehaviorCandidateEditor.Add(candidates, MoveToRest, restLoss * 0.75 + noiseStress * 0.5 - context.State.NeedRest);
        }

        #endregion IBehaviorModifierEngine
    }
}
