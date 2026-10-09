// DefaultSleepCoordinator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Sleep
{
    using System;
    using System.Collections.Generic;
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using static ActionNames;

    /// <summary>
    /// Owns the sleep prompt flow and runtime sleep session lifecycle outside ordinary need evaluation.
    /// </summary>
    internal sealed class DefaultSleepCoordinator : ISleepCoordinator
    {
        #region Private fields

        private readonly SleepConfig _sleepCfg;
        private readonly BehaviorConfig _behaviorCfg;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _log;
        private ISleepSession? _activeSession;

        /// <summary>Session state from a loaded save, turned into a live session on the next tick (the character's RNG is only available then).</summary>
        private SleepSessionState? _pendingRestore;

        #endregion Private fields

        #region Construction

        public DefaultSleepCoordinator(SleepConfig sleepCfg, BehaviorConfig behaviorCfg, ILoggerFactory loggerFactory)
        {
            _sleepCfg = sleepCfg; _behaviorCfg = behaviorCfg; _loggerFactory = loggerFactory; _log = loggerFactory.CreateLogger<DefaultSleepCoordinator>();
        }

        #endregion Construction

        #region ISleepCoordinator

        public SleepDecisionResult Tick(BehaviorContext context)
        {
            var state = context.State;
            var ctx = context.HumanContext;

            // A game restored mid-sleep resumes the session from the state it was saved with.
            if (_activeSession is null && _pendingRestore is { } saved)
            {
                var resumed = new DefaultSleepSession(_sleepCfg, _loggerFactory, ctx.Random);
                resumed.Restore(saved);
                _activeSession = resumed;
            }

            _pendingRestore = null;

            // A session ended outside Tick (an interrupt event), or a Sleep plan without any session (a save from before sessions were
            // persisted): close the plan so the character is not left "asleep" with no recovery.
            if (_activeSession is { IsActive: false } || (_activeSession is null && state.CurrentPlan?.Name == Sleep))
            {
                _activeSession = null;
                state = EndSleep(state, ctx);
            }

            // Active sleep sessions own the whole tick until they end naturally or are interrupted.
            if (_activeSession is { IsActive: true })
            {
                _activeSession.Tick(context.Now, context.Dt, ctx, context.Outbox);
                if (!_activeSession.IsActive)
                {
                    state = EndSleep(state, ctx);
                    _activeSession = null;
                }
                else
                {
                    state = state with { ActiveSleep = _activeSession.Capture() };
                }

                return new SleepDecisionResult(true, state);
            }

            if (state.WaitingForSleepConfirmation) return new SleepDecisionResult(true, state);

            // During grace, sleep does not block other behavior; it just raises the sleep penalty over time.
            if (state.SleepGraceExpiresAt.HasValue && context.Now < state.SleepGraceExpiresAt.Value)
            {
                _log.SleepGracePenalty(ctx.Id.Value.ToString(), 1.0 + state.SleepDeclineCount * 0.5, state.SleepDeclineCount);
                return new SleepDecisionResult(false, state);
            }

            var ph = ctx.Snapshot.Physiology;
            var isEmergency = state.NeedRest >= _sleepCfg.EmergencyNeedRestThreshold || ph.Energy <= _sleepCfg.EmergencyEnergyThreshold;

            // Moderate sleepiness still yields to critical hunger or thirst unless the character is collapsing.
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
                using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultSleepCoordinator))) if (isEmergency && sleepCooldown > 0) _log.SleepPromptSent(ctx.Id.Value.ToString(), state.NeedRest);
                return new SleepDecisionResult(true, state with { WaitingForSleepConfirmation = true, SleepGraceExpiresAt = null });
            }

            return new SleepDecisionResult(false, state);
        }

        private BehaviorState EndSleep(BehaviorState state, IHumanContext ctx)
        {
            var dict = new Dictionary<string, double>(state.Cooldowns ?? new Dictionary<string, double>()) { [Sleep] = _behaviorCfg.SleepCooldownHours * WWorld.Spec.BodyClockScale };
            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultSleepCoordinator))) _log.SleepSessionEnded(ctx.Id.Value.ToString(), _behaviorCfg.SleepCooldownHours);
            return state with { CurrentPlan = null, Cooldowns = dict, ActiveSleep = null };
        }

        public BehaviorState Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox, BehaviorState state)
        {
            switch (@event)
            {
                case SleepConfirmed sc:
                    var ph = ctx.Snapshot.Physiology;
                    // Sleep session creation stays here so the runtime session never leaks into BehaviorState.
                    var session = new DefaultSleepSession(_sleepCfg, _loggerFactory, ctx.Random);
                    // Sleep lengths are written for an Earth body clock; a body adapted to the planet's day stretches or compresses them.
                    var bodyScale = WWorld.Spec.BodyClockScale;
                    var minSleep = _behaviorCfg.MinSleepHours * bodyScale;
                    var maxSleep = _behaviorCfg.MaxSleepHours * bodyScale;
                    var sleepHours = Math.Clamp((_behaviorCfg.BaseSleepHours + ph.SleepDebtHours * 0.5) * bodyScale, minSleep, maxSleep);

                    // Hunger disturbs sleep — high hunger reduces sleep duration.
                    // Biological basis: ghrelin (hunger hormone) promotes wakefulness (Hanlon et al. 2016).
                    if (ph.Hunger > _sleepCfg.HungerSleepShorteningThreshold)
                    {
                        var hungerFraction = (ph.Hunger - _sleepCfg.HungerSleepShorteningThreshold)
                                           / (100.0 - _sleepCfg.HungerSleepShorteningThreshold);
                        sleepHours *= (1.0 - hungerFraction * _sleepCfg.HungerSleepShorteningMax);
                        sleepHours = Math.Max(sleepHours, minSleep);
                    }

                    // Without an alarm the character wakes on its own when Process S reaches the circadian waking threshold;
                    // the clamped duration (shortened by hunger) is then only the quality reference and the longest sleep is MaxSleepHours.
                    var alarm = sc.PlannedWakeUp != default;
                    var plannedWakeUp = alarm
                        ? sc.PlannedWakeUp
                        : sc.OccurredAt + WTimeSpan.FromHours(Math.Max(sleepHours, maxSleep));
                    var spontaneous = alarm ? null : new SpontaneousWake(minSleep, sleepHours);
                    session.Begin(sc.OccurredAt, plannedWakeUp, ctx, outbox, sc.Companion, sc.SharedType, spontaneous);
                    _activeSession = session;
                    using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultSleepCoordinator))) _log.SleepStarted(ctx.Id.Value.ToString(), sleepHours);
                    return state with { WaitingForSleepConfirmation = false, SleepGraceExpiresAt = null, SleepDeclineCount = 0, ActiveSleep = session.Capture(), CurrentPlan = new PlannedAction(Sleep, sc.OccurredAt, WTimeSpan.FromHours(sleepHours), 100) };

                case SleepDeclined sd:
                    var newDeclineCount = state.SleepDeclineCount + 1;
                    var graceHours = Math.Max(1.0, _sleepCfg.SleepGraceHours / newDeclineCount);
                    using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultSleepCoordinator))) _log.SleepDeclinedByPlayer(ctx.Id.Value.ToString(), newDeclineCount, graceHours);
                    return state with { WaitingForSleepConfirmation = false, SleepDeclineCount = newDeclineCount, SleepGraceExpiresAt = sd.OccurredAt + WTimeSpan.FromHours(graceHours) };

                case SleepInterrupted si when _activeSession is { IsActive: true }:
                    _activeSession.Interrupt(si.OccurredAt, si.Cause, ctx, outbox);
                    break;
            }
            return state;
        }

        public void RestoreState(BehaviorState state)
        {
            _activeSession = null;
            _pendingRestore = state.ActiveSleep;
        }

        #endregion ISleepCoordinator
    }
}
