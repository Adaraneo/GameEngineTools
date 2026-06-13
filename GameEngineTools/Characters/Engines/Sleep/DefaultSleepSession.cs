// DefaultSleepSession.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Sleep
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Default implementation of a sleep session.
    /// Drives the progression through phases and generates risk and narrative events.
    /// </summary>
    /// <remarks>
    /// A session is created as a new instance for each sleep —
    /// it is not registered in the DI container as a singleton.
    /// It is created by <c>DefaultBehaviorEngine</c> when handling <see cref="SleepConfirmed"/>.
    /// </remarks>
    internal sealed class DefaultSleepSession : ISleepSession
    {
        #region Private fields

        private readonly SleepConfig _cfg;
        private readonly ILogger _log;
        private readonly IRandomSource _rng;

        /// <summary>Time the session began (= the time of falling asleep).</summary>
        private WDateTime _sleepStart;

        /// <summary>Time the current phase began.</summary>
        private WDateTime _phaseStart;

        /// <summary>Stress level at the moment of falling asleep — affects the nightmare probability.</summary>
        private double _stressAtSleepStart;

        /// <summary>True if a dream event has already been generated during the REM phase.</summary>
        private bool _dreamFiredThisRem;

        #endregion Private fields

        #region Public properties (ISleepSession)

        /// <inheritdoc/>
        public SleepPhase CurrentPhase { get; private set; }

        /// <inheritdoc/>
        public bool IsActive { get; private set; }

        /// <inheritdoc/>
        public WDateTime PlannedWakeUp { get; private set; }

        /// <inheritdoc/>
        public HumanId? Companion { get; private set; }

        /// <inheritdoc/>
        public double HoursSlept { get; private set; }

        #endregion Public properties (ISleepSession)

        #region Constructor

        /// <summary>
        /// Creates the session instance. Call <see cref="Begin"/> to start it.
        /// </summary>
        /// <param name="cfg">Configuration of the sleep subsystem.</param>
        /// <param name="loggerFactory">Logger factory.</param>
        /// <param name="rng">The character's deterministic random-number generator.</param>
        public DefaultSleepSession(SleepConfig cfg, ILoggerFactory loggerFactory, IRandomSource rng)
        {
            _cfg = cfg;
            _log = loggerFactory.CreateLogger<DefaultSleepSession>();
            _rng = rng;
        }

        #endregion Constructor

        #region Lifecycle

        /// <inheritdoc/>
        public void Begin(
            WDateTime now,
            WDateTime plannedWakeUp,
            IHumanContext ctx,
            IEventCollector outbox,
            HumanId? companion = null,
            SharedSleepType? sharedType = null)
        {
            _sleepStart = now;
            _phaseStart = now;
            _stressAtSleepStart = ctx.Snapshot.Psychology.Stress;
            _dreamFiredThisRem = false;
            HoursSlept = 0;
            IsActive = true;
            Companion = companion;
            PlannedWakeUp = plannedWakeUp;

            EnterPhase(SleepPhase.Falling, now, ctx, outbox);

            // Shared sleep — publish the context
            if (companion.HasValue && sharedType.HasValue)
            {
                outbox.Add(new SharedSleepBegan(now, ctx.Id, companion.Value, sharedType.Value));
                using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultSleepSession)))
                {
                    _log.SharedSleepStarted(ctx.Id.Value.ToString(), sharedType.Value.ToString(), companion.Value.ToString());
                }
            }
            else
            {
                using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultSleepSession)))
                {
                    _log.SleepStarted(ctx.Id.Value.ToString(), (plannedWakeUp - now).TotalHours);
                }
            }
        }

        /// <inheritdoc/>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            if (!IsActive) return;

            var h = Math.Max(0, dt.TotalHours);
            HoursSlept += h;

            // Natural awakening — the time has elapsed
            if (now >= PlannedWakeUp)
            {
                EndSession(now, wasInterrupted: false, ctx, outbox);
                return;
            }

            // Risk check for the current phase
            CheckAmbush(now, h, ctx, outbox);

            // Phase progression
            var timeInPhase = now - _phaseStart;
            switch (CurrentPhase)
            {
                case SleepPhase.Falling:
                    if (timeInPhase.TotalHours >= _cfg.FallingDurationHours)
                        EnterPhase(SleepPhase.Light, now, ctx, outbox);
                    break;

                case SleepPhase.Light:
                    if (timeInPhase.TotalHours >= _cfg.LightDurationHours)
                        EnterPhase(SleepPhase.Deep, now, ctx, outbox);
                    break;

                case SleepPhase.Deep:
                    if (timeInPhase.TotalHours >= _cfg.DeepDurationHours)
                        EnterPhase(SleepPhase.Rem, now, ctx, outbox);
                    break;

                case SleepPhase.Rem:
                    // Narrative: dream and nightmare — each only once per REM cycle
                    FireRemEvents(now, ctx, outbox);

                    if (timeInPhase.TotalHours >= _cfg.RemDurationHours)
                    {
                        _dreamFiredThisRem = false; // reset for the next REM
                        EnterPhase(SleepPhase.Light, now, ctx, outbox); // cycle again
                    }
                    break;

                case SleepPhase.Waking:
                    // Waking phase — the session ends on the next Tick or immediately
                    EndSession(now, wasInterrupted: false, ctx, outbox);
                    break;
            }
        }

        /// <inheritdoc/>
        public void Interrupt(WDateTime now, InterruptCause cause, IHumanContext ctx, IEventCollector outbox)
        {
            if (!IsActive) return;

            outbox.Add(new SleepInterrupted(now, ctx.Id, cause, CurrentPhase));
            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultSleepSession)))
            {
                _log.SleepInterrupted(ctx.Id.Value.ToString(), cause.ToString(), CurrentPhase.ToString());
            }

            EndSession(now, wasInterrupted: true, ctx, outbox);
        }

        #endregion Lifecycle

        #region Private helper methods

        /// <summary>
        /// Transitions to the given phase and publishes <see cref="SleepPhaseChanged"/>.
        /// </summary>
        private void EnterPhase(SleepPhase phase, WDateTime now, IHumanContext ctx, IEventCollector outbox)
        {
            CurrentPhase = phase;
            _phaseStart = now;
            outbox.Add(new SleepPhaseChanged(now, ctx.Id, phase));
            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultSleepSession)))
            {
                _log.SleepPhaseEntered(ctx.Id.Value.ToString(), phase.ToString());
            }
        }

        /// <summary>
        /// Checks the ambush probability for the current tick.
        /// The risk is modulated by the sleep phase and the presence of a companion.
        /// </summary>
        private void CheckAmbush(WDateTime now, double h, IHumanContext ctx, IEventCollector outbox)
        {
            // The deep phase lowers ambush risk (the character hears less, but an attack is harder)
            // REM phase — the character is at rest but reacts poorly
            var phaseModifier = CurrentPhase switch
            {
                SleepPhase.Falling => 1.2,  // easily ambushed, not yet asleep
                SleepPhase.Light => 1.0,
                SleepPhase.Deep => 0.6,  // deeply asleep — attacker has the advantage, but the character reacts worse
                SleepPhase.Rem => 0.4,  // physically relaxed, mentally active
                SleepPhase.Waking => 0.8,
                _ => 1.0
            };

            var companionModifier = Companion.HasValue ? _cfg.CompanionGuardModifier : 1.0;
            var chance = _cfg.AmbushBaseChancePerHour * h * phaseModifier * companionModifier;

            if (_rng.Chance(chance))
            {
                using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultSleepSession)))
                {
                    _log.SleepAmbush(ctx.Id.Value.ToString(), CurrentPhase.ToString());
                }

                Interrupt(now, InterruptCause.Ambush, ctx, outbox);
            }
        }

        /// <summary>
        /// Generates narrative events during the REM phase (a dream or a nightmare).
        /// Each event is generated at most once per REM pass.
        /// </summary>
        private void FireRemEvents(WDateTime now, IHumanContext ctx, IEventCollector outbox)
        {
            if (_dreamFiredThisRem) return;

            // The nightmare probability depends on the stress at the moment of falling asleep
            var nightmareChance = _stressAtSleepStart > _cfg.NightmareStressThreshold
                ? _cfg.NightmareChanceHighStress
                : _cfg.NightmareChanceNormal;

            if (_rng.Chance(nightmareChance))
            {
                outbox.Add(new NightmareTriggered(now, ctx.Id, _stressAtSleepStart));
                using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultSleepSession)))
                {
                    _log.SleepNightmare(ctx.Id.Value.ToString(), _stressAtSleepStart);
                }

                Interrupt(now, InterruptCause.Nightmare, ctx, outbox);
            }
            else
            {
                // Deterministic seed from the character's seed + time — so dreams are consistent
                var dreamSeed = ctx.Random.Next(0, int.MaxValue);
                outbox.Add(new DreamOccurred(now, ctx.Id, dreamSeed));
                using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultSleepSession)))
                {
                    _log.SleepDream(ctx.Id.Value.ToString(), dreamSeed);
                }
            }

            _dreamFiredThisRem = true;
        }

        /// <summary>
        /// Ends the session, computes the sleep quality and publishes <see cref="SleepEnded"/>.
        /// </summary>
        /// <param name="now">Current game time.</param>
        /// <param name="wasInterrupted">True if the sleep was interrupted.</param>
        private void EndSession(WDateTime now, bool wasInterrupted, IHumanContext ctx, IEventCollector outbox)
        {
            IsActive = false;

            HoursSlept = (now - _sleepStart).TotalHours;

            var quality = ComputeSleepQuality(wasInterrupted);

            outbox.Add(new SleepEnded(now, ctx.Id, HoursSlept, quality, wasInterrupted));
            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultSleepSession)))
            {
                _log.SleepWokeUp(ctx.Id.Value.ToString(), HoursSlept, quality, wasInterrupted);
            }
        }

        /// <summary>
        /// Computes the sleep quality (0–100) based on duration and course.
        /// <br/>
        /// Ideal sleep = <c>SleepConfig.BaseSleepHours</c> without interruption = 100.
        /// </summary>
        private double ComputeSleepQuality(bool wasInterrupted)
        {
            // Base: ratio of time slept to the ideal duration
            // The ideal is in BehaviorConfig.BaseSleepHours (passed via the plannedWakeUp duration)
            var plannedHours = (PlannedWakeUp - _sleepStart).TotalHours;
            var completionRatio = plannedHours > 0
                ? Math.Min(1.0, HoursSlept / plannedHours)
                : 0.5;

            var quality = completionRatio * 100.0;

            // Penalty for interruption — depends on which phase the interruption occurred in
            if (wasInterrupted)
            {
                quality *= CurrentPhase switch
                {
                    SleepPhase.Falling => 0.2,  // almost nothing
                    SleepPhase.Light => 0.4,
                    SleepPhase.Deep => 0.6,
                    SleepPhase.Rem => 0.75, // REM is valuable, interruption hurts
                    SleepPhase.Waking => 0.9,  // almost finished sleeping
                    _ => 0.5
                };
            }

            return Math.Clamp(quality, 0, 100);
        }

        #endregion Private helper methods
    }
}
