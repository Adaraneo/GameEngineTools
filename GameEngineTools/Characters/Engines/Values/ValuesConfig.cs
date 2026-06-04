// ValuesConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Values
{
    /// <summary>
    /// Tuning parameters for the values drift engine.
    /// All values bind from <c>Characters:Values</c> in appsettings.
    /// </summary>
    /// <remarks>
    /// Calibration anchors (skill ref §1 · human-behavior-npc):
    /// <list type="bullet">
    ///   <item><see cref="LearningRate"/> — per-significant-behaviour nudge magnitude.</item>
    ///   <item><see cref="RegressionPerDay"/> — slow pull back to baseline.</item>
    ///   <item><see cref="ExternalIncentiveDiscount"/> — Kelley discounting: externally-caused
    ///   acts reinforce internal values less.</item>
    /// </list>
    /// </remarks>
    public sealed record ValuesConfig(
        /// <summary>
        /// Drift magnitude applied to a value per significant value-relevant action [0..1].
        /// Default 0.02.
        /// </summary>
        double LearningRate = 0.02,

        /// <summary>
        /// Per-day lerp fraction pulling <c>Current</c> toward <c>Baseline</c>.
        /// Default 0.00023 → ~8 %/year regression, matching Vecchione (2016) 4-year rank-order
        /// stability r ≈ 0.69. (Note: the implementation plan tabled 0.004, which is internally
        /// inconsistent with its own stated "~8 %/rok" anchor and the "values are stable" premise;
        /// the scientific 8 %/year outcome was chosen.)
        /// </summary>
        double RegressionPerDay = 0.00023,

        /// <summary>
        /// Multiplier on the learning rate when the action had a sufficient external cause
        /// (Kelley discounting). Default 0.4.
        /// </summary>
        double ExternalIncentiveDiscount = 0.4,

        /// <summary>
        /// Learning-rate multiplier during the adolescent identity window (Teenager stage).
        /// Adolescence shows elevated value plasticity. Default 2.5 (range 2–3).
        /// </summary>
        double AdolescentLearningMultiplier = 2.5,

        /// <summary>
        /// Regression-rate multiplier during the adolescent window — drift sticks more
        /// (less pull-back) while identity is forming. Default 0.5.
        /// </summary>
        double AdolescentRegressionMultiplier = 0.5,

        /// <summary>
        /// Fraction of a value nudge applied (with opposite sign) to the circumplex opposite pole
        /// (Bardi 2009 — values change in coordinated, structure-preserving ways). Default 0.5.
        /// </summary>
        double OppositeCouplingFactor = 0.5,

        /// <summary>
        /// Fraction of a value nudge applied (same sign) to the two circumplex neighbours.
        /// Default 0.25.
        /// </summary>
        double NeighborCouplingFactor = 0.25,

        /// <summary>
        /// Congruence above which a committed action is treated as value-affirming and strengthens
        /// the dominant expressed value. Default 0.0.
        /// </summary>
        double PositiveCongruenceThreshold = 0.0
    )
    {
        /// <summary>Parameterless constructor — all fields use their defaults.</summary>
        public ValuesConfig() : this(0.02, 0.00023, 0.4, 2.5, 0.5, 0.5, 0.25, 0.0)
        { }
    }
}
