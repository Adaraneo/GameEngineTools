// ICognitiveResolutionLevelRuntime.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// Stores the current runtime LOD assigned to individual characters.
    /// </summary>
    public interface ICognitiveResolutionLevelRuntime
    {
        /// <summary>Gets the current LOD tier for a character (defaulting when unset).</summary>
        /// <param name="id">Character identifier.</param>
        CognitiveResolutionLevel Get(HumanId id);

        /// <summary>Sets the LOD tier for a character.</summary>
        /// <param name="id">Character identifier.</param>
        /// <param name="level">The LOD tier to assign.</param>
        void Set(HumanId id, CognitiveResolutionLevel level);

        /// <summary>Clears any explicit LOD assignment for a character.</summary>
        /// <param name="id">Character identifier.</param>
        void Clear(HumanId id);
    }
}
