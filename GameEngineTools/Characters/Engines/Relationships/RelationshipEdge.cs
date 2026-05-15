// RelationshipEdge.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// A directed edge in the relationship graph — how character A perceives character B.
    /// </summary>
    /// <remarks>
    /// The graph is asymmetric: A may like B more than B likes A.
    /// All numeric dimensions are in [0, 100] unless noted otherwise.
    /// </remarks>
    public sealed record RelationshipEdge(
        HumanId A,
        HumanId B,
        double Like,
        double Trust,
        /// <summary>
        /// Accumulated familiarity from repeated exposure and accepted contact.
        /// Higher values mean A feels more acquainted with B.
        /// Non-monotonic with Like: very high Familiarity without continued positive contact
        /// slowly erodes Like (Norton, Frost &amp; Ariely 2007).
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
        double IntimateAffinity,
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
        /// Never decays — cumulative historical counter.
        /// </summary>
        int PositiveInteractionCount = 0,
        /// <summary>
        /// Last known biological sex category of B, when the interaction source provided it.
        /// Kept on the edge so later target scoring can use orientation without requiring a world lookup.
        /// </summary>
        SexBiology? TargetBiology = null,
        /// <summary>
        /// How much the relationship operates on communal norms (responding to needs, not tracking reciprocity).
        /// High CommunalStrength: tracking and recording favors actively hurts the bond (Clark &amp; Mills 2012).
        /// Grows from intimate touch and sexual encounters; decays very slowly.
        /// </summary>
        double CommunalStrength = 0,
        /// <summary>
        /// How much the relationship operates on exchange norms (equity, explicit reciprocity).
        /// Independent of CommunalStrength — both can be non-zero.
        /// </summary>
        double ExchangeStrength = 0,
        /// <summary>
        /// Accumulated unresolved transgression weight [0–100].
        /// Power-law decay over time; increased by micro-negatives and rejected advances;
        /// reduced by repair attempts (weighted by Lewicki apology components — responsibility &gt; repair offer &gt; regret).
        /// While non-zero reduces effective Trust and Closeness perceived by the actor.
        /// </summary>
        double TransgressionResidue = 0,
        /// <summary>
        /// World-time of the most recent interaction that updated this edge.
        /// Used for Navarro's 8× gap rule: if the gap since last contact exceeds
        /// 8× the expected contact interval, decay rate is multiplied.
        /// </summary>
        WDateTime? LastContactTime = null,

        /// <summary>
        /// Set to <c>true</c> when a <see cref="ContemptuousActPerformed"/> event has been processed.
        /// Contempt is a terminal relationship marker (Gottman 1994): once set, RepairAttempts
        /// can never rebuild Trust or Like above the post-contempt ceiling.
        /// The flag itself does not decay and cannot be cleared.
        /// </summary>
        bool IsContemptuouslyDestroyed = false,

        /// <summary>
        /// How much this character's desire toward B has shifted from spontaneous to responsive.
        /// In long-term communal relationships, spontaneous initiation declines and the person
        /// instead responds to partner's advances (Basson 2001 responsive desire model).
        /// Range [0–100]: 0 = fully spontaneous, 100 = fully responsive.
        /// Grows with CommunalStrength and accumulated interaction history.
        /// </summary>
        double ResponsiveDesireLevel = 0,

        /// <summary>
        /// How dominant (coercion-based status) the observer perceives the target to be.
        /// Range [0–100], neutral 50. Rises when target performs threatening or coercive acts
        /// (ContemptuousAct, witnessed NegativeAct/Betrayal). Drives compliance and avoidance
        /// rather than approach. Cheng et al. (2013, JPSP); Redhead et al. (2019, R. Soc. Open Sci.)
        /// </summary>
        double PerceivedDominance = 50,

        /// <summary>
        /// How prestigious (freely-conferred admiration) the observer perceives the target to be.
        /// Range [0–100], neutral 50. Rises when target performs skilled, generous, or admirable
        /// acts (PositiveAct observed, meaningful SelfDisclosure). Drives voluntary approach and
        /// social copying. Cheng et al. (2013, JPSP); Redhead et al. (2019, R. Soc. Open Sci.)
        /// </summary>
        double PerceivedPrestige = 50,
        /// <summary>
        /// Identifies the family bond type on this directed edge.
        /// Set once by <see cref="GameEngineTools.Characters.Generation.FamilyBuilder"/>
        /// at world-setup time; never mutated by runtime engines.
        /// Defaults to <see cref="KinRole.None"/> for all ordinary social edges.
        /// </summary>
        KinRole KinRole = KinRole.None);
}
