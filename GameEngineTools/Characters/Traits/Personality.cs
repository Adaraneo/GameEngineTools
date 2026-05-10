// Personality.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
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
        SexualResponsiveness? DualControl = null);

    public sealed record BigFive(
        double Openness, double Conscientiousness, double Extraversion, double Agreeableness, double Neuroticism);

    public enum CommunicationStyle
    { Direct, Indirect, HighContext, LowContext }

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
        public static readonly Sociosexuality Restricted   = new(0.10, 0.10, 0.10);

        /// <summary>Moderate on all three facets — population average.</summary>
        public static readonly Sociosexuality Intermediate = new(0.50, 0.50, 0.50);

        /// <summary>High on all three facets — permissive, context-independent, high drive.</summary>
        public static readonly Sociosexuality Unrestricted = new(0.90, 0.90, 0.90);
    }

    public enum Chronotype
    { Lark, Neutral, Owl }
}
