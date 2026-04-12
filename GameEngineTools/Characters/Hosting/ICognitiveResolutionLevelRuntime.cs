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
        CognitiveResolutionLevel Get(HumanId id);

        void Set(HumanId id, CognitiveResolutionLevel level);

        void Clear(HumanId id);
    }
}
