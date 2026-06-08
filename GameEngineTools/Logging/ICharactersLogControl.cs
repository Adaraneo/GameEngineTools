// ICharactersLogControl.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Logging
{
    /// <summary>
    /// Control interface for explicitly flushing character file logs.
    /// </summary>
    public interface ICharactersLogControl
    {
        /// <summary>
        /// Flushes all open character log writers.
        /// </summary>
        void FlushAll();
    }
}
