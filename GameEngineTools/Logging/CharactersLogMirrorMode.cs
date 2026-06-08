// CharactersLogMirrorMode.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Logging
{
    /// <summary>
    /// Determines where character log entries are written.
    /// </summary>
    public enum CharactersLogMirrorMode
    {
        /// <summary>
        /// All entries are written only to the global log.
        /// </summary>
        GlobalOnly,

        /// <summary>
        /// Scoped entries are written to the global log and to the per-person/per-subsystem mirror.
        /// </summary>
        GlobalAndScoped
    }
}
