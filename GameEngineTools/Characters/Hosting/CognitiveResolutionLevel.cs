// CognitiveResolutionLevel.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// Runtime simulation fidelity tier for a character.
    /// </summary>
    public enum CognitiveResolutionLevel
    {
        /// <summary>Highest fidelity — the player or focus character.</summary>
        Player = 0,
        /// <summary>Reduced fidelity — characters near the focus.</summary>
        Nearby = 1,
        /// <summary>Lowest fidelity — background characters.</summary>
        Background = 2
    }
}
