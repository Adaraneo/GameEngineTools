// IClock.cs
// Copyright (c) 50PSoftware

using GameEngineTools.World.Utils.Time;
using System;

namespace GameEngineTools.World.Core.Time
{
    /// <summary>A startable/stoppable world clock exposing the current world time.</summary>
    public interface IClock
    {
        /// <summary>The current world time.</summary>
        WDateTime Now { get; }

        /// <summary>Starts the clock advancing.</summary>
        void Start();

        /// <summary>Stops the clock.</summary>
        void Stop();

        /// <summary>Sets the current world time (a reset to the start of the world). Clocks that cannot be set refuse.</summary>
        void SetNow(WDateTime now) => throw new NotSupportedException("This clock cannot be set.");
    }
}
