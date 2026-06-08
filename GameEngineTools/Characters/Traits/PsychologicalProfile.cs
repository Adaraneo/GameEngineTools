// PsychologicalProfile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    /// <summary>Dominant stress-coping style of a character.</summary>
    public enum CopingStyle
    {
        /// <summary>Balanced, flexible coping.</summary>
        Balanced,

        /// <summary>Avoidant — disengages from stressors.</summary>
        Avoidant,

        /// <summary>Rationalizing — copes by reframing/justifying.</summary>
        Rationalizing,

        /// <summary>Humor — defuses stress with humour.</summary>
        Humor,

        /// <summary>Aggressive compensation — copes by asserting dominance.</summary>
        AggressiveCompensation,

        /// <summary>People-pleasing — copes by accommodating others.</summary>
        PeoplePleasing
    }

    /// <summary>Identity-relevant self-narrative loadings, each in [0–1].</summary>
    /// <param name="DiligenceIdentity">Degree to which the character sees themselves as diligent.</param>
    /// <param name="ToughnessIdentity">Degree to which the character sees themselves as tough/resilient.</param>
    /// <param name="BelongingIdentity">Degree to which the character sees themselves as socially belonging.</param>
    public sealed record SelfNarrative(
        double DiligenceIdentity,
        double ToughnessIdentity,
        double BelongingIdentity)
    {
        /// <summary>Neutral default narrative (all facets at 0.5).</summary>
        public static SelfNarrative Default { get; } = new(0.5, 0.5, 0.5);
    }

    /// <summary>
    /// Derived psychological trait profile: coping style, self-narrative, ambivalence and
    /// follow-through. Seeded from <see cref="Personality"/> at character creation.
    /// </summary>
    /// <param name="Coping">Dominant coping style.</param>
    /// <param name="Narrative">Identity self-narrative.</param>
    /// <param name="Ambivalence">Tendency toward conflicted motivation, [0–1].</param>
    /// <param name="FollowThrough">Tendency to follow through on intentions, [0–1].</param>
    public sealed record PsychologicalProfile(
        CopingStyle Coping,
        SelfNarrative Narrative,
        double Ambivalence,
        double FollowThrough)
    {
        /// <summary>Neutral default profile.</summary>
        public static PsychologicalProfile Default { get; } = new(
            CopingStyle.Balanced,
            SelfNarrative.Default,
            Ambivalence: 0.2,
            FollowThrough: 0.7);

        /// <summary>Derives a psychological profile from a character's personality traits.</summary>
        /// <param name="personality">Source personality.</param>
        /// <returns>The derived profile.</returns>
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
