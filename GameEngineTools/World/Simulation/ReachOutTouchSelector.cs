// ReachOutTouchSelector.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Simulation
{
    using System;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Relationships;

    /// <summary>
    /// Selects whether a ReachOut should also attempt a cautious touch escalation.
    /// </summary>
    /// <remarks>
    /// Light touch unlocks earlier than before but stays uncommon.
    /// Friendly touch remains gated by privacy and warmer relationship context.
    /// </remarks>
    public static class ReachOutTouchSelector
    {
        #region Public API

        /// <summary>
        /// Chooses a touch level to attempt, or <c>null</c> when no touch should be tried.
        /// </summary>
        /// <param name="edge">Directional relationship edge toward the target.</param>
        /// <param name="hasPrivacy">Whether the current interaction context offers privacy.</param>
        /// <param name="rng">Random source used to keep touch attempts uncommon.</param>
        /// <returns>The selected touch level, or <c>null</c> when no touch should be attempted.</returns>
        public static TouchLevel? SelectTouchLevel(RelationshipEdge? edge, bool hasPrivacy, Random rng)
        {
            ArgumentNullException.ThrowIfNull(rng);

            if (edge is null)
            {
                return null;
            }

            if (CanAttemptFriendlyTouch(edge, hasPrivacy) && rng.NextDouble() < 0.05)
            {
                return TouchLevel.Friendly;
            }

            if (CanAttemptLightTouch(edge) && rng.NextDouble() < 0.06)
            {
                return TouchLevel.Light;
            }

            return null;
        }

        /// <summary>
        /// Determines whether light touch is relationship-plausible, before randomness is applied.
        /// </summary>
        public static bool CanAttemptLightTouch(RelationshipEdge? edge)
            => edge is not null
                && edge.Closeness > 15
                && edge.Comfort > 50;

        /// <summary>
        /// Determines whether friendly touch is relationship-plausible, before randomness is applied.
        /// </summary>
        public static bool CanAttemptFriendlyTouch(RelationshipEdge? edge, bool hasPrivacy)
            => edge is not null
                && hasPrivacy
                && edge.Closeness > 30
                && edge.SexualInterest > 30;

        #endregion Public API
    }
}
