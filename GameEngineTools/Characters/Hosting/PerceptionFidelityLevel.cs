// PerceptionFidelityLevel.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// Runtime fidelity level for perception systems.
    /// </summary>
    public enum PerceptionFidelityLevel
    {
        /// <summary>Full perception.</summary>
        Full = 0,
        /// <summary>Perceive only the local surroundings.</summary>
        LocalOnly = 1,
        /// <summary>Coarse, low-detail perception.</summary>
        Coarse = 2
    }
}
