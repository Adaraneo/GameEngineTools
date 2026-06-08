// BehaviorIntentKind.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Intent
{
    /// <summary>
    /// Intent buckets used to stabilize action selection across adjacent ticks.
    /// </summary>
    public enum BehaviorIntentKind
    {
        /// <summary>No active intent.</summary>
        None,

        /// <summary>Engaged in a work session.</summary>
        WorkSession,

        /// <summary>Seeking rest.</summary>
        RestSeeking,

        /// <summary>Seeking social contact.</summary>
        SocialSeeking,

        /// <summary>Seeking privacy.</summary>
        PrivacySeeking,

        /// <summary>Exploring / seeking novelty.</summary>
        Exploration,

        /// <summary>Engaged in self-care.</summary>
        SelfCare,

        /// <summary>
        /// Character is actively seeking a location that has food or drink available.
        /// </summary>
        FoodSeeking,
    }
}
