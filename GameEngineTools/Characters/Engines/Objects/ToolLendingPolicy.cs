// ToolLendingPolicy.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using System;
    using GameEngineTools.Characters.Engines.Relationships;

    /// <summary>The chance that an owner lends a tool to someone who asked: a pure function of how the owner feels about the asker and whether the owner needs the tool now; every number is a design pick.</summary>
    public static class ToolLendingPolicy
    {
        // Weights of the owner's view of the asker (edge values are 0-100); a stranger (no edge) scores 0.
        private const double TrustWeight = 0.5;
        private const double LikeWeight = 0.3;
        private const double ClosenessWeight = 0.2;

        // The score at which the chance is one half; a more agreeable owner asks for less (45 to 65 over Agreeableness 1 to 0).
        private const double BaseThreshold = 55.0;
        private const double AgreeableThresholdShift = 20.0;

        // How many score points turn the chance from one half to about three quarters.
        private const double ChanceSpread = 8.0;

        // An owner who needs the tool now lends a tenth as readily.
        private const double NeededNowFactor = 0.1;

        /// <summary>Gets the owner's feeling for the asker, 0-100.</summary>
        public static double Score(RelationshipEdge? edge)
        {
            return edge is null ? 0.0 : TrustWeight * edge.Trust + LikeWeight * edge.Like + ClosenessWeight * edge.Closeness;
        }

        /// <summary>Gets whether the owner needs the tool now: on a work shift while the tool lies where the owner stands; a tool at another place is not missed.</summary>
        public static bool OwnerNeedsItNow(bool ownerOnWorkShift, string? ownerPlace, string toolLocation)
        {
            return ownerOnWorkShift && ownerPlace is not null && string.Equals(ownerPlace, toolLocation, StringComparison.Ordinal);
        }

        /// <summary>Gets the chance (0-1) that the owner lends: logistic in the score above the owner's threshold, a tenth of that while the owner needs the tool now.</summary>
        public static double Probability(RelationshipEdge? edge, double ownerAgreeableness, bool ownerNeedsItNow)
        {
            if (edge is null || edge.IsContemptuouslyDestroyed)
            {
                return 0.0;
            }

            var margin = Score(edge) - (BaseThreshold - AgreeableThresholdShift * (ownerAgreeableness - 0.5));
            var chance = 1.0 / (1.0 + Math.Exp(-margin / ChanceSpread));
            return ownerNeedsItNow ? chance * NeededNowFactor : chance;
        }
    }
}
