// BehaviorIntentKind.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Intent
{
    /// <summary>
    /// Intent buckets used to stabilize action selection across adjacent ticks.
    /// </summary>
    public enum BehaviorIntentKind
    {
        None,
        WorkSession,
        RestSeeking,
        SocialSeeking,
        PrivacySeeking,
        Exploration,
        SelfCare,
        /// <summary>
        /// Character is actively seeking a location that has food or drink available.
        /// </summary>
        FoodSeeking,
    }
}
