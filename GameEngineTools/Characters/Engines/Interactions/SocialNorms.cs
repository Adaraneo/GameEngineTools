// SocialNorms.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interactions
{
    /// <summary>
    /// Classifies the type of social norm that governs an interaction surface.
    /// </summary>
    /// <remarks>
    /// Based on Fiske's Relational Models Theory (1992), Bicchieri (2006, 2017),
    /// and Haidt's moral foundations (2012). Each kind maps to a primary emotional
    /// response channel:
    /// <list type="bullet">
    ///   <item><see cref="Greeting"/> and <see cref="PublicConduct"/> — embarrassment channel (lower VAD spike, audience-required).</item>
    ///   <item><see cref="Intimacy"/>, <see cref="RitualContext"/>, <see cref="HarmCare"/>, <see cref="Honesty"/> — shame channel (identity-level threat).</item>
    ///   <item><see cref="Authority"/>, <see cref="Reciprocity"/>, <see cref="FamilyRole"/> — mixed; severity determines channel.</item>
    /// </list>
    /// </remarks>
    public enum SocialNormKind
    {
        /// <summary>
        /// Norms governing greetings and introductions.
        /// Violation triggers embarrassment rather than shame.
        /// Example: ignoring a greeting, or interrupting a formal introduction.
        /// </summary>
        Greeting = 0,

        /// <summary>
        /// Norms governing general public behaviour (noise, dress, spatial etiquette).
        /// Violation triggers embarrassment; audience presence is required.
        /// Example: shouting in a library, inappropriate dress.
        /// </summary>
        PublicConduct,

        /// <summary>
        /// Norms governing physical and emotional intimacy — who, when, and in what context.
        /// Violation triggers shame (identity-level devaluation).
        /// Example: sexual advance in a workplace context, uninvited disclosure.
        /// </summary>
        Intimacy,

        /// <summary>
        /// Norms specific to ritual or mourning contexts (funerals, ceremonies, prayers, weddings).
        /// Strongly restricts positive or playful social acts.
        /// Violation triggers shame and moral outrage in observers.
        /// </summary>
        RitualContext,

        /// <summary>
        /// Norms within kinship structures — duties and expectations between family members.
        /// Maps to Fiske's Communal Sharing + Authority Ranking relational models.
        /// </summary>
        FamilyRole,

        /// <summary>
        /// Norms of fairness and equal exchange (Fiske's Equality Matching).
        /// Violation triggers anger in counterparty, moral outrage in observers.
        /// Example: free-riding, asymmetric turn-taking.
        /// </summary>
        Reciprocity,

        /// <summary>
        /// Norms of deference to status or legitimate authority (Fiske's Authority Ranking).
        /// Violation triggers shame in the subordinate, anger in the authority.
        /// </summary>
        Authority,

        /// <summary>
        /// Norms protecting others from harm — the strongest third-party punishment elicitor.
        /// Violation triggers moral outrage in observers and guilt in the actor.
        /// </summary>
        HarmCare,

        /// <summary>
        /// Norms of truthfulness and non-deception.
        /// Violation triggers guilt (actor) and anger/betrayal (victim).
        /// </summary>
        Honesty
    }

    /// <summary>
    /// Identifies the Fiske (1992) relational model active between the participants.
    /// Used by <see cref="NormViolationMath"/> to weight devaluation severity.
    /// </summary>
    public enum RelationalModel
    {
        /// <summary>Kinship, friendship — unconditional sharing and care.</summary>
        CommunalSharing = 0,

        /// <summary>Hierarchy, leadership — ranked authority and obedience.</summary>
        AuthorityRanking,

        /// <summary>Peers, turn-taking — balanced reciprocity.</summary>
        EqualityMatching,

        /// <summary>Strangers, transactions — cost-benefit exchange.</summary>
        MarketPricing
    }

    /// <summary>
    /// Structured descriptor of the social norm context active on an interaction surface.
    /// Replaces the single-scalar <c>Restrictiveness</c> proposed in the research plan,
    /// based on Sznycer (2016): shame tracks <c>Severity × P(devaluation spreads)</c>.
    /// </summary>
    /// <param name="Kind">
    /// The category of norm that is active. Determines which emotional response channel fires
    /// (embarrassment vs. shame) and how observers react.
    /// </param>
    /// <param name="Severity">
    /// Magnitude of social devaluation if the norm is violated [0..1].
    /// 0 = trivial faux pas; 1 = catastrophic identity-level transgression.
    /// Calibrated to Sznycer et al. (2016): shame intensity r = .67–.79 with devaluation score.
    /// </param>
    /// <param name="EnforcementProbability">
    /// Probability that a violation will be noticed and sanctioned [0..1].
    /// Multiplied with <see cref="Severity"/> to compute the anticipatory shame appraisal score.
    /// Low in private contexts even for high-severity norms.
    /// </param>
    /// <param name="RelationalModel">
    /// Optional Fiske relational model between the interacting parties.
    /// When set, modulates the devaluation weight — CS violations carry heavier shame than MP.
    /// </param>
    public sealed record SocialNormContext(
        SocialNormKind Kind,
        double Severity,
        double EnforcementProbability,
        RelationalModel? RelationalModel = null)
    {
        // ── Factory helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Returns a pre-configured context for a funeral or mourning situation.
        /// High severity, high enforcement — any positive or playful act is strongly inappropriate.
        /// </summary>
        public static SocialNormContext Funeral =>
            new(SocialNormKind.RitualContext, Severity: 0.85, EnforcementProbability: 0.90);

        /// <summary>
        /// Returns a context for a formal workplace setting.
        /// Moderate severity; intimacy and very casual acts are inappropriate.
        /// </summary>
        public static SocialNormContext FormalWork =>
            new(SocialNormKind.Authority, Severity: 0.55, EnforcementProbability: 0.70,
                RelationalModel: Interactions.RelationalModel.AuthorityRanking);

        /// <summary>
        /// Returns a context for a casual social gathering.
        /// Low severity; most acts are acceptable unless they cross intimacy boundaries.
        /// </summary>
        public static SocialNormContext CasualSocial =>
            new(SocialNormKind.PublicConduct, Severity: 0.20, EnforcementProbability: 0.40);
    }
}
