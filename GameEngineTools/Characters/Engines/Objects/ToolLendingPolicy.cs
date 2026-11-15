// ToolLendingPolicy.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Engines.Relationships;

    /// <summary>Whether an owner lends a tool to someone who asked: a pure function of how the owner feels about the asker and whether the owner needs the tool now. All numbers are design picks, not sourced.</summary>
    public static class ToolLendingPolicy
    {
        // Weights of the owner's view of the asker (edge values are 0-100); a stranger (no edge) scores 0.
        private const double TrustWeight = 0.5;
        private const double LikeWeight = 0.3;
        private const double ClosenessWeight = 0.2;

        // The score needed to lend; a more agreeable owner asks for less (45 to 65 over Agreeableness 1 to 0).
        private const double BaseThreshold = 55.0;
        private const double AgreeableThresholdShift = 20.0;

        /// <summary>The owner's feeling for the asker, 0-100.</summary>
        public static double Score(RelationshipEdge? edge)
            => edge is null ? 0.0 : TrustWeight * edge.Trust + LikeWeight * edge.Like + ClosenessWeight * edge.Closeness;

        /// <summary>True when the owner lends: not needing the tool now, and the score reaches the threshold.</summary>
        public static bool Accepts(RelationshipEdge? edge, double ownerAgreeableness, bool ownerOnWorkShift)
            => !ownerOnWorkShift && edge is { IsContemptuouslyDestroyed: false } && Score(edge) >= BaseThreshold - AgreeableThresholdShift * (ownerAgreeableness - 0.5);
    }
}
