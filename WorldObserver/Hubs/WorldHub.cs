// WorldHub.cs
// Copyright (c) 50PSoftware

namespace WorldObserver.Hubs
{
    using Microsoft.AspNetCore.SignalR;
    using WorldObserver.Simulation;

    /// <summary>
    /// SignalR hub bridging the browser and the realtime simulation.
    /// Server → client: <c>Tick</c> (full world state) and <c>Narrative</c> (one event line).
    /// Client → server: the playback controls below, which mutate <see cref="SimulationControl"/>.
    /// </summary>
    public sealed class WorldHub : Hub
    {
        private readonly SimulationControl _control;

        /// <summary>Creates the hub with the shared pacing state.</summary>
        public WorldHub(SimulationControl control) => _control = control;

        /// <summary>Pauses the simulation.</summary>
        public void Pause() => _control.Pause();

        /// <summary>Resumes the simulation.</summary>
        public void Play() => _control.Play();

        /// <summary>Advances exactly one tick while paused.</summary>
        public void Step() => _control.Step();

        /// <summary>Sets the per-tick real-time delay in milliseconds (higher = slower).</summary>
        public void SetDelay(int delayMs) => _control.SetDelay(delayMs);

        /// <summary>Sets the world tempo: game-minutes advanced per tick (higher = world clock faster).</summary>
        public void SetTickMinutes(int minutes) => _control.SetSimMinutesPerTick(minutes);
    }
}
