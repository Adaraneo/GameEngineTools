// GoalConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Goals
{
    /// <summary>
    /// Tuning parameters for the goal engine.
    /// All values bind from <c>Characters:Goals</c> in appsettings.
    /// </summary>
    public sealed record GoalConfig(
        /// <summary>Passive salience decay per game day [0..1]. Default 0.008.</summary>
        double SalienceDecayPerDay = 0.008,

        /// <summary>
        /// Decay multiplier when the character has not acted toward this goal
        /// for more than <see cref="NegligenceThresholdDays"/> days. Default 2.5.
        /// </summary>
        double NegligenceDecayMultiplier = 2.5,

        /// <summary>Days without goal-relevant action before negligence multiplier kicks in. Default 3.</summary>
        double NegligenceThresholdDays = 3.0,

        /// <summary>Salience gain per goal-relevant ActionCommitted [0..1]. Default 0.06.</summary>
        double SalienceGainOnProgress = 0.06,

        /// <summary>Progress gain per strongly relevant action (Work→MasterCraft). Default 0.04.</summary>
        double ProgressGainStrong = 0.04,

        /// <summary>Progress gain per weakly relevant action. Default 0.015.</summary>
        double ProgressGainWeak = 0.015,

        /// <summary>Frustration gain per goal-blocked or rejected interaction. Default 0.12.</summary>
        double FrustrationGainOnBlock = 0.12,

        /// <summary>Frustration decay per day. Default 0.05.</summary>
        double FrustrationDecayPerDay = 0.05,

        /// <summary>Frustration threshold above which Abandoned resolution is triggered. Default 0.85.</summary>
        double AbandonmentFrustrationThreshold = 0.85,

        /// <summary>Salience floor below which Faded resolution is triggered. Default 0.03.</summary>
        double FadedSalienceThreshold = 0.03,

        /// <summary>
        /// Maximum utility flat bias applied per goal to a single candidate.
        /// Keeps goals influential but not dominant. Default 12.0.
        /// </summary>
        double MaxFlatBiasPerGoal = 12.0,

        // ── Personality seeding thresholds ────────────────────────────────────

        /// <summary>Competence motivation threshold to seed MasterCraft. Default 0.72.</summary>
        double MasterCraftCompetenceThreshold = 0.72,

        /// <summary>Affiliation motivation threshold to seed FindPartner. Default 0.70.</summary>
        double FindPartnerAffiliationThreshold = 0.70,

        /// <summary>Openness BigFive threshold to seed FindMeaning. Default 0.75.</summary>
        double FindMeaningOpennessThreshold = 0.75,

        /// <summary>Initial salience for personality-seeded goals. Default 0.25.</summary>
        double PersonalitySeedSalience = 0.25
    )
    {
        /// <summary>Parameterless constructor — all fields use their defaults.</summary>
        public GoalConfig() : this(0.008, 2.5, 3.0, 0.06, 0.04, 0.015, 0.12, 0.05, 0.85, 0.03, 12.0,
                                    0.72, 0.70, 0.75, 0.25) { }
    }
}
