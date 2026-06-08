// DefaultInterestEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interests
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Options;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Default implementation of <see cref="IInterestEngine"/>. Raises a RIASEC interest when the
    /// character performs a matching action and finds it rewarding, and regresses Current toward
    /// Baseline over time so interests cannot run away to the ceiling.
    /// </summary>
    internal sealed class DefaultInterestEngine : IInterestEngine
    {
        #region Dimensions

        private const int Realistic = 0;
        private const int Investigative = 1;
        private const int Artistic = 2;
        private const int Social = 3;
        private const int Enterprising = 4;
        private const int Conventional = 5;
        private const int Dim = 6;

        #endregion Dimensions

        #region State and configuration

        /// <inheritdoc/>
        public InterestState State { get; private set; }

        /// <inheritdoc/>
        public InterestConfig Config { get; }

        #endregion State and configuration

        #region Construction

        public DefaultInterestEngine(IOptions<InterestConfig> cfg)
        {
            Config = cfg.Value;
            var neutral = new InterestProfile(0.5, 0.5, 0.5, 0.5, 0.5, 0.5);
            State = InterestState.FromBaseline(neutral);
        }

        #endregion Construction

        #region IInterestEngine

        /// <inheritdoc/>
        public void SeedFromBaseline(InterestProfile baseline)
            => State = InterestState.FromBaseline(baseline);

        #endregion IInterestEngine

        #region IEngine — Tick (regression brake)

        /// <inheritdoc/>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            var days = Math.Max(0.0, dt.TotalDays);
            if (days <= 0.0) return;

            var regressionMult = IsPlastic(ctx, now) ? Config.YoungRegressionMultiplier : 1.0;
            var fraction = Math.Clamp(Config.RegressionPerDay * regressionMult * days, 0.0, 1.0);
            if (fraction <= 0.0) return;

            var cur = ToArray(State.Current);
            var baseArr = ToArray(State.Baseline);

            var changed = false;
            for (var i = 0; i < Dim; i++)
            {
                var next = cur[i] + (baseArr[i] - cur[i]) * fraction;
                if (Math.Abs(next - cur[i]) > 1e-9) { cur[i] = next; changed = true; }
            }

            if (changed) State = State with { Current = FromArray(cur) };
        }

        #endregion IEngine — Tick (regression brake)

        #region IEngine — Handle

        /// <inheritdoc/>
        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            switch (@event)
            {
                case ActionCommitted ac when ac.Human == ctx.Id:
                    HandleAction(ac, ctx);
                    break;
            }
        }

        #endregion IEngine — Handle

        #region IEngine — RestoreState

        /// <inheritdoc/>
        public void RestoreState(InterestState state) => State = state;

        #endregion IEngine — RestoreState

        #region Reward handling

        private void HandleAction(ActionCommitted ac, IHumanContext ctx)
        {
            var dim = MapActionToDimension(ac.ActionName);
            if (dim < 0) return;

            // Only rewarding experiences raise interest (high positive valence at the time).
            var valence = ctx.Snapshot.Psychology.Valence;
            if (valence < Config.RewardValenceThreshold) return;

            var learnMult = IsPlastic(ctx, ac.OccurredAt) ? Config.YoungLearningMultiplier : 1.0;
            var gain = Config.LearningRate * learnMult;

            var cur = ToArray(State.Current);
            cur[dim] = Math.Clamp(cur[dim] + gain, 0.0, 1.0);
            State = State with { Current = FromArray(cur) };
        }

        private static int MapActionToDimension(string actionName) => actionName switch
        {
            Create => Artistic,
            Work => Conventional,
            ReachOut => Social,
            InviteIntimacy => Social,
            _ => -1
        };

        #endregion Reward handling

        #region Helpers

        private bool IsPlastic(IHumanContext ctx, WDateTime now)
            => AgeYears(ctx.Identity.BirthDate, now) < Config.PlasticityAgeMax;

        private static int AgeYears(WDateOnly birth, WDateTime now)
        {
            var today = now.Date;
            var age = today.Year - birth.Year;
            if (today.Month < birth.Month || (today.Month == birth.Month && today.Day < birth.Day))
                age--;
            return Math.Max(0, age);
        }

        private static double[] ToArray(InterestProfile p) => new[]
        {
            p.Realistic, p.Investigative, p.Artistic, p.Social, p.Enterprising, p.Conventional
        };

        private static InterestProfile FromArray(double[] a) => new InterestProfile(
            Realistic: a[Realistic],
            Investigative: a[Investigative],
            Artistic: a[Artistic],
            Social: a[Social],
            Enterprising: a[Enterprising],
            Conventional: a[Conventional]);

        #endregion Helpers
    }
}
