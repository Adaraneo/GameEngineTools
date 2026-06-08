// DefaultSocialFidelityPolicy.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;
using Microsoft.Extensions.Options;

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// Default social fidelity policy backed by current runtime LOD.
    /// </summary>
    public sealed class DefaultSocialFidelityPolicy : ISocialFidelityPolicy
    {
        private readonly CharacterFidelityConfig _cfg;
        private readonly ICognitiveResolutionLevelRuntime _lodRuntime;

        /// <summary>Creates the policy from fidelity config and the runtime LOD registry.</summary>
        /// <param name="cfg">Per-tier fidelity configuration.</param>
        /// <param name="lodRuntime">Runtime LOD registry.</param>
        public DefaultSocialFidelityPolicy(
            IOptions<CharacterFidelityConfig> cfg,
            ICognitiveResolutionLevelRuntime lodRuntime)
        {
            _cfg = cfg.Value;
            _lodRuntime = lodRuntime;
        }

        /// <inheritdoc/>
        public SocialFidelityLevel GetLevel(HumanId human)
            => _lodRuntime.Get(human) switch
            {
                CognitiveResolutionLevel.Player => _cfg.PlayerSocial,
                CognitiveResolutionLevel.Nearby => _cfg.NearbySocial,
                CognitiveResolutionLevel.Background => _cfg.BackgroundSocial,
                _ => SocialFidelityLevel.Full
            };
    }
}
