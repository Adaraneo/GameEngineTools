// IWorldClock.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Time
{
    /// <summary>Low-level world clock exposing the simulation time scale and raw world-tick count.</summary>
    public interface IWorldClock
    {
        /// <summary>Multiplier mapping real time to world time.</summary>
        double TimeScale { get; }

        internal long NowWorldTicks();
    }
}
