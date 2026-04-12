// CharactersLogMirrorMode.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Logging
{
    /// <summary>
    /// Určuje, kam se zapisují character log události.
    /// </summary>
    public enum CharactersLogMirrorMode
    {
        /// <summary>
        /// Všechny události se zapisují pouze do globálního logu.
        /// </summary>
        GlobalOnly,

        /// <summary>
        /// Scoped události se zapisují do globálního logu i do per-person/per-subsystem mirroru.
        /// </summary>
        GlobalAndScoped
    }
}
