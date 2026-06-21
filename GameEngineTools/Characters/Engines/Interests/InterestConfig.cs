// InterestConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interests
{
    /// <summary>
    /// Tuning parameters for the interest drift engine.
    /// All values bind from <c>Characters:Interests</c> in appsettings.
    /// </summary>
    public sealed record InterestConfig(
        /// <summary>Interest gain on a rewarding domain-relevant action [0..1]. Default 0.03.</summary>
        double LearningRate = 0.03,

        /// <summary>Per-day lerp fraction pulling Current toward Baseline (the runaway brake). Default 0.0015.</summary>
        double RegressionPerDay = 0.0015,

        /// <summary>
        /// Psychology valence above which an action counts as "rewarding" and raises its RIASEC
        /// dimension. Default 0.35.
        /// </summary>
        /// <remarks>
        /// The documented baseline PAD valence is already +0.1..+0.2 (a contented, neutral mood),
        /// so a threshold at or below that band makes <i>every</i> routine action "rewarding" and
        /// saturates the most-frequently-mapped dimension (Social, via ReachOut/InviteIntimacy)
        /// while the rarer ones stay at baseline. 0.35 requires a clearly above-baseline positive
        /// moment, not merely the absence of bad mood.
        /// </remarks>
        double RewardValenceThreshold = 0.35,

        /// <summary>
        /// Age below which interests are plastic (Low 2005: stabilise ~22). Default 22.
        /// </summary>
        int PlasticityAgeMax = 22,

        /// <summary>Learning-rate multiplier while interests are plastic (under PlasticityAgeMax). Default 1.5.</summary>
        double YoungLearningMultiplier = 1.5,

        /// <summary>Regression-rate multiplier while plastic — drift sticks more. Default 0.5.</summary>
        double YoungRegressionMultiplier = 0.5
    )
    {
        /// <summary>Parameterless constructor — all fields use their defaults.</summary>
        public InterestConfig() : this(0.03, 0.0015, 0.35, 22, 1.5, 0.5)
        { }
    }
}
