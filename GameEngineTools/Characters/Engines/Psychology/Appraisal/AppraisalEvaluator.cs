// AppraisalEvaluator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology.Appraisal
{
    using System;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Goals;

    /// <summary>
    /// Pure, stateless mapper from an incoming <see cref="IDomainEvent"/> + character context to an
    /// <see cref="AppraisalOutcome"/> (Scherer CPM stimulus-evaluation checks). Returns <c>null</c>
    /// for events that have no appraisal interpretation, so the caller can fall back to its legacy
    /// emotion logic.
    /// </summary>
    /// <remarks>
    /// This is deliberately a small, curated set of <i>currently un-appraised</i> events (goal-domain
    /// events). It does not steal events that already have bespoke affect handlers in
    /// <c>DefaultPsychologyEngine</c>, so wiring it in is additive and lossless. Source: Scherer (2001)
    /// Component Process Model; Roseman (1996) appraisal-emotion structure.
    /// </remarks>
    public static class AppraisalEvaluator
    {
        /// <summary>
        /// Evaluates the CPM checks for <paramref name="event"/>. Returns <c>null</c> if the event is
        /// not appraisable (the caller should then use its fallback emotion logic).
        /// </summary>
        /// <param name="event">The incoming domain event.</param>
        /// <param name="ctx">Character context (goals, values, relationships, personality).</param>
        /// <param name="current">The character's current psychology state (for coping potential).</param>
        /// <returns>An <see cref="AppraisalOutcome"/> or <c>null</c>.</returns>
        public static AppraisalOutcome? TryEvaluate(IDomainEvent @event, IHumanContext ctx, PsychologyState current)
        {
            ArgumentNullException.ThrowIfNull(@event);
            ArgumentNullException.ThrowIfNull(ctx);

            return @event switch
            {
                GoalResolved gr when GoalBelongsToSelf(gr.Human, ctx) => AppraiseGoalResolved(gr, ctx, current),
                GoalProgressed gp when GoalBelongsToSelf(gp.Human, ctx) => AppraiseGoalProgressed(gp, current),
                GoalActivated ga when GoalBelongsToSelf(ga.Human, ctx) => AppraiseGoalActivated(ga, current),
                _ => null
            };
        }

        #region Coping potential

        /// <summary>
        /// Estimates coping potential [0..1] from the current psychology state: high stress and low
        /// dominance reduce perceived ability to cope (Lazarus 1991; Scherer 2001).
        /// </summary>
        private static double CopingFrom(PsychologyState s)
        {
            // Dominance is the felt sense of control; stress erodes it.
            var coping = 0.35 + 0.5 * s.Dominance - 0.3 * (s.Stress / 100.0);
            return Math.Clamp(coping, 0.0, 1.0);
        }

        #endregion

        #region Goal appraisals

        private static bool GoalBelongsToSelf(HumanId human, IHumanContext ctx) => human == ctx.Id;

        private static AppraisalOutcome AppraiseGoalResolved(GoalResolved gr, IHumanContext ctx, PsychologyState current)
        {
            var goal = ctx.Snapshot.Goals?.Goals.FirstOrDefault(g => g.Id == gr.GoalId);
            var hadTarget = goal?.TargetHuman is not null;
            var coping = CopingFrom(current);

            switch (gr.Resolution)
            {
                case GoalResolution.Completed:
                    // Goal-conducive in the extreme + self-caused → pride/joy (Roseman 1996).
                    return new AppraisalOutcome(
                        Relevance: 0.9,
                        Novelty: 0.3,
                        IntrinsicPleasantness: 0.6,
                        GoalConduciveness: 1.0,
                        Agency: AppraisalAgency.Self,
                        Certainty: 0.95,
                        CopingPotential: Math.Max(coping, 0.7),
                        NormCompatibility: 0.4);

                case GoalResolution.Abandoned:
                    // Strong obstruction. Relational goals with a target attribute blame to the other
                    // (→ anger); impersonal abandonment is attributed to circumstances (→ sadness).
                    var blockedByOther = hadTarget &&
                        (gr.Kind == PersistentGoalKind.SeekRevenge ||
                         gr.Kind == PersistentGoalKind.RepairRelationship ||
                         gr.Kind == PersistentGoalKind.FindPartner ||
                         gr.Kind == PersistentGoalKind.ProtectFamily);
                    return new AppraisalOutcome(
                        Relevance: 0.85,
                        Novelty: 0.2,
                        IntrinsicPleasantness: -0.4,
                        GoalConduciveness: -1.0,
                        Agency: blockedByOther ? AppraisalAgency.Other : AppraisalAgency.Circumstance,
                        Certainty: 0.8,
                        CopingPotential: coping,
                        NormCompatibility: 0.0);

                case GoalResolution.Faded:
                case GoalResolution.Displaced:
                default:
                    // Low-intensity disengagement — barely registers emotionally.
                    return new AppraisalOutcome(
                        Relevance: 0.25,
                        Novelty: 0.1,
                        IntrinsicPleasantness: -0.1,
                        GoalConduciveness: -0.25,
                        Agency: AppraisalAgency.Circumstance,
                        Certainty: 0.7,
                        CopingPotential: coping,
                        NormCompatibility: 0.0);
            }
        }

        private static AppraisalOutcome AppraiseGoalProgressed(GoalProgressed gp, PsychologyState current)
        {
            var progressDelta = gp.NewProgress - gp.OldProgress;
            var conducive = Math.Clamp(progressDelta * 4.0, -1.0, 1.0); // ±0.25 progress ⇒ full magnitude
            var coping = CopingFrom(current);
            return new AppraisalOutcome(
                Relevance: Math.Clamp(0.3 + Math.Abs(conducive) * 0.5, 0.0, 1.0),
                Novelty: 0.15,
                IntrinsicPleasantness: conducive * 0.4,
                GoalConduciveness: conducive,
                Agency: AppraisalAgency.Self,
                Certainty: 0.85,
                CopingPotential: coping,
                NormCompatibility: 0.1);
        }

        private static AppraisalOutcome AppraiseGoalActivated(GoalActivated ga, PsychologyState current)
        {
            // A newly salient goal is novel and mildly relevant but not yet conducive/obstructive.
            return new AppraisalOutcome(
                Relevance: Math.Clamp(0.2 + ga.InitialSalience * 0.4, 0.0, 1.0),
                Novelty: 0.6,
                IntrinsicPleasantness: 0.0,
                GoalConduciveness: 0.1,
                Agency: AppraisalAgency.Self,
                Certainty: 0.5,
                CopingPotential: CopingFrom(current),
                NormCompatibility: 0.1);
        }

        #endregion
    }
}
