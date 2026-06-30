// SimulationControl.cs
// Copyright (c) 50PSoftware

namespace WorldObserver.Simulation
{
    /// <summary>
    /// Shared, thread-safe pacing state for the realtime simulation loop. The simulation runs on its
    /// own background thread; the browser (via WorldHub) mutates this to pause, single-step, or change
    /// speed. Pause granularity is one tick.
    /// </summary>
    public sealed class SimulationControl
    {
        private volatile bool _paused;
        private volatile int _delayMs;
        private volatile int _simMinutesPerTick;
        private int _step;

        /// <summary>Creates the control with the configured initial per-tick delay and world tempo.</summary>
        /// <param name="initialDelayMs">Starting real-time delay per tick (ms); clamped to [0, 2000].</param>
        /// <param name="initialSimMinutesPerTick">Starting game-minutes advanced per tick; clamped to [1, 240].</param>
        public SimulationControl(int initialDelayMs = 120, int initialSimMinutesPerTick = 3)
        {
            _delayMs = Math.Clamp(initialDelayMs, 0, 2000);
            _simMinutesPerTick = Math.Clamp(initialSimMinutesPerTick, 1, 240);
        }

        /// <summary>Whether the loop is currently paused.</summary>
        public bool Paused => _paused;

        /// <summary>Real-time delay applied after every tick (ms). Higher = slower.</summary>
        public int DelayMs => _delayMs;

        /// <summary>
        /// Game-minutes advanced per tick (world tempo). Higher = the world clock runs faster for the
        /// same compute, at the cost of coarser temporal resolution (short trips become less visible).
        /// </summary>
        public int SimMinutesPerTick => _simMinutesPerTick;

        /// <summary>Sets the world tempo in game-minutes per tick, clamped to [1, 240].</summary>
        public void SetSimMinutesPerTick(int minutes) => _simMinutesPerTick = Math.Clamp(minutes, 1, 240);

        /// <summary>Pauses the loop after the current tick completes.</summary>
        public void Pause() => _paused = true;

        /// <summary>Resumes continuous ticking.</summary>
        public void Play() => _paused = false;

        /// <summary>Requests exactly one tick while paused.</summary>
        public void Step() => Interlocked.Exchange(ref _step, 1);

        /// <summary>Sets the per-tick delay in milliseconds, clamped to [0, 2000].</summary>
        public void SetDelay(int delayMs) => _delayMs = Math.Clamp(delayMs, 0, 2000);

        /// <summary>
        /// Blocks the simulation thread according to the current pause / speed state. Called once at
        /// the start of every tick. Throws <see cref="OperationCanceledException"/> on shutdown.
        /// </summary>
        public void WaitForTurn(CancellationToken ct)
        {
            while (_paused)
            {
                ct.ThrowIfCancellationRequested();
                if (Interlocked.Exchange(ref _step, 0) == 1)
                    break; // let exactly one tick through, stay paused
                Thread.Sleep(40);
            }

            ct.ThrowIfCancellationRequested();

            var delay = _delayMs;
            if (delay > 0)
                Thread.Sleep(delay);
        }
    }
}
