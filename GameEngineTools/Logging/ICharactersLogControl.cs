// ICharactersLogControl.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Logging
{
    /// <summary>
    /// Řídicí rozhraní pro explicitní flush character file logů.
    /// </summary>
    public interface ICharactersLogControl
    {
        /// <summary>
        /// Vyflushuje všechny otevřené character log writery.
        /// </summary>
        void FlushAll();
    }
}
