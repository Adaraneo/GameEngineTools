// DefaultNeedAppraisalEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.NeedAppraisal
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Goals;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Derives the SDT need-appraisal layer each tick. This is a <b>read-only derivation</b> over
    /// existing goal/relationship/regulatory-focus state, not an <see cref="IBehaviorModifierEngine"/> —
    /// it does not touch any <see cref="BehaviorCandidate.Utility"/>. Its output feeds future
    /// well-being/ill-being / diagnostic outputs (MVP: stored in <c>EnginesSnapshot.NeedAppraisal</c>).
    /// </summary>
    public interface INeedAppraisalEngine
    {
        /// <summary>The most recently derived appraisal (starts at <see cref="NeedAppraisalState.Empty"/>).</summary>
        NeedAppraisalState State { get; }

        /// <summary>Recomputes the appraisal from the character's current snapshot.</summary>
        void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx);
    }

    /// <summary>
    /// Default SDT need-appraisal derivation. Competence reads goal progress/frustration, Relatedness
    /// reads relationship warmth (excluding status), Autonomy reads goal-origin volition with a weak
    /// regulatory-focus covariate.
    /// </summary>
    internal sealed class DefaultNeedAppraisalEngine : INeedAppraisalEngine
    {
        /// <inheritdoc/>
        public NeedAppraisalState State { get; private set; } = NeedAppraisalState.Empty;

        /// <inheritdoc/>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx)
        {
            State = new NeedAppraisalState(
                ComputeCompetence(ctx),
                ComputeRelatedness(ctx),
                ComputeAutonomy(ctx));
        }

        /// <summary>
        /// Competence ≈ goal-progress signal (Carver &amp; Scheier 1998) with a frustration discount.
        /// Reads <see cref="GoalState.Active"/>; positive competence feedback raises satisfaction even
        /// without goal attainment (Lintunen et al. 2025).
        /// </summary>
        /// <remarks>
        /// MVP uses a progress-minus-frustration signal as a velocity proxy. True progress <i>velocity</i>
        /// (via consumed <see cref="GoalProgressed"/> deltas) is a future refinement — TODO.
        /// </remarks>
        private static NeedChannel ComputeCompetence(IHumanContext ctx)
        {
            var goals = ctx.Snapshot.Goals?.Active.ToList() ?? new List<PersistentGoal>();

            var avgProgressSignal = goals.Count == 0
                ? 0.5
                : goals.Average(g => g.Progress - g.Frustration * 0.5);
            var satisfaction = Math.Clamp(0.5 + avgProgressSignal * 0.5, 0.0, 1.0);
            var frustration = goals.Count == 0
                ? 0.0
                : Math.Clamp(goals.Average(g => g.Frustration), 0.0, 1.0);
            return new NeedChannel(satisfaction, frustration);
        }

        /// <summary>
        /// Relatedness ≈ "felt belonging" from <see cref="RelationshipEdge"/> Closeness/Comfort/Trust,
        /// EXPLICITLY EXCLUDING status / social-comparison signals (Respect, PerceivedPrestige,
        /// SocialComparisonOccurred stay elsewhere — relatedness is "not fueled by status gain",
        /// Deci &amp; Ryan 2004).
        /// </summary>
        private static NeedChannel ComputeRelatedness(IHumanContext ctx)
        {
            var edges = ctx.Snapshot.Relationships?.Edges?.Values.ToList() ?? new List<RelationshipEdge>();
            if (edges.Count == 0)
                return new NeedChannel(0.4, 0.1); // mild baseline deficit, no active thwarting

            var avgWarmth = edges.Average(e => (e.Closeness + e.Comfort + e.Trust) / 3.0) / 100.0;
            var satisfaction = Math.Clamp(avgWarmth, 0.0, 1.0);

            // Frustration = active disconnection: low closeness AND no positive history (distinguishes
            // thwarting from a mere low baseline).
            var frustrationSignal = edges.Count(e => e.Closeness < 20 && e.PositiveInteractionCount < 2)
                                    / (double)edges.Count;
            return new NeedChannel(satisfaction, Math.Clamp(frustrationSignal, 0.0, 1.0));
        }

        /// <summary>
        /// Autonomy ≈ volition/self-endorsement. The dominant signal is goal <see cref="GoalOrigin"/>
        /// (self-endorsed <see cref="GoalOrigin.Personality"/> goals feel autonomous; externally imposed
        /// <see cref="GoalOrigin.Scripted"/> goals feel controlled) — the "volition vs pressure" axis
        /// (Scholl et al. 2019). <see cref="Traits.RegulatoryFocusProfile.Promotion"/> enters only as a
        /// WEAK positive covariate (Vaughn 2016a), so Autonomy does not collapse into RegulatoryFocus.
        /// </summary>
        /// <remarks>
        /// Deviation from the plan's MVP sketch (which derived Autonomy purely from Promotion): a
        /// Promotion-only appraisal would correlate r≈1 with Promotion and fail the distinctness
        /// acceptance test. Goal-origin is an existing, principled volition signal that keeps the two
        /// constructs separable. Autonomy frustration is left at 0.0 (a dedicated "pressured action"
        /// thwarting signal is future work — TODO).
        /// </remarks>
        private static NeedChannel ComputeAutonomy(IHumanContext ctx)
        {
            var goals = ctx.Snapshot.Goals?.Active.ToList() ?? new List<PersistentGoal>();

            var volition = goals.Count == 0
                ? 0.5 // no active pursuits → neutral self-endorsement
                : goals.Average(g => g.Origin switch
                {
                    GoalOrigin.Personality => 1.0, // self-concordant → autonomous
                    GoalOrigin.Scripted => 0.0,    // externally imposed → controlled
                    _ => 0.5                       // Event-driven → neutral
                });

            var promotionCovariate = ctx.Personality.RegulatoryFocus?.Promotion ?? 0.5;

            // Volition dominates (weight 0.55); Promotion is a weak covariate (weight 0.2).
            var satisfaction = Math.Clamp(0.15 + volition * 0.55 + promotionCovariate * 0.2, 0.0, 1.0);
            const double frustration = 0.0; // TODO: wire to a "controlled motivation" thwarting signal.
            return new NeedChannel(satisfaction, frustration);
        }
    }
}
