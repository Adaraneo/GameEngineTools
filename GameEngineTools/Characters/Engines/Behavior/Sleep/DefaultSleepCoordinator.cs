// DefaultSleepCoordinator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Sleep
{
    using System;
    using System.Collections.Generic;
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Logging;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using static ActionNames;

    internal sealed class DefaultSleepCoordinator : ISleepCoordinator
    {
        private readonly SleepConfig _sleepCfg;
        private readonly BehaviorConfig _behaviorCfg;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _log;
        private ISleepSession? _activeSession;

        public DefaultSleepCoordinator(SleepConfig sleepCfg, BehaviorConfig behaviorCfg, ILoggerFactory loggerFactory)
        {
            _sleepCfg = sleepCfg; _behaviorCfg = behaviorCfg; _loggerFactory = loggerFactory; _log = loggerFactory.CreateLogger<DefaultSleepCoordinator>();
        }

        public SleepDecisionResult Tick(BehaviorContext context)
        {
            var state = context.State;
            var ctx = context.HumanContext;
            if (_activeSession is { IsActive: true })
            {
                _activeSession.Tick(context.Now, context.Dt, ctx, context.Outbox);
                if (!_activeSession.IsActive)
                {
                    var dict = new Dictionary<string, double>(state.Cooldowns ?? new Dictionary<string, double>()) { [Sleep] = _behaviorCfg.SleepCooldownHours };
                    state = state with { CurrentPlan = null, Cooldowns = dict };
                    _activeSession = null;
                    using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultSleepCoordinator)))) _log.SleepSessionEnded(ctx.Id.Value.ToString(), _behaviorCfg.SleepCooldownHours);
                }
                return new SleepDecisionResult(true, state);
            }

            if (state.WaitingForSleepConfirmation) return new SleepDecisionResult(true, state);
            if (state.SleepGraceExpiresAt.HasValue && context.Now < state.SleepGraceExpiresAt.Value)
            {
                _log.SleepGracePenalty(ctx.Id.Value.ToString(), 1.0 + state.SleepDeclineCount * 0.5, state.SleepDeclineCount);
                return new SleepDecisionResult(false, state);
            }

            var ph = ctx.Snapshot.Physiology;
            var isEmergency = state.NeedRest >= _sleepCfg.EmergencyNeedRestThreshold || ph.Energy <= _sleepCfg.EmergencyEnergyThreshold;
            if (!isEmergency && (ph.Thirst >= _sleepCfg.ThirstSleepBlockThreshold || ph.Hunger >= _sleepCfg.HungerSleepBlockThreshold))
            {
                _log.SleepBlockedByBiology(ctx.Id.Value.ToString(), ph.Hunger, ph.Thirst);
                return new SleepDecisionResult(false, state);
            }

            var sleepCooldown = BehaviorMath.CooldownFor(context.Cooldowns, Sleep);
            if (state.NeedRest >= _sleepCfg.SleepPromptThreshold && (sleepCooldown <= 0 || isEmergency))
            {
                var surface = ctx.Snapshot.InteractionSurface;
                var inRestLocation = surface.Kind == SurfaceKind.Rest || surface.Kind == SurfaceKind.Private;
                if (!inRestLocation && !isEmergency)
                {
                    var moveDur = WTimeSpan.FromMinutes(20);
                    context.Outbox.Add(new ActionCommitted(context.Now, ctx.Id, MoveToRest, moveDur));
                    return new SleepDecisionResult(true, state with { CurrentPlan = new PlannedAction(MoveToRest, context.Now, moveDur, state.NeedRest) });
                }

                context.Outbox.Add(new SleepPromptRequested(context.Now, ctx.Id, state.NeedRest));
                using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultSleepCoordinator)))) if (isEmergency && sleepCooldown > 0) _log.SleepPromptSent(ctx.Id.Value.ToString(), state.NeedRest);
                return new SleepDecisionResult(true, state with { WaitingForSleepConfirmation = true, SleepGraceExpiresAt = null });
            }

            return new SleepDecisionResult(false, state);
        }

        public BehaviorState Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox, BehaviorState state)
        {
            switch (@event)
            {
                case SleepConfirmed sc:
                    var session = new DefaultSleepSession(_sleepCfg, _loggerFactory, ctx.Random);
                    var sleepHours = Math.Clamp(_behaviorCfg.BaseSleepHours + ctx.Snapshot.Physiology.SleepDebtHours * 0.5, _behaviorCfg.MinSleepHours, _behaviorCfg.MaxSleepHours);
                    var plannedWakeUp = sc.PlannedWakeUp != default ? sc.PlannedWakeUp : sc.OccurredAt + WTimeSpan.FromHours(sleepHours);
                    session.Begin(sc.OccurredAt, plannedWakeUp, ctx, outbox, sc.Companion, sc.SharedType);
                    _activeSession = session;
                    using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultSleepCoordinator)))) _log.SleepStarted(ctx.Id.Value.ToString(), sleepHours);
                    return state with { WaitingForSleepConfirmation = false, SleepGraceExpiresAt = null, SleepDeclineCount = 0, CurrentPlan = new PlannedAction(Sleep, sc.OccurredAt, WTimeSpan.FromHours(sleepHours), 100) };
                case SleepDeclined sd:
                    var newDeclineCount = state.SleepDeclineCount + 1;
                    var graceHours = Math.Max(1.0, _sleepCfg.SleepGraceHours / newDeclineCount);
                    using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultSleepCoordinator)))) _log.SleepDeclinedByPlayer(ctx.Id.Value.ToString(), newDeclineCount, graceHours);
                    return state with { WaitingForSleepConfirmation = false, SleepDeclineCount = newDeclineCount, SleepGraceExpiresAt = sd.OccurredAt + WTimeSpan.FromHours(graceHours) };
                case SleepInterrupted si when _activeSession is { IsActive: true }:
                    _activeSession.Interrupt(si.OccurredAt, si.Cause, ctx, outbox);
                    break;
            }
            return state;
        }

        public void RestoreState() => _activeSession = null;
    }
}
