// DefaultSelfConceptEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SelfConcept
{
    using System;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Goals;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Default implementation of <see cref="ISelfConceptEngine"/>. Evolves the perceived self from
    /// interaction feedback via self-verification, tracks self-esteem and self-discrepancy, and
    /// seeds a <see cref="PersistentGoalKind.BuildIdentity"/> goal when discrepancy runs high.
    /// </summary>
    /// <remarks>
    /// Confirming feedback (agrees with the current self-view) is accepted at
    /// <see cref="SelfConceptConfig.ConfirmingWeight"/>; disconfirming feedback is discounted to
    /// <see cref="SelfConceptConfig.DisconfirmingWeight"/> (Swann 1983). The asymmetry means a
    /// character rejected by others lowers their perceived extraversion only slowly — but it does
    /// move, eventually opening an ideal/perceived gap that motivates identity work.
    /// </remarks>
    internal sealed class DefaultSelfConceptEngine : ISelfConceptEngine
    {
        #region State and configuration

        /// <inheritdoc/>
        public SelfConcept State { get; private set; }

        /// <inheritdoc/>
        public SelfConceptConfig Config { get; }

        #endregion State and configuration

        #region Construction

        public DefaultSelfConceptEngine(IOptions<SelfConceptConfig> cfg)
        {
            Config = cfg.Value;
            State = SelfConcept.Neutral;
        }

        #endregion Construction

        #region ISelfConceptEngine

        /// <inheritdoc/>
        public void SeedFromPersonality(Personality personality)
        {
            var bf = personality.BigFive;

            // Perceived self starts as the actual self; ideal subset is statically initialised
            // to the actual socially-relevant traits (the R6 life-transition hook shifts it later).
            // Self-esteem is seeded inversely from Neuroticism (high N → lower baseline esteem).
            var esteem = Math.Clamp(0.55 - (bf.Neuroticism - 0.5) * 0.30, 0.0, 1.0);

            var sc = new SelfConcept(
                PerceivedOpenness: bf.Openness,
                PerceivedConscientiousness: bf.Conscientiousness,
                PerceivedExtraversion: bf.Extraversion,
                PerceivedAgreeableness: bf.Agreeableness,
                PerceivedNeuroticism: bf.Neuroticism,
                IdealExtraversion: bf.Extraversion,
                IdealAgreeableness: bf.Agreeableness,
                IdealConscientiousness: bf.Conscientiousness,
                SelfEsteem: esteem,
                SelfDiscrepancy: 0.0);

            State = sc with { SelfDiscrepancy = ComputeDiscrepancy(sc) };
        }

        #endregion ISelfConceptEngine

        #region IEngine — Tick

        /// <inheritdoc/>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            // Recompute discrepancy (ideal may have shifted via a future R6 hook) and seed
            // identity-work when it runs high — but only once while no BuildIdentity goal is active.
            var discrepancy = ComputeDiscrepancy(State);
            if (Math.Abs(discrepancy - State.SelfDiscrepancy) > 1e-9)
                State = State with { SelfDiscrepancy = discrepancy };

            if (discrepancy > Config.DiscrepancyThreshold && !HasActiveBuildIdentity(ctx))
            {
                outbox.Add(new GoalInjected(
                    now, ctx.Id, PersistentGoalKind.BuildIdentity, Config.BuildIdentitySeedSalience));
            }
        }

        #endregion IEngine — Tick

        #region IEngine — Handle

        /// <inheritdoc/>
        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            switch (@event)
            {
                case InteractionOutcome io when io.From == ctx.Id || io.To == ctx.Id:
                    HandleInteraction(io, ctx, outbox);
                    break;

                case LifeStage.LifeStageTransitionOccurred lst when lst.Human == ctx.Id:
                    HandleLifeStageTransition(lst, ctx, outbox);
                    break;
            }
        }

        /// <summary>
        /// R6→R3 hook (the cycle-breaking link deferred from Phase 1): a life-stage transition shifts
        /// the ideal self toward stage-appropriate maturity and seeds identity / meaning goals.
        /// </summary>
        private void HandleLifeStageTransition(
            LifeStage.LifeStageTransitionOccurred lst, IHumanContext ctx, IEventCollector outbox)
        {
            switch (lst.From, lst.To)
            {
                case (StadiumType.Teenager, StadiumType.Adult):
                    // Entering adulthood consolidates identity work.
                    outbox.Add(new GoalInjected(
                        lst.OccurredAt, ctx.Id, PersistentGoalKind.BuildIdentity, Config.BuildIdentitySeedSalience));
                    break;

                case (StadiumType.Adult, StadiumType.MidAged):
                    // Mid-life: ideals mature (more value on conscientiousness/stability, slightly less
                    // on outward extraversion). This re-opens a small discrepancy that motivates meaning-seeking.
                    var s = State with
                    {
                        IdealConscientiousness = Math.Clamp(State.IdealConscientiousness + 0.05, 0.0, 1.0),
                        IdealExtraversion = Math.Clamp(State.IdealExtraversion - 0.03, 0.0, 1.0)
                    };
                    State = s with { SelfDiscrepancy = ComputeDiscrepancy(s) };

                    outbox.Add(new GoalInjected(
                        lst.OccurredAt, ctx.Id, PersistentGoalKind.FindMeaning, Config.BuildIdentitySeedSalience));
                    break;
            }
        }

        #endregion IEngine — Handle

        #region IEngine — RestoreState

        /// <inheritdoc/>
        public void RestoreState(SelfConcept state) => State = state;

        #endregion IEngine — RestoreState

        #region Feedback handling

        private void HandleInteraction(InteractionOutcome io, IHumanContext ctx, IEventCollector outbox)
        {
            // Social feedback as a noisy observation of social competence: accepted → 1, rejected → 0.
            var observation = io.Accepted ? 1.0 : 0.0;

            var s = State;
            var newExtra = UpdatePerceived(s.PerceivedExtraversion, observation);
            var newAgree = UpdatePerceived(s.PerceivedAgreeableness, observation);
            var newEsteem = UpdateEsteem(s.SelfEsteem, observation);

            var extraDelta = Math.Abs(newExtra - s.PerceivedExtraversion);
            var esteemDelta = Math.Abs(newEsteem - s.SelfEsteem);

            s = s with
            {
                PerceivedExtraversion = newExtra,
                PerceivedAgreeableness = newAgree,
                SelfEsteem = newEsteem
            };
            s = s with { SelfDiscrepancy = ComputeDiscrepancy(s) };
            State = s;

            if (extraDelta > Config.MetaperceptionEmitThreshold || esteemDelta > Config.MetaperceptionEmitThreshold)
            {
                outbox.Add(new MetaperceptionUpdated(
                    io.OccurredAt, ctx.Id, s.PerceivedExtraversion, s.PerceivedAgreeableness,
                    s.SelfEsteem, s.SelfDiscrepancy));
            }
        }

        /// <summary>
        /// Self-verification update for a perceived trait. Confirming feedback (same side of the
        /// midpoint as the current self-view) is accepted; disconfirming feedback is discounted.
        /// </summary>
        internal double UpdatePerceived(double perceived, double observation)
        {
            var confirming = (observation >= 0.5) == (perceived >= 0.5);
            var weight = confirming ? Config.ConfirmingWeight : Config.DisconfirmingWeight;
            return Math.Clamp(perceived + (observation - perceived) * weight * Config.PerceivedUpdateStep, 0.0, 1.0);
        }

        private double UpdateEsteem(double esteem, double observation)
        {
            var confirming = (observation >= 0.5) == (esteem >= 0.5);
            var weight = confirming ? Config.ConfirmingWeight : Config.DisconfirmingWeight;
            return Math.Clamp(esteem + (observation - esteem) * weight * Config.EsteemUpdateStep, 0.0, 1.0);
        }

        #endregion Feedback handling

        #region Helpers

        private static double ComputeDiscrepancy(SelfConcept s)
            => (Math.Abs(s.IdealExtraversion - s.PerceivedExtraversion)
              + Math.Abs(s.IdealAgreeableness - s.PerceivedAgreeableness)
              + Math.Abs(s.IdealConscientiousness - s.PerceivedConscientiousness)) / 3.0;

        private static bool HasActiveBuildIdentity(IHumanContext ctx)
        {
            var goals = ctx.Snapshot.Goals;
            if (goals is null) return false;
            return goals.Active.Any(g => g.Kind == PersistentGoalKind.BuildIdentity);
        }

        #endregion Helpers
    }
}
