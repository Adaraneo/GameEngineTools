// IClock.cs
// Copyright (c) 50PSoftware

using GameEngineTools.World.Utils.Time;

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
    }
}
