// MemoryFidelityLevel.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// Runtime fidelity level for episodic memory ingestion.
    /// </summary>
    public enum MemoryFidelityLevel
    {
        /// <summary>Full episodic memory ingestion.</summary>
        Full = 0,
        /// <summary>Reduced memory ingestion.</summary>
        Reduced = 1,
        /// <summary>Minimal memory ingestion.</summary>
        Minimal = 2
    }
}
