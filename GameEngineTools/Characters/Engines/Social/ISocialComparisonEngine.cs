// ISocialComparisonEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Social
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    #region Enums

    /// <summary>Whether a comparison standard stands above or below the comparer.</summary>
    public enum ComparisonDirection
    {
        /// <summary>No meaningful comparison occurred (standings too close, or no target).</summary>
        None,

        /// <summary>The comparison target stands above the self (more competent / higher status).</summary>
        Upward,

        /// <summary>The comparison target stands below the self.</summary>
        Downward
    }

    /// <summary>
    /// How the self-evaluation moved relative to the standard. <b>Contrast is the default</b>
    /// (self-evaluation moves <i>away</i> from the standard); assimilation (toward the standard) is
    /// the exception, requiring attainability + identification (Gerber, Wheeler &amp; Suls 2018).
    /// </summary>
    public enum ComparisonReaction
    {
        /// <summary>Self-evaluation moved away from the standard — the dominant response.</summary>
        Contrast,

        /// <summary>Self-evaluation moved toward the standard — only under attainability + identification.</summary>
        Assimilation
    }

    /// <summary>
    /// Envy bifurcation following an upward comparison (van de Ven et al.; Meier &amp; Schäfer 2018):
    /// benign envy fuels self-improvement, malicious envy fuels hostility toward the target.
    /// </summary>
    public enum ComparisonEnvy
    {
        /// <summary>No envy component.</summary>
        None,

        /// <summary>Attainable upward standard → inspiration, approach, achievement motivation.</summary>
        Benign,

        /// <summary>Unattainable upward standard + low agreeableness → hostility / desire to pull down.</summary>
        Malicious
    }

    #endregion Enums

    #region SocialComparisonState

    /// <summary>
    /// Minimal persistent state for the social comparison engine: the time of the last comparison,
    /// used to throttle comparison to a reflective cadence rather than every tick.
    /// </summary>
    public sealed record SocialComparisonState(WDateTime? LastComparisonAt = null);

    #endregion SocialComparisonState

    #region SocialComparisonOccurred

    /// <summary>
    /// Emitted when a character compares themselves against a known peer. Carries the deltas that
    /// downstream engines apply: <see cref="SelfConcept"/> (self-esteem), <see cref="Psychology"/>
    /// (mood + achievement motivation), and <see cref="Relationships"/> (malicious-envy hostility).
    /// </summary>
    /// <param name="Human">The comparer.</param>
    /// <param name="Target">The peer used as the comparison standard.</param>
    /// <param name="Direction">Upward or downward.</param>
    /// <param name="Reaction">Contrast (default) or assimilation.</param>
    /// <param name="Envy">None / benign / malicious.</param>
    /// <param name="SelfEsteemDelta">Change to global self-esteem [−1..+1 scale of SelfConcept.SelfEsteem].</param>
    /// <param name="MoodValenceDelta">Immediate PAD valence change [−1..+1].</param>
    /// <param name="MoodBaselineDelta">Persistent mood baseline change [0..100 scale].</param>
    /// <param name="AchievementMotivationDelta">Change to NeedAchievement [0..100 scale] (benign envy).</param>
    /// <param name="TargetHostilityDelta">Hostility magnitude toward the target (malicious envy); 0 otherwise.</param>
    public sealed record SocialComparisonOccurred(
        WDateTime OccurredAt,
        HumanId Human,
        HumanId Target,
        ComparisonDirection Direction,
        ComparisonReaction Reaction,
        ComparisonEnvy Envy,
        double SelfEsteemDelta,
        double MoodValenceDelta,
        double MoodBaselineDelta,
        double AchievementMotivationDelta,
        double TargetHostilityDelta) : IDomainEvent;

    #endregion SocialComparisonOccurred

    #region ISocialComparisonEngine

    /// <summary>
    /// Owns a character's social comparison process: selecting a reference peer, evaluating
    /// contrast/assimilation against them, and emitting <see cref="SocialComparisonOccurred"/>
    /// deltas for self-concept, psychology, and relationships to consume next tick.
    /// </summary>
    public interface ISocialComparisonEngine : IEngine<SocialComparisonState, SocialComparisonConfig>
    {
    }

    #endregion ISocialComparisonEngine
}
