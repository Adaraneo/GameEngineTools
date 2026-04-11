// ICharacterLodRuntime.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// Stores the current runtime LOD assigned to individual characters.
    /// </summary>
    public interface ICharacterLodRuntime
    {
        CharacterLodLevel Get(HumanId id);

        void Set(HumanId id, CharacterLodLevel level);

        void Clear(HumanId id);
    }
}
