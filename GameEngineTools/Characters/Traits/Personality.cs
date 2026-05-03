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

    public enum Sociosexuality
    { Restricted, Intermediate, Unrestricted }

    public enum Chronotype
    { Lark, Neutral, Owl }
}
