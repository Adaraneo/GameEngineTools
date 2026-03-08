// Personality.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{

    public sealed record Personality(
        BigFive BigFive,
        AttachmentStyle Attachment,
        CommunicationStyle Communication,
        MotivationWeights Motivation,
        Sociosexuality Sociosexuality,
        Chronotype Chronotype);

    public sealed record BigFive(
        double Openness, double Conscientiousness, double Extraversion, double Agreeableness, double Neuroticism);

    public enum AttachmentStyle { Secure, Anxious, Avoidant, Disorganized }
    public enum CommunicationStyle { Direct, Indirect, HighContext, LowContext }
    public sealed record MotivationWeights(double Affiliation, double Achievement, double Power, double Altruism, double Competence, double Autonomy, double Curiosity, double Rest, double Sexuality);
    public enum Sociosexuality { Restricted, Intermediate, Unrestricted }
    public enum Chronotype { Lark, Neutral, Owl }
}
