// WorldObserverOptions.cs
// Copyright (c) 50PSoftware

namespace WorldObserver.Simulation
{
    /// <summary>
    /// App-level configuration, bound from the <c>WorldObserver</c> section of appsettings.json.
    /// </summary>
    public sealed class WorldObserverOptions
    {
        /// <summary>Number of characters generated into the fresh world at startup.</summary>
        public int CharacterCount { get; set; } = 6;

        /// <summary>
        /// Initial real-time delay applied after every tick, in milliseconds (higher = slower).
        /// The browser speed slider overrides this live; this is just the starting tempo.
        /// </summary>
        public int DefaultDelayMs { get; set; } = 120;

        /// <summary>How many recent distinct locations to keep in each character's movement trail.</summary>
        public int TrailLength { get; set; } = 6;

        /// <summary>
        /// Simulation time advanced per tick, in minutes. This is the world's temporal resolution:
        /// travel (and everything else) is only observable at tick boundaries, so a coarse step hides
        /// short trips. In-city walks here are ~1–9 min, so the default 3 min lets typical journeys
        /// span several ticks and show up as "na cestě" instead of completing inside a single tick.
        /// </summary>
        public int TickStepMinutes { get; set; } = 3;
    }
}
