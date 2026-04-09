// RelationshipEdge.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using Characters.Core;

    /// <summary>
    /// A directed edge in the relationship graph — how character A perceives character B.
    /// </summary>
    /// <remarks>
    /// The graph is asymmetric: A may like B more than B likes A.
    /// All numeric dimensions are in [0, 100].
    /// </remarks>
    public sealed record RelationshipEdge(
        HumanId A,
        HumanId B,
        double Like,
        double Trust,
        /// <summary>
        /// Accumulated familiarity from repeated exposure and accepted contact.
        /// Higher values mean A feels more acquainted with B.
        /// </summary>
        double Familiarity,
        /// <summary>
        /// Perceived aesthetic appeal driven mainly by taste and preference matching.
        /// </summary>
        double AestheticAttraction,
        /// <summary>
        /// Perceived physical appeal driven mainly by baseline appearance cues.
        /// </summary>
        double PhysicalAttraction,
        /// <summary>
        /// Romantic inclination toward B.
        /// More context-dependent than raw physical attraction.
        /// </summary>
        double RomanticInterest,
        /// <summary>
        /// Sexual inclination toward B.
        /// Strongly shaped by physical attraction plus comfort and intimacy context.
        /// </summary>
        double SexualInterest,
        double Closeness,
        double Respect,
        double Comfort,
        DomainBreakdown Breakdown,
        /// <summary>
        /// Running count of positive (accepted) interactions between A and B.
        /// Used to compute the familiarity bonus in <see cref="DefaultRelationshipsEngine"/>.
        /// </summary>
        int PositiveInteractionCount = 0);
}
