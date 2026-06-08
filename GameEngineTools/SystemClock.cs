// SystemClock.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools
{
    using System;
    using System.Timers;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Production implementation of <see cref="IClock"/> — automatically advances time
    /// based on <see cref="IWorldClock.TimeScale"/> and <see cref="WorldTimeSpec.TicksPerSecond"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The timer ticks every real second and advances <see cref="Now"/> by
    /// <c>TimeScale × TicksPerSecond</c> world ticks. At <c>TimeScale = 1.0</c>
    /// this means 1 real second = 1 world second.
    /// </para>
    /// <para>
    /// <b>Why <see cref="WorldTimeSpec"/> instead of <c>WorldTimeContext</c>?</b><br/>
    /// <c>WorldTimeContext</c> depends on <c>IClock</c>, and <c>IClock</c> would in turn depend
    /// on <c>WorldTimeContext</c> — a circular dependency. <c>WorldTimeSpec</c> is a pure
    /// data object with no dependencies, so no cycle arises.
    /// </para>
    /// </remarks>
    public sealed class SystemClock : IClock, IDisposable
    {
        #region Soukromá pole

        private readonly Timer _timer;
        private readonly double _timeScale;

        // Number of ticks per real second, scaled to the world's speed.
        // Precomputed once in the constructor — no need to touch the spec on every tick.
        private readonly long _ticksPerRealSecond;

        #endregion Soukromá pole

        #region Konstrukce

        /// <summary>
        /// Initializes the clock. The initial <see cref="Now"/> is set to the current
        /// world time from <paramref name="worldClock"/>.
        /// </summary>
        /// <param name="worldClock">
        /// Source of the initial world-tick time and <see cref="IWorldClock.TimeScale"/>.
        /// </param>
        /// <param name="spec">
        /// World-time specification — needed to compute the advance in <c>Timer_Elapsed</c>.
        /// We do not use <c>WorldTimeContext</c> to avoid a circular dependency.
        /// </param>
        public SystemClock(IWorldClock worldClock, WorldTimeSpec spec)
        {
            _timeScale = worldClock.TimeScale;
            _ticksPerRealSecond = spec.TicksPerSecond;

            Now = new WDateTime(worldClock.NowWorldTicks());

            _timer = new Timer { Interval = 1000 };
            _timer.Elapsed += Timer_Elapsed;
        }

        #endregion Konstrukce

        #region IClock

        /// <inheritdoc/>
        public WDateTime Now { get; private set; }

        /// <inheritdoc/>
        public void Start() => _timer.Start();

        /// <inheritdoc/>
        public void Stop() => _timer.Stop();

        #endregion IClock

        #region Veřejné utility

        /// <summary>
        /// Manually sets the current time. Use it in the game loop or in tests.
        /// </summary>
        /// <param name="now">The new current game time.</param>
        public void SetNow(WDateTime now) => Now = now;

        /// <summary>
        /// Advances the current time by the given interval.
        /// </summary>
        /// <param name="dt">The interval by which to advance the time.</param>
        public void Advance(WTimeSpan dt) => Now = Now + dt;

        #endregion Veřejné utility

        #region IDisposable

        /// <inheritdoc/>
        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
        }

        #endregion IDisposable

        #region Privátní

        private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            // Advance by TimeScale world seconds per real second.
            // The precomputed _ticksPerRealSecond avoids touching the spec in the hot path.
            Now = new WDateTime(Now.WorldTicks + (long)(_timeScale * _ticksPerRealSecond));
        }

        #endregion Privátní
    }
}
