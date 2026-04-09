// ActionCategory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    /// <summary>
    /// Groups actions into coarse continuity buckets for inertia and novelty shaping.
    /// </summary>
    internal enum ActionCategory
    {
        /// <summary>
        /// Focused productive work such as <c>Work</c> and <c>Create</c>.
        /// </summary>
        Productive,

        /// <summary>
        /// Social approach and intimacy actions.
        /// </summary>
        Social,

        /// <summary>
        /// Biological regulation such as eating, drinking, and self-care.
        /// </summary>
        Biological,

        /// <summary>
        /// Passive rest or low-engagement recovery.
        /// </summary>
        Rest
    }
}
