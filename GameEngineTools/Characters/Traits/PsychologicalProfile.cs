// PsychologicalProfile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    public enum CopingStyle
    { Balanced, Avoidant, Rationalizing, Humor, AggressiveCompensation, PeoplePleasing }

    public sealed record SelfNarrative(
        double DiligenceIdentity,
        double ToughnessIdentity,
        double BelongingIdentity)
    {
        public static SelfNarrative Default { get; } = new(0.5, 0.5, 0.5);
    }

    public sealed record PsychologicalProfile(
        CopingStyle Coping,
        SelfNarrative Narrative,
        double Ambivalence,
        double FollowThrough)
    {
        public static PsychologicalProfile Default { get; } = new(
            CopingStyle.Balanced,
            SelfNarrative.Default,
            Ambivalence: 0.2,
            FollowThrough: 0.7);

        public static PsychologicalProfile FromPersonality(Personality personality)
        {
            var coping =
                personality.Attachment.Avoidance >= 0.6 ? CopingStyle.Avoidant :
                personality.Attachment.Anxiety >= 0.6 ? CopingStyle.PeoplePleasing :
                personality.BigFive.Agreeableness >= 0.75 ? CopingStyle.PeoplePleasing :
                personality.BigFive.Extraversion >= 0.75 && personality.BigFive.Openness >= 0.6 ? CopingStyle.Humor :
                personality.BigFive.Conscientiousness >= 0.75 ? CopingStyle.Rationalizing :
                personality.BigFive.Neuroticism >= 0.7 && personality.Motivation.Power >= 0.6 ? CopingStyle.AggressiveCompensation :
                CopingStyle.Balanced;

            return new PsychologicalProfile(
                coping,
                new SelfNarrative(
                    DiligenceIdentity: Clamp01(0.3 + personality.BigFive.Conscientiousness * 0.7),
                    ToughnessIdentity: Clamp01(0.2 + (1.0 - personality.BigFive.Neuroticism) * 0.5 + personality.Motivation.Power * 0.3),
                    BelongingIdentity: Clamp01(0.2 + personality.Motivation.Affiliation * 0.5 + personality.BigFive.Agreeableness * 0.3)),
                Ambivalence: Clamp01(0.1 + personality.BigFive.Neuroticism * 0.5 + (1.0 - personality.BigFive.Conscientiousness) * 0.2),
                FollowThrough: Clamp01(0.3 + personality.BigFive.Conscientiousness * 0.5 + (1.0 - personality.BigFive.Neuroticism) * 0.2));
        }

        private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));
    }
}
