// NormViolationMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interactions
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Pure-static math for social norm violation detection, shame spike computation,
    /// and observer cascade routing.
    /// </summary>
    /// <remarks>
    /// Theoretical foundations:
    /// <list type="bullet">
    ///   <item>Sznycer, Tooby &amp; Cosmides (2016) — shame tracks audience-predicted devaluation.</item>
    ///   <item>Muris, Meesters &amp; van Asseldonk (2018) — Big Five × shame-proneness correlations.</item>
    ///   <item>Lickel, Schmader et al. (2005) — vicarious shame in in-group observers.</item>
    ///   <item>Hartsough, Ginther &amp; Marois (2020) — moral outrage in third-party observers.</item>
    ///   <item>Bicchieri (2017) — norm enforcement probability as a behavioral force component.</item>
    /// </list>
    /// </remarks>
    internal static class NormViolationMath
    {
        #region Violation score

        /// <summary>
        /// Computes the anticipatory norm violation score for an action on a given surface.
        /// </summary>
        /// <remarks>
        /// Formula: <c>Score = Severity × EnforcementProbability × AudienceFactor</c>
        /// where <c>AudienceFactor = 1.0</c> (private) to <c>1.4</c> (crowded public space).
        /// This reflects Sznycer (2016): shame intensity tracks devaluation × P(information spreads).
        /// </remarks>
        /// <param name="normContext">The active norm context on the interaction surface.</param>
        /// <param name="hasPrivacy">Whether the surface offers privacy.</param>
        /// <param name="observers">Number of observers present. Drives AudienceFactor.</param>
        /// <returns>Violation score in [0..1].</returns>
        internal static double ComputeViolationScore(
            SocialNormContext normContext,
            bool hasPrivacy,
            int observers)
        {
            // Audience factor: private = 0.6 (shame can still be self-directed),
            // public with observers scales up to 1.4 (Sznycer 2016 cross-cultural r = .69–.79).
            var audienceFactor = hasPrivacy
                ? 0.6
                : Math.Min(1.4, 0.9 + observers * 0.05);

            return Math.Clamp(
                normContext.Severity * normContext.EnforcementProbability * audienceFactor,
                0.0, 1.0);
        }

        #endregion Violation score

        #region Acceptance penalty

        /// <summary>
        /// Returns the penalty factor applied to the recipient's acceptance probability
        /// when the proposed action violates an active norm on the surface.
        /// </summary>
        /// <remarks>
        /// Applied as a multiplicative penalty: <c>baseP *= (1 - Penalty)</c>.
        /// A funeral-context ReachOut (score ≈ 0.77) reduces baseP by ~62%.
        /// </remarks>
        /// <param name="violationScore">Pre-computed violation score [0..1].</param>
        /// <returns>Penalty factor in [0..0.85].</returns>
        internal static double AcceptancePenalty(double violationScore)
            => Math.Clamp(violationScore * 0.80, 0.0, 0.85);

        #endregion Acceptance penalty

        #region Shame spike

        /// <summary>
        /// Computes the Valence, Arousal, and Dominance delta for a norm-violation shame spike.
        /// </summary>
        /// <remarks>
        /// VAD signature based on Singh &amp; Bhushan (2025, <i>Frontiers in Psychology</i> 16:1678930):
        /// shame exhibits variable arousal — high in socially evaluative contexts (observers present),
        /// lower in private contexts (withdrawal/defeat response).
        /// <para>
        /// Personality calibration (Muris, Meesters &amp; van Asseldonk, 2018,
        /// <i>Child Psychiatry &amp; Human Development</i> 49:268):
        /// <list type="bullet">
        ///   <item>Neuroticism: r = .39–.45 → gain α ≈ 0.70.</item>
        ///   <item>Extraversion: r = −.25 to −.32 → damping α ≈ 0.35.</item>
        ///   <item>Conscientiousness and Agreeableness: consistent for guilt, NOT shame → ignored here.</item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <param name="violationScore">Pre-computed violation score [0..1].</param>
        /// <param name="hasAudience">True when observers are present — amplifies arousal.</param>
        /// <param name="personality">Actor's personality, used to scale the spike.</param>
        /// <returns>Tuple of (deltaValence, deltaArousal, deltaDominance) to apply.</returns>
        internal static (double DeltaValence, double DeltaArousal, double DeltaDominance) ComputeShameSpike(
            double violationScore,
            bool hasAudience,
            Personality personality)
        {
            var n = personality.BigFive.Neuroticism;
            var e = personality.BigFive.Extraversion;

            // Personality multiplier — calibrated to published effect sizes.
            // Neuroticism is the dominant predictor; Extraversion damps the response.
            var personalityMult = 1.0
                + 0.70 * (n - 0.5)   // Neuroticism gain (r = .39–.45)
                - 0.35 * (e - 0.5);  // Extraversion damping (r = −.25 to −.32)

            personalityMult = Math.Clamp(personalityMult, 0.30, 2.20);

            // Base VAD deltas from Singh & Bhushan (2025) shame profile.
            var baseValence = -0.55 * violationScore;
            var baseDominance = -0.65 * violationScore;

            // Arousal is context-dependent: high with audience (socially evaluative),
            // low without (defeat/withdrawal mode).
            var baseArousal = hasAudience
                ? 0.55 * violationScore   // Dickerson et al. (2004): social-eval context
                : 0.20 * violationScore;  // Gruenewald et al. (2004): defeat/withdrawal

            return (
                DeltaValence: Math.Clamp(baseValence * personalityMult, -0.85, 0.0),
                DeltaArousal: Math.Clamp(baseArousal * personalityMult, 0.0, 0.80),
                DeltaDominance: Math.Clamp(baseDominance * personalityMult, -0.85, 0.0));
        }

        #endregion Shame spike

        #region Observer reaction routing

        /// <summary>
        /// Determines the type of emotional reaction an observer will have upon witnessing
        /// a norm violation.
        /// </summary>
        /// <remarks>
        /// Routing logic based on:
        /// <list type="bullet">
        ///   <item>Hartsough, Ginther &amp; Marois (2020): moral outrage &gt; anger for third-party violations.</item>
        ///   <item>Lickel, Schmader, Curtis, Scarnier &amp; Ames (2005): vicarious shame requires shared identity with the actor.</item>
        /// </list>
        /// When in-group identity cannot be determined, <see cref="ObserverReactionKind.MoralOutrage"/>
        /// is the safe fallback.
        /// </remarks>
        /// <param name="observer">The observer's ID.</param>
        /// <param name="actor">The actor's ID.</param>
        /// <param name="victim">The direct victim of the act, if any.</param>
        /// <param name="sharesIdentityWithActor">
        /// True if the observer shares a group/family identity with the actor.
        /// When unknown, pass <c>false</c> (defaults to MoralOutrage).
        /// </param>
        /// <returns>The observer's reaction kind.</returns>
        internal static ObserverReactionKind RouteObserverReaction(
            HumanId observer,
            HumanId actor,
            HumanId? victim,
            bool sharesIdentityWithActor)
        {
            // Direct victim → anger (second-party reaction).
            if (victim.HasValue && observer == victim.Value)
                return ObserverReactionKind.Anger;

            // In-group observer → vicarious shame (Lickel et al. 2005).
            if (sharesIdentityWithActor)
                return ObserverReactionKind.VicariousShame;

            // Everyone else → moral outrage (third-party; Hartsough et al. 2020).
            return ObserverReactionKind.MoralOutrage;
        }

        #endregion Observer reaction routing

        #region Norm kind channel

        /// <summary>
        /// Returns true when the given norm kind triggers the full shame channel
        /// (identity-level devaluation, Sznycer 2016).
        /// Returns false for the embarrassment channel (convention violation, audience-required).
        /// </summary>
        /// <remarks>
        /// Embarrassment channel: <see cref="SocialNormKind.Greeting"/>, <see cref="SocialNormKind.PublicConduct"/>.
        /// Shame channel: all others.
        /// Based on Keltner &amp; Buswell (1996) and Piretti et al. (2023, <i>Brain Sciences</i> 13:559).
        /// </remarks>
        internal static bool IsShameChannel(SocialNormKind kind) => kind switch
        {
            SocialNormKind.Greeting => false, // embarrassment
            SocialNormKind.PublicConduct => false, // embarrassment
            _ => true   // shame
        };

        #endregion Norm kind channel
    }
}
