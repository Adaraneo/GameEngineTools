// InterestConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interests
{
    /// <summary>
    /// Tuning parameters for the interest drift engine.
    /// All values bind from <c>Characters:Interests</c> in appsettings.
    /// </summary>
    public sealed record InterestConfig(
        /// <summary>Interest gain on one reinforcement, scaled by headroom (1 − interest). Default 0.0003.</summary>
        double LearningRate = 0.0003,

        /// <summary>Per-day lerp fraction pulling Current toward Baseline (the runaway brake). Default 0.0015.</summary>
        double RegressionPerDay = 0.0015,

        /// <summary>Valence above which an action counts as rewarding; baseline mood is already +0.1..+0.2, so it must be clearly higher. Default 0.35.</summary>
        double RewardValenceThreshold = 0.35,

        /// <summary>
        /// Age below which interests are plastic (Low 2005: stability rises sharply at 18–22). Default 22.
        /// </summary>
        int PlasticityAgeMax = 22,

        /// <summary>Learning-rate multiplier while interests are plastic (under PlasticityAgeMax). Default 1.5.</summary>
        double YoungLearningMultiplier = 1.5,

        /// <summary>Regression-rate multiplier while plastic — drift sticks more. Default 0.5.</summary>
        double YoungRegressionMultiplier = 0.5,

        /// <summary>Minimum world days between two reinforcements of the same dimension. Default 1.</summary>
        double ReinforcementCooldownDays = 1.0
    )
    {
        /// <summary>Parameterless constructor — all fields use their defaults.</summary>
        public InterestConfig() : this(0.0003, 0.0015, 0.35, 22, 1.5, 0.5, 1.0)
        { }
    }
}
