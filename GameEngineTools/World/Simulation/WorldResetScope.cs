// WorldResetScope.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Simulation
{
    using System;

    /// <summary>What <see cref="SimulationScene.Reset"/> returns to the state after generation; combine freely, with the time caveat described there.</summary>
    [Flags]
    public enum WorldResetScope
    {
        /// <summary>Nothing.</summary>
        None = 0,

        /// <summary>Every character: engines, identity, inbox, scheduled events, sleep; at home again.</summary>
        Characters = 1,

        /// <summary>The world objects, from the generated-world file.</summary>
        World = 2,

        /// <summary>The world clock, back to where the scene started.</summary>
        Time = 4,

        /// <summary>All three.</summary>
        All = Characters | World | Time,
    }
}
