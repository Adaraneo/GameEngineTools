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
        private readonly CharacterLodConfig _cfg;
        private readonly ICharacterLodRuntime _lodRuntime;

        public DefaultBehaviorCadencePolicy(
            IOptions<CharacterLodConfig> cfg,
            ICharacterLodRuntime lodRuntime)
        {
            _cfg = cfg.Value;
            _lodRuntime = lodRuntime;
        }

        public WTimeSpan? GetDecisionStep(IHuman human)
        {
            var ts = _lodRuntime.Get(human.Id) switch
            {
                CharacterLodLevel.Player => _cfg.PlayerBehaviorDecisionStep,
                CharacterLodLevel.Nearby => _cfg.NearbyBehaviorDecisionStep,
                CharacterLodLevel.Background => _cfg.BackgroundBehaviorDecisionStep,
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
