// CognitiveResolutionLevelConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// Runtime LOD cadence configuration loaded from appsettings.Characters.json.
    /// </summary>
    public sealed record CognitiveResolutionLevelConfig(
        TimeSpan? PlayerBehaviorDecisionStep = null,
        TimeSpan? NearbyBehaviorDecisionStep = null,
        TimeSpan? BackgroundBehaviorDecisionStep = null)
    {
        public CognitiveResolutionLevelConfig() : this(null, null, null) { }
    }
}
