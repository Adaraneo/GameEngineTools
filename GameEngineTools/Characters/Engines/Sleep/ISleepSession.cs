// ISleepSession.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Sleep
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Contract for a single character's sleep session.
    /// The session is created on <see cref="SleepConfirmed"/> and destroyed on <see cref="SleepEnded"/>.
    /// </summary>
    /// <remarks>
    /// The session is deliberately separate from <see cref="IEngine{TState,TConfig}"/> —
    /// sleep has its own life cycle (Begin → Tick → End/Interrupt),
    /// which does not match the continuous ticking of the other engines.
    /// <br/><br/>
    /// The BehaviorEngine holds a reference to the active session and forwards ticks to it
    /// while <see cref="IsActive"/> returns <c>true</c>.
    /// </remarks>
    public interface ISleepSession
    {
        #region Stav

        /// <summary>
        /// Current phase of the sleep cycle.
        /// </summary>
        SleepPhase CurrentPhase { get; }

        /// <summary>
        /// True if the session is still running (the character is asleep).
        /// False means the session has ended — either naturally or by interruption.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Planned wake-up time.
        /// May be earlier than the actual end if an interruption occurs.
        /// </summary>
        WDateTime PlannedWakeUp { get; }

        /// <summary>
        /// Optional companion for shared sleep.
        /// <c>null</c> if the character sleeps alone.
        /// </summary>
        HumanId? Companion { get; }

        /// <summary>
        /// Total number of hours the character has slept in this session.
        /// Grows continuously with each tick.
        /// </summary>
        double HoursSlept { get; }

        #endregion Stav

        #region Lifecycle

        /// <summary>
        /// Begins the sleep session.
        /// Switches the phase to <see cref="SleepPhase.Falling"/> and publishes
        /// <see cref="SleepPhaseChanged"/> and optionally <see cref="SharedSleepBegan"/>.
        /// </summary>
        /// <param name="now">Current game time.</param>
        /// <param name="plannedWakeUp">Planned wake-up time.</param>
        /// <param name="ctx">Kontext postavy.</param>
        /// <param name="outbox">Event collector for this tick.</param>
        /// <param name="companion">Optional companion for shared sleep.</param>
        /// <param name="sharedType">The shared-sleep type — must be set if <paramref name="companion"/> != null.</param>
        void Begin(
            WDateTime now,
            WDateTime plannedWakeUp,
            IHumanContext ctx,
            IEventCollector outbox,
            HumanId? companion = null,
            SharedSleepType? sharedType = null);

        /// <summary>
        /// Continuous tick — advances time in the session, switches phases,
        /// and generates risk and narrative events.
        /// If the session ends naturally, sets <see cref="IsActive"/> to <c>false</c>
        /// a publikuje <see cref="SleepEnded"/>.
        /// </summary>
        /// <param name="now">Current game time.</param>
        /// <param name="dt">Length of the elapsed game interval.</param>
        /// <param name="ctx">Kontext postavy.</param>
        /// <param name="outbox">Event collector for this tick.</param>
        void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox);

        /// <summary>
        /// Interrupts the sleep before its planned end.
        /// Publikuje <see cref="SleepInterrupted"/> a <see cref="SleepEnded"/>,
        /// sets <see cref="IsActive"/> to <c>false</c>.
        /// </summary>
        /// <param name="now">Game time of the interruption.</param>
        /// <param name="cause">Cause of the interruption.</param>
        /// <param name="ctx">Kontext postavy.</param>
        /// <param name="outbox">Event collector for this tick.</param>
        void Interrupt(WDateTime now, InterruptCause cause, IHumanContext ctx, IEventCollector outbox);

        #endregion Lifecycle
    }
}
