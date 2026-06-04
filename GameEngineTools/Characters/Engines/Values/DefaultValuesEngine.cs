// DefaultValuesEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Values
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Default implementation of <see cref="IValuesEngine"/>. Evolves <see cref="ValuesState.Current"/>
    /// from lived experience while slowly regressing it toward the immutable
    /// <see cref="ValuesState.Baseline"/>.
    /// </summary>
    /// <remarks>
    /// Drift channels:
    /// <list type="number">
    ///   <item><b>Violation</b> (<see cref="ValueCongruenceViolated"/>): erode the violated value,
    ///   strengthen the value the conflicting action expressed (the "winner").</item>
    ///   <item><b>Affirmation</b> (<see cref="ActionCommitted"/> with positive congruence):
    ///   strengthen the dominant expressed value. Discounted when the act had an external cause
    ///   (Kelley discounting).</item>
    ///   <item><b>Regression</b> (<see cref="Tick"/>): lerp Current → Baseline.</item>
    /// </list>
    /// All nudges propagate through the Schwartz circumplex (opposite pole decremented, neighbours
    /// nudged) so the value structure stays internally coherent (Bardi 2009).
    /// </remarks>
    internal sealed class DefaultValuesEngine : IValuesEngine
    {
        #region Circumplex layout

        // Canonical circular order (Schwartz 1992). opposite(i) = (i + 5) mod 10.
        private const int SelfDirection = 0;
        private const int Stimulation   = 1;
        private const int Hedonism      = 2;
        private const int Achievement   = 3;
        private const int Power         = 4;
        private const int Security      = 5;
        private const int Conformity    = 6;
        private const int Tradition     = 7;
        private const int Benevolence   = 8;
        private const int Universalism  = 9;

        private const int Dim = 10;

        #endregion Circumplex layout

        #region State and configuration

        /// <inheritdoc/>
        public ValuesState State { get; private set; }

        /// <inheritdoc/>
        public ValuesConfig Config { get; }

        #endregion State and configuration

        #region Construction

        private readonly ILogger _log;

        public DefaultValuesEngine(IOptions<ValuesConfig> cfg, ILogger<DefaultValuesEngine> log)
        {
            Config = cfg.Value;
            _log = log;

            // Until seeded, use a neutral baseline (all values at population midpoint).
            var neutral = new ValuesProfile(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5);
            State = ValuesState.FromBaseline(neutral);
        }

        #endregion Construction

        #region IValuesEngine

        /// <inheritdoc/>
        public void SeedFromBaseline(ValuesProfile baseline)
            => State = ValuesState.FromBaseline(baseline);

        #endregion IValuesEngine

        #region IEngine — Tick (regression to baseline)

        /// <inheritdoc/>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            var days = Math.Max(0.0, dt.TotalDays);
            if (days <= 0.0) return;

            var regressionMult = IsAdolescent(ctx, now) ? Config.AdolescentRegressionMultiplier : 1.0;
            var fraction = Math.Clamp(Config.RegressionPerDay * regressionMult * days, 0.0, 1.0);
            if (fraction <= 0.0) return;

            var cur = ToArray(State.Current);
            var baseArr = ToArray(State.Baseline);

            var changed = false;
            for (var i = 0; i < Dim; i++)
            {
                var next = cur[i] + (baseArr[i] - cur[i]) * fraction;
                if (Math.Abs(next - cur[i]) > 1e-9)
                {
                    cur[i] = next;
                    changed = true;
                }
            }

            if (changed)
                State = State with { Current = FromArray(cur) };
        }

        #endregion IEngine — Tick

        #region IEngine — Handle

        /// <inheritdoc/>
        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            switch (@event)
            {
                case ValueCongruenceViolated vcv when vcv.Actor == ctx.Id:
                    HandleViolation(vcv, ctx, outbox);
                    break;

                case ActionCommitted ac when ac.Human == ctx.Id:
                    HandleAffirmation(ac, ctx);
                    break;
            }
        }

        #endregion IEngine — Handle

        #region IEngine — RestoreState

        /// <inheritdoc/>
        public void RestoreState(ValuesState state) => State = state;

        #endregion IEngine — RestoreState

        #region Drift handlers

        private void HandleViolation(ValueCongruenceViolated vcv, IHumanContext ctx, IEventCollector outbox)
        {
            var lr = Config.LearningRate * (IsAdolescent(ctx, vcv.OccurredAt) ? Config.AdolescentLearningMultiplier : 1.0);
            if (lr <= 0.0) return;

            var cur = ToArray(State.Current);

            // Erode the violated value (named in the event; fall back to the action's most-violated dim).
            var loading = ActionValueLoadings.Get(vcv.ActionName);
            var loadArr = ToArray(loading);

            var violatedIdx = ParseValueName(vcv.DominantViolatedValue);
            if (violatedIdx < 0)
                violatedIdx = DominantIndex(loadArr, cur, mostNegative: true);

            if (violatedIdx >= 0)
                ApplyNudge(cur, violatedIdx, -lr);

            // Strengthen the value the conflicting action expressed (the "winner").
            var winnerIdx = DominantIndex(loadArr, cur, mostNegative: false);
            if (winnerIdx >= 0 && winnerIdx != violatedIdx)
                ApplyNudge(cur, winnerIdx, +lr);

            State = State with { Current = FromArray(cur) };

            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultValuesEngine)))
            {
                _log.ValueDrifted(
                    ctx.Id.Value.ToString(),
                    violatedIdx >= 0 ? ValueName(violatedIdx) : "none",
                    winnerIdx >= 0 ? ValueName(winnerIdx) : "none",
                    -lr);
            }
        }

        private void HandleAffirmation(ActionCommitted ac, IHumanContext ctx)
        {
            var loading = ActionValueLoadings.Get(ac.ActionName);
            if (loading == ValueLoadVector.Zero) return;

            var congruence = loading.Congruence(State.Current);
            if (congruence <= Config.PositiveCongruenceThreshold) return;

            var cur = ToArray(State.Current);
            var loadArr = ToArray(loading);

            var winnerIdx = DominantIndex(loadArr, cur, mostNegative: false);
            if (winnerIdx < 0) return;

            // Kelley discounting: an action with a sufficient external cause (it was displaced /
            // pressured by conflict arbitration) reinforces the internal value less.
            var externalIncentive = ac.ConflictReason is not null;
            var discount = externalIncentive ? Config.ExternalIncentiveDiscount : 1.0;
            var adolescent = IsAdolescent(ctx, ac.OccurredAt) ? Config.AdolescentLearningMultiplier : 1.0;
            var lr = Config.LearningRate * adolescent * discount;
            if (lr <= 0.0) return;

            ApplyNudge(cur, winnerIdx, +lr);
            State = State with { Current = FromArray(cur) };
        }

        #endregion Drift handlers

        #region Reinforcement math (testable)

        /// <summary>
        /// Applies a single value nudge with circumplex coupling to a profile array
        /// (canonical Schwartz order). Used by the engine and exercised directly by tests.
        /// </summary>
        internal static double[] ApplyReinforcement(double[] profile, int valueIndex, double delta, ValuesConfig cfg)
        {
            var copy = (double[])profile.Clone();
            ApplyNudgeInternal(copy, valueIndex, delta, cfg);
            return copy;
        }

        private void ApplyNudge(double[] arr, int idx, double delta) => ApplyNudgeInternal(arr, idx, delta, Config);

        private static void ApplyNudgeInternal(double[] arr, int idx, double delta, ValuesConfig cfg)
        {
            arr[idx] = Math.Clamp(arr[idx] + delta, 0.0, 1.0);

            var opposite = (idx + 5) % Dim;
            arr[opposite] = Math.Clamp(arr[opposite] - delta * cfg.OppositeCouplingFactor, 0.0, 1.0);

            var left = (idx + Dim - 1) % Dim;
            var right = (idx + 1) % Dim;
            arr[left]  = Math.Clamp(arr[left]  + delta * cfg.NeighborCouplingFactor, 0.0, 1.0);
            arr[right] = Math.Clamp(arr[right] + delta * cfg.NeighborCouplingFactor, 0.0, 1.0);
        }

        #endregion Reinforcement math

        #region Helpers

        /// <summary>
        /// Returns the index of the value with the most positive (or most negative) weighted load,
        /// i.e. loading[d] × profile[d]. Returns -1 when no qualifying dimension exists.
        /// </summary>
        private static int DominantIndex(double[] loading, double[] profile, bool mostNegative)
        {
            var best = -1;
            var bestVal = mostNegative ? 0.0 : 0.0;
            for (var d = 0; d < Dim; d++)
            {
                var weighted = loading[d] * profile[d];
                if (mostNegative)
                {
                    if (weighted < bestVal) { bestVal = weighted; best = d; }
                }
                else
                {
                    if (weighted > bestVal) { bestVal = weighted; best = d; }
                }
            }

            return best;
        }

        private bool IsAdolescent(IHumanContext ctx, WDateTime now)
        {
            var age = AgeYears(ctx.Identity.BirthDate, now);
            return StadiumResolver.Resolve(age) == StadiumType.Teenager;
        }

        private static int AgeYears(WDateOnly birth, WDateTime now)
        {
            var today = now.Date;
            var age = today.Year - birth.Year;
            if (today.Month < birth.Month || (today.Month == birth.Month && today.Day < birth.Day))
                age--;
            return Math.Max(0, age);
        }

        private static double[] ToArray(ValuesProfile p) => new[]
        {
            p.SelfDirection, p.Stimulation, p.Hedonism, p.Achievement, p.Power,
            p.Security, p.Conformity, p.Tradition, p.Benevolence, p.Universalism
        };

        private static double[] ToArray(ValueLoadVector v) => new[]
        {
            v.SelfDirection, v.Stimulation, v.Hedonism, v.Achievement, v.Power,
            v.Security, v.Conformity, v.Tradition, v.Benevolence, v.Universalism
        };

        private static ValuesProfile FromArray(double[] a) => new ValuesProfile(
            Benevolence:   a[Benevolence],
            Universalism:  a[Universalism],
            SelfDirection: a[SelfDirection],
            Stimulation:   a[Stimulation],
            Hedonism:      a[Hedonism],
            Achievement:   a[Achievement],
            Power:         a[Power],
            Security:      a[Security],
            Conformity:    a[Conformity],
            Tradition:     a[Tradition]);

        private static int ParseValueName(string? name) => name switch
        {
            nameof(ValuesProfile.SelfDirection) => SelfDirection,
            nameof(ValuesProfile.Stimulation)   => Stimulation,
            nameof(ValuesProfile.Hedonism)      => Hedonism,
            nameof(ValuesProfile.Achievement)   => Achievement,
            nameof(ValuesProfile.Power)         => Power,
            nameof(ValuesProfile.Security)      => Security,
            nameof(ValuesProfile.Conformity)    => Conformity,
            nameof(ValuesProfile.Tradition)     => Tradition,
            nameof(ValuesProfile.Benevolence)   => Benevolence,
            nameof(ValuesProfile.Universalism)  => Universalism,
            _ => -1
        };

        private static string ValueName(int idx) => idx switch
        {
            SelfDirection => nameof(ValuesProfile.SelfDirection),
            Stimulation   => nameof(ValuesProfile.Stimulation),
            Hedonism      => nameof(ValuesProfile.Hedonism),
            Achievement   => nameof(ValuesProfile.Achievement),
            Power         => nameof(ValuesProfile.Power),
            Security      => nameof(ValuesProfile.Security),
            Conformity    => nameof(ValuesProfile.Conformity),
            Tradition     => nameof(ValuesProfile.Tradition),
            Benevolence   => nameof(ValuesProfile.Benevolence),
            Universalism  => nameof(ValuesProfile.Universalism),
            _ => "Unknown"
        };

        #endregion Helpers
    }
}
