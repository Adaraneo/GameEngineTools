// DefaultBehaviorCadencePolicy.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.Options;

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// Default cadence policy based on current runtime LOD and config defaults.
    /// </summary>
    public sealed class DefaultBehaviorCadencePolicy : IBehaviorCadencePolicy
    {
        private readonly CognitiveResolutionLevelConfig _cfg;
        private readonly ICognitiveResolutionLevelRuntime _lodRuntime;

        /// <summary>Creates the policy from cadence config and the runtime LOD registry.</summary>
        /// <param name="cfg">Per-tier decision-cadence configuration.</param>
        /// <param name="lodRuntime">Runtime LOD registry.</param>
        public DefaultBehaviorCadencePolicy(
            IOptions<CognitiveResolutionLevelConfig> cfg,
            ICognitiveResolutionLevelRuntime lodRuntime)
        {
            _cfg = cfg.Value;
            _lodRuntime = lodRuntime;
        }

        /// <inheritdoc/>
        public WTimeSpan? GetDecisionStep(IHuman human)
        {
            var ts = _lodRuntime.Get(human.Id) switch
            {
                CognitiveResolutionLevel.Player => _cfg.PlayerBehaviorDecisionStep,
                CognitiveResolutionLevel.Nearby => _cfg.NearbyBehaviorDecisionStep,
                CognitiveResolutionLevel.Background => _cfg.BackgroundBehaviorDecisionStep,
                _ => null
            };

            if (ts is null || ts.Value <= TimeSpan.Zero)
            {
                return null;
            }

            return WTimeSpan.FromHours(ts.Value.TotalHours);
        }
    }
}
