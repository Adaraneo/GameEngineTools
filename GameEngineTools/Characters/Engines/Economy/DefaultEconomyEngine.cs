// DefaultEconomyEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Economy
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Schedule;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Default <see cref="IEconomyEngine"/>. Purely event-reactive — no per-tick simulation of its own.
    /// Applies a wage when a <c>Work</c> action commits (using the character's occupation and the hours
    /// worked), and deducts/credits coin on <see cref="Purchased"/>/<see cref="Sold"/> events emitted by
    /// the object-interaction commit path. Follows the trailing-nullable-field pattern: an absent
    /// <see cref="EconomyState"/> initializes to <c>Wealth = 0</c> on first event. Food-economy Tier 2.
    /// </summary>
    internal sealed class DefaultEconomyEngine : IEconomyEngine
    {
        #region State and configuration

        /// <inheritdoc/>
        public EconomyState State { get; private set; } = EconomyState.Zero;

        /// <inheritdoc/>
        public EconomyConfig Config { get; }

        #endregion State and configuration

        private readonly ILogger _log;

        public DefaultEconomyEngine(IOptions<EconomyConfig> cfg, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(cfg);
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger<DefaultEconomyEngine>();
        }

        #region IEngine

        /// <inheritdoc/>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            // No per-tick work — economy state changes only in response to events.
        }

        /// <inheritdoc/>
        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            switch (@event)
            {
                case ActionCommitted { ActionName: ActionNames.Work } work when work.Human == ctx.Id:
                    ApplyWorkWage(work, ctx, outbox);
                    break;

                case Purchased purchase when purchase.Actor == ctx.Id:
                    State = ApplyPurchase(State, purchase.Price);
                    break;

                case Sold sale when sale.Actor == ctx.Id:
                    State = ApplySale(State, sale.Price);
                    break;
            }
        }

        /// <inheritdoc/>
        public void RestoreState(EconomyState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            State = state;
        }

        #endregion IEngine

        #region Wealth transitions (pure — unit-testable in isolation)

        /// <summary>Applies a wage payment for time worked.</summary>
        public EconomyState ApplyWage(EconomyState? current, double wagePerHour, double hoursWorked)
        {
            if (wagePerHour < 0) throw new ArgumentOutOfRangeException(nameof(wagePerHour));
            if (hoursWorked < 0) throw new ArgumentOutOfRangeException(nameof(hoursWorked));

            var wealth = (current?.Wealth ?? 0.0) + wagePerHour * hoursWorked;
            return new EconomyState(wealth);
        }

        /// <summary>
        /// Applies a purchase. The caller (Buy commit path + behavior gating) must have already verified
        /// affordability — this only commits the deduction and throws on an unaffordable amount, so a
        /// gating bug surfaces loudly rather than silently overdrawing. Mirrors the gate-vs-commit split
        /// of <c>ProductionService.TryConsumeInputs</c> in Tier 1.
        /// </summary>
        public EconomyState ApplyPurchase(EconomyState current, double price)
        {
            ArgumentNullException.ThrowIfNull(current);
            if (price < 0) throw new ArgumentOutOfRangeException(nameof(price));
            if (current.Wealth < price)
                throw new InvalidOperationException("Purchase committed without sufficient wealth — gating bug.");

            return new EconomyState(current.Wealth - price);
        }

        /// <summary>Applies a sale (surplus item → shop, coin → character).</summary>
        public EconomyState ApplySale(EconomyState? current, double price)
        {
            if (price < 0) throw new ArgumentOutOfRangeException(nameof(price));
            return new EconomyState((current?.Wealth ?? 0.0) + price);
        }

        #endregion Wealth transitions

        #region Helpers

        /// <summary>
        /// Pays the character for a committed <c>Work</c> action: wage-per-hour (by occupation) × the
        /// action's duration in hours. No-op for characters without an occupation (<c>Work</c> is a
        /// generic productivity action; only employed characters draw a wage). Emits <see cref="WageEarned"/>.
        /// </summary>
        private void ApplyWorkWage(ActionCommitted work, IHumanContext ctx, IEventCollector outbox)
        {
            var occupation = ctx.Snapshot.Schedule?.Occupation;
            if (string.IsNullOrEmpty(occupation) || occupation == OccupationIds.None)
                return;

            var hours = Math.Max(0.0, work.Duration.TotalHours);
            if (hours <= 0.0)
                return;

            var wagePerHour = Config.WagePerHour(occupation);
            State = ApplyWage(State, wagePerHour, hours);

            outbox.Add(new WageEarned(work.OccurredAt, ctx.Id, occupation, wagePerHour, hours, State.Wealth));

            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultEconomyEngine)))
                _log.WageEarned(ctx.Id.ToString(), occupation, wagePerHour, hours, State.Wealth);
        }

        #endregion Helpers
    }
}
