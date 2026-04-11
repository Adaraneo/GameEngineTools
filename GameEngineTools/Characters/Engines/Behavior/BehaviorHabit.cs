// BehaviorHabit.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Coarse context band used to keep habit learning cue-bound without overfitting to exact timestamps.
    /// </summary>
    public enum HabitTimeBand
    {
        Night,
        Morning,
        Day,
        Evening
    }

    /// <summary>
    /// Dominant cue under which an action tends to be repeated.
    /// </summary>
    public enum HabitCueKind
    {
        Neutral,
        BodyNeed,
        StressRelief,
        SocialNeed,
        CompetenceNeed
    }

    /// <summary>
    /// Interpretable habit tendency derived from reinforcement history.
    /// </summary>
    public enum HabitTendency
    {
        Neutral,
        Adaptive,
        MaladaptiveCoping
    }

    /// <summary>
    /// Persistent learned action tendency for one broad cue and environment context.
    /// </summary>
    public sealed record BehaviorHabitTrace(
        string ActionName,
        SurfaceKind SurfaceKind,
        HabitTimeBand TimeBand,
        HabitCueKind CueKind,
        double Strength,
        double AdaptiveReinforcement,
        double CopingReinforcement,
        int RepetitionCount,
        WDateTime LastUpdatedAt,
        HabitTendency Tendency);
}
