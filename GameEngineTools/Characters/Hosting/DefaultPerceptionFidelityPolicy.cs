// DefaultPerceptionFidelityPolicy.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;
using Microsoft.Extensions.Options;

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// Default perception fidelity policy backed by current runtime LOD.
    /// </summary>
    public sealed class DefaultPerceptionFidelityPolicy : IPerceptionFidelityPolicy
    {
        private readonly CharacterFidelityConfig _cfg;
        private readonly ICognitiveResolutionLevelRuntime _lodRuntime;

        public DefaultPerceptionFidelityPolicy(
            IOptions<CharacterFidelityConfig> cfg,
            ICognitiveResolutionLevelRuntime lodRuntime)
        {
            _cfg = cfg.Value;
            _lodRuntime = lodRuntime;
        }

        public PerceptionFidelityLevel GetLevel(HumanId human)
            => _lodRuntime.Get(human) switch
            {
                CognitiveResolutionLevel.Player => _cfg.PlayerPerception,
                CognitiveResolutionLevel.Nearby => _cfg.NearbyPerception,
                CognitiveResolutionLevel.Background => _cfg.BackgroundPerception,
                _ => PerceptionFidelityLevel.Full
            };
    }
}
