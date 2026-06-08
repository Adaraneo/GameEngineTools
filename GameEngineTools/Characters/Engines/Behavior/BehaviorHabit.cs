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
        /// <summary>Night-time band.</summary>
        Night,
        /// <summary>Morning band.</summary>
        Morning,
        /// <summary>Daytime band.</summary>
        Day,
        /// <summary>Evening band.</summary>
        Evening
    }

    /// <summary>
    /// Dominant cue under which an action tends to be repeated.
    /// </summary>
    public enum HabitCueKind
    {
        /// <summary>No dominant cue.</summary>
        Neutral,
        /// <summary>Driven by a bodily need (hunger, thirst, fatigue).</summary>
        BodyNeed,
        /// <summary>Driven by stress relief.</summary>
        StressRelief,
        /// <summary>Driven by a social need.</summary>
        SocialNeed,
        /// <summary>Driven by a competence need.</summary>
        CompetenceNeed
    }

    /// <summary>
    /// Interpretable habit tendency derived from reinforcement history.
    /// </summary>
    public enum HabitTendency
    {
        /// <summary>No clear tendency.</summary>
        Neutral,
        /// <summary>Adaptive — reinforced by need-appropriate outcomes.</summary>
        Adaptive,
        /// <summary>Maladaptive coping — reinforced primarily by stress relief.</summary>
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

    /// <summary>
    /// Compact credit-assignment signal used to learn from committed behavior without full reward learning.
    /// </summary>
    internal sealed record HabitLearningSignal(
        string ActionName,
        SurfaceKind SurfaceKind,
        HabitTimeBand TimeBand,
        HabitCueKind CueKind,
        double CueFit,
        double ReliefFit,
        double CopingFit,
        double ConstraintPenalty,
        WDateTime OccurredAt);

    /// <summary>
    /// Optional extension point for future memory or intent layers to adjust habit applicability.
    /// </summary>
    internal interface IHabitApplicabilityModulator
    {
        double ModulateApplicability(
            BehaviorContext context,
            BehaviorCandidate candidate,
            BehaviorHabitTrace trace,
            double baseApplicability);
    }

    /// <summary>
    /// Default habit applicability modulator. Preserves baseline behavior.
    /// </summary>
    internal sealed class NoOpHabitApplicabilityModulator : IHabitApplicabilityModulator
    {
        public static NoOpHabitApplicabilityModulator Instance { get; } = new();

        private NoOpHabitApplicabilityModulator()
        {
        }

        public double ModulateApplicability(
            BehaviorContext context,
            BehaviorCandidate candidate,
            BehaviorHabitTrace trace,
            double baseApplicability)
            => baseApplicability;
    }
}
