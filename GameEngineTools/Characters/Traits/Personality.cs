// Personality.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    /// <summary>
    /// A character's stable personality trait bundle: Big Five, attachment, communication
    /// style, motivation weights, sociosexuality, chronotype, and the dual-control sexual profile.
    /// Pervasive across all engines.
    /// </summary>
    /// <param name="BigFive">Big Five (OCEAN) trait scores.</param>
    /// <param name="Attachment">Two-dimensional attachment profile (anxiety, avoidance).</param>
    /// <param name="Communication">Communication style.</param>
    /// <param name="Motivation">Motivation weightings across drives.</param>
    /// <param name="Sociosexuality">SOI-R sociosexual orientation.</param>
    /// <param name="Chronotype">Circadian chronotype (lark/neutral/owl).</param>
    public sealed record Personality(
        BigFive BigFive,
        AttachmentProfile Attachment,
        CommunicationStyle Communication,
        MotivationWeights Motivation,
        Sociosexuality Sociosexuality,
        Chronotype Chronotype,
        /// <summary>
        /// Dual Control Model profile (Bancroft &amp; Janssen 2000).
        /// <c>null</c> = population average (SES=0.5, SIS1=0.5, SIS2=0.5) — backward compatible.
        /// </summary>
        SexualResponsiveness? DualControl = null,

        /// <summary>
        /// Per-NPC Theory-of-Mind recursion ceiling (max levels of "I think that you think…").
        /// Population mean ≈ 4, SD ≈ 1 (Kinderman 1998). Default 4 for backward compatibility;
        /// sampled per character by the generator via <see cref="Engines.ToM.ToMMath.GenerateCeiling"/>.
        /// </summary>
        int ToMCeiling = 4,

        /// <summary>
        /// Iowa-Netherlands Comparison Orientation Measure (INCOM; Gibbons &amp; Buunk 1999).
        /// <c>null</c> = engine falls back to inline computation from Neuroticism — backward compatible.
        /// When set, <see cref="ComparisonOrientationProfile.Overall"/> is used directly as the
        /// comparison intensity scalar in the social comparison engine.
        /// </summary>
        ComparisonOrientationProfile? ComparisonOrientation = null,

        /// <summary>
        /// General dark-core factor (D-factor) profile (Moshagen, Hilbig &amp; Zettler 2018).
        /// <c>null</c> = character was generated before this feature; downstream engines treat null
        /// as zero dark-core (backward-compatible no-op). When set,
        /// <see cref="DarkCoreProfile.DarkCore"/> scales antagonistic utility in the behavior engine
        /// and amplifies malicious envy in the social comparison engine.
        /// </summary>
        DarkCoreProfile? DarkCore = null,

        /// <summary>
        /// Per-character hyperboloid temporal-discounting profile (Green &amp; Myerson 2004).
        /// <c>null</c> = character predates this feature; the behavior engine falls back to the
        /// population mean <c>DiscountRateKMean</c> with no per-agent variance (backward-compatible).
        /// When set, <see cref="TemporalDiscountProfile.K"/> drives the
        /// <c>DiscountedValueModifier</c> in the behavior engine.
        /// </summary>
        TemporalDiscountProfile? TemporalDiscount = null,

        /// <summary>
        /// Higgins (1997) regulatory-focus profile (Promotion / Prevention).
        /// <c>null</c> = character predates this feature; both integration points are no-ops
        /// (no λ modulation in loss aversion, no regulatory-fit bonus) — backward-compatible.
        /// When set, <see cref="RegulatoryFocusProfile.Prevention"/> minus
        /// <see cref="RegulatoryFocusProfile.Promotion"/> modulates effective loss-aversion λ.
        /// </summary>
        RegulatoryFocusProfile? RegulatoryFocus = null);


/// <summary>Big Five (OCEAN) personality dimensions, each in [0–1].</summary>
    /// <param name="Openness">Openness to experience.</param>
    /// <param name="Conscientiousness">Conscientiousness.</param>
    /// <param name="Extraversion">Extraversion.</param>
    /// <param name="Agreeableness">Agreeableness.</param>
    /// <param name="Neuroticism">Neuroticism.</param>
    public sealed record BigFive(
        double Openness, double Conscientiousness, double Extraversion, double Agreeableness, double Neuroticism);

    /// <summary>Preferred communication style.</summary>
    public enum CommunicationStyle
    {
        /// <summary>Direct, explicit communication.</summary>
        Direct,

        /// <summary>Indirect, implicit communication.</summary>
        Indirect,

        /// <summary>High-context (meaning carried by context).</summary>
        HighContext,

        /// <summary>Low-context (meaning carried by explicit words).</summary>
        LowContext
    }

    /// <summary>Relative weighting of a character's motivational drives.</summary>
    /// <param name="Affiliation">Drive for social connection.</param>
    /// <param name="Achievement">Drive for accomplishment.</param>
    /// <param name="Power">Drive for influence/control.</param>
    /// <param name="Altruism">Drive to help others.</param>
    /// <param name="Competence">Drive for mastery.</param>
    /// <param name="Autonomy">Drive for independence.</param>
    /// <param name="Curiosity">Drive to explore.</param>
    /// <param name="Rest">Drive for rest/recovery.</param>
    /// <param name="Sexuality">Sexual drive weighting.</param>
    public sealed record MotivationWeights(double Affiliation, double Achievement, double Power, double Altruism, double Competence, double Autonomy, double Curiosity, double Rest, double Sexuality);

    /// <summary>
    /// Three-facet Sociosexual Orientation Inventory (SOI-R; Penke &amp; Asendorpf 2008).
    /// Each facet is independent, allowing nuanced profiles not possible with a single axis:
    /// e.g. high past Behavior + low current Desire (changed person);
    /// or high Attitude + low Behavior (willing but inexperienced).
    /// </summary>
    /// <param name="Behavior">
    /// Past casual sexual behavior frequency [0–1].
    /// Low = few partners / committed history; High = many casual partners.
    /// </param>
    /// <param name="Attitude">
    /// Attitude toward casual sex [0–1].
    /// Low = disapproves / requires emotional connection; High = approves / permissive toward context.
    /// Governs acceptance thresholds and blocking conditions.
    /// </param>
    /// <param name="Desire">
    /// Desire for uncommitted sexual contact [0–1].
    /// Low = little drive toward casual intimacy; High = strong spontaneous drive.
    /// Governs initiative (InviteIntimacy trait bias) and utility multipliers.
    /// </param>
    public sealed record Sociosexuality(double Behavior, double Attitude, double Desire)
    {
        // ── Backward-compatible static presets ──────────────────────────────────
        // All existing call sites using Sociosexuality.Restricted etc. continue to work
        // because these are now static readonly properties of the record type.

        /// <summary>Low on all three facets — committed, context-requiring, low drive.</summary>
        public static readonly Sociosexuality Restricted = new(0.10, 0.10, 0.10);

        /// <summary>Moderate on all three facets — population average.</summary>
        public static readonly Sociosexuality Intermediate = new(0.50, 0.50, 0.50);

        /// <summary>High on all three facets — permissive, context-independent, high drive.</summary>
        public static readonly Sociosexuality Unrestricted = new(0.90, 0.90, 0.90);
    }

    /// <summary>Circadian chronotype.</summary>
    public enum Chronotype
    {
        /// <summary>Morning type (lark).</summary>
        Lark,

        /// <summary>Neither strongly morning nor evening.</summary>
        Neutral,

        /// <summary>Evening type (owl).</summary>
        Owl
    }
}
