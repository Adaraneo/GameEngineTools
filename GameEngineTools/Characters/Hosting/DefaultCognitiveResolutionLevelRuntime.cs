// DefaultCognitiveResolutionLevelRuntime.cs
// Copyright (c) 50PSoftware

using System.Collections.Concurrent;
using GameEngineTools.Characters.Core;

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// In-memory runtime LOD registry for characters.
    /// </summary>
    public sealed class DefaultCognitiveResolutionLevelRuntime : ICognitiveResolutionLevelRuntime
    {
        private readonly ConcurrentDictionary<HumanId, CognitiveResolutionLevel> _levels = new();

        /// <inheritdoc/>
        public CognitiveResolutionLevel Get(HumanId id)
            => _levels.TryGetValue(id, out var level)
                ? level
                : CognitiveResolutionLevel.Nearby;

        /// <inheritdoc/>
        public void Set(HumanId id, CognitiveResolutionLevel level)
            => _levels[id] = level;

        /// <inheritdoc/>
        public void Clear(HumanId id)
            => _levels.TryRemove(id, out _);
    }
}
