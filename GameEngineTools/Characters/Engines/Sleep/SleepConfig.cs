// SleepConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Sleep
{
    /// <summary>
    /// Configuration of the sleep subsystem.
    /// Controls the prompt thresholds, decline penalties,
    /// phase durations and risk probabilities.
    /// </summary>
    /// <remarks>
    /// The game day has 26 hours — the parameters are calibrated for this rhythm.
    /// The base sleep length (<c>BaseSleepHours</c>) corresponds to about 30% of the game day.
    /// </remarks>
    public sealed record SleepConfig(

    #region Prompt a odmítnutí

        /// <summary>
        /// Minimum <c>NeedRest</c> (0–100) at which the BehaviorEngine
        /// emits <see cref="SleepEvents.SleepPromptRequested"/>.
        /// </summary>
        double SleepPromptThreshold,

        /// <summary>
        /// Number of hours the BehaviorEngine waits after a sleep is declined
        /// before a new prompt. Each further decline shortens the period.
        /// </summary>
        double SleepGraceHours,

        /// <summary>
        /// Maximum number of declines after which the grace period
        /// stops shortening and the penalty reaches its maximum.
        /// </summary>
        int MaxDeclineCount,

        /// <summary>
        /// Stress increment per game hour after the grace period expires
        /// (after a declined prompt). Grows with the number of declines.
        /// </summary>
        double DeclinePenaltyStressPerHour,

    #endregion Prompt a odmítnutí

    #region Délky fází (v herních hodinách)

        /// <summary>
        /// Duration of the <see cref="SleepPhase.Falling"/> phase in hours.
        /// </summary>
        double FallingDurationHours,

        /// <summary>
        /// Duration of one pass through the <see cref="SleepPhase.Light"/> phase in hours.
        /// </summary>
        double LightDurationHours,

        /// <summary>
        /// Duration of the <see cref="SleepPhase.Deep"/> phase in hours.
        /// </summary>
        double DeepDurationHours,

        /// <summary>
        /// Duration of the <see cref="SleepPhase.Rem"/> phase in hours.
        /// Narrative events (dreams, nightmares) are generated during this phase.
        /// </summary>
        double RemDurationHours,

    #endregion Délky fází (v herních hodinách)

    #region Rizika

        /// <summary>
        /// Base ambush probability per hour of sleep (0–1).
        /// Modified by the sleep phase and the presence of a companion.
        /// </summary>
        double AmbushBaseChancePerHour,

        /// <summary>
        /// Ambush-risk multiplier when the character sleeps with a companion in a camp.
        /// A value below 1.0 lowers the risk (the companion keeps watch).
        /// </summary>
        double CompanionGuardModifier,

        /// <summary>
        /// Nightmare probability during the REM phase when stress > 50.
        /// </summary>
        double NightmareChanceHighStress,

        /// <summary>
        /// Nightmare probability during the REM phase under normal stress.
        /// </summary>
        double NightmareChanceNormal,

    #endregion Rizika

        double EmergencyNeedRestThreshold,
        double EmergencyEnergyThreshold,
        double ThirstSleepBlockThreshold,
        double HungerSleepBlockThreshold,
        double NightmareStressThreshold,
    /// <summary>
    /// Hunger level above which sleep duration starts shortening.
    /// Biological basis: ghrelin promotes wakefulness when energy stores are low.
    /// Default: 70.
    /// </summary>
    double HungerSleepShorteningThreshold = 70.0,

    /// <summary>
    /// Maximum fractional reduction in sleep duration at Hunger = 100.
    /// 0.5 means sleep is halved at maximum hunger. Default: 0.5.
    /// </summary>
    double HungerSleepShorteningMax = 0.5
    )
    {
        /// <summary>
        /// Default configuration calibrated for the 26-hour game day.
        /// </summary>
        public SleepConfig() : this(
            SleepPromptThreshold: 70.0,
            SleepGraceHours: 4.0,
            MaxDeclineCount: 3,
            DeclinePenaltyStressPerHour: 2.0,
            FallingDurationHours: 0.25,   // 15 min
            LightDurationHours: 0.75,   // 45 min
            DeepDurationHours: 2.5,    // 2.5 hod
            RemDurationHours: 1.5,    // 1.5 hod
            AmbushBaseChancePerHour: 0.03,
            CompanionGuardModifier: 0.4,
            NightmareChanceHighStress: 0.25,
            NightmareChanceNormal: 0.05,
            EmergencyNeedRestThreshold: 90.0,
            EmergencyEnergyThreshold: 5.0,
            ThirstSleepBlockThreshold: 80.0,
            HungerSleepBlockThreshold: 80.0,
            NightmareStressThreshold: 70.0
        )
        { }
    }
}
