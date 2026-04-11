// DefaultCharacterLodRuntime.cs
// Copyright (c) 50PSoftware

using System.Collections.Concurrent;
using GameEngineTools.Characters.Core;

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// In-memory runtime LOD registry for characters.
    /// </summary>
    public sealed class DefaultCharacterLodRuntime : ICharacterLodRuntime
    {
        private readonly ConcurrentDictionary<HumanId, CharacterLodLevel> _levels = new();

        public CharacterLodLevel Get(HumanId id)
            => _levels.TryGetValue(id, out var level)
                ? level
                : CharacterLodLevel.Nearby;

        public void Set(HumanId id, CharacterLodLevel level)
            => _levels[id] = level;

        public void Clear(HumanId id)
            => _levels.TryRemove(id, out _);
    }
}
