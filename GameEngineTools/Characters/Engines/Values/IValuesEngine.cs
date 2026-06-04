// IValuesEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Values
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    #region ValuesState

    /// <summary>
    /// Mutable motivational state holding both the character's <b>current</b> (drifting)
    /// Schwartz value profile and the immutable <b>baseline</b> seeded from BigFive at creation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Prior, not constant": <see cref="Baseline"/> is generated once by
    /// <see cref="ValuesProfileGenerator"/> and never changes. <see cref="Current"/> starts equal
    /// to <see cref="Baseline"/>, drifts from lived experience (value-congruent / value-violating
    /// action), and slowly regresses back toward <see cref="Baseline"/> in the absence of
    /// reinforcement (Vecchione 2016: 4-year rank-order stability r ≈ 0.69).
    /// </para>
    /// <para>
    /// <see cref="ValuesBehaviorModifier"/> and the Guilt channel read <see cref="Current"/>,
    /// never <see cref="Baseline"/> — so morality is keyed to who the character has become,
    /// not who they were born to be.
    /// </para>
    /// </remarks>
    public sealed record ValuesState(
        ValuesProfile Current,
        ValuesProfile Baseline)
    {
        /// <summary>Creates a state where Current equals Baseline (freshly seeded character).</summary>
        public static ValuesState FromBaseline(ValuesProfile baseline) => new(baseline, baseline);
    }

    #endregion ValuesState

    #region IValuesEngine

    /// <summary>
    /// Owns and evolves a character's Schwartz value profile over time.
    /// </summary>
    /// <remarks>
    /// Architectural twin of <c>DefaultGoalEngine</c>: seed from a personality-derived prior,
    /// then per-tick regression + event-driven update. Nothing here re-derives values from
    /// BigFive after creation — drift is driven purely by lived experience.
    /// </remarks>
    public interface IValuesEngine : IEngine<ValuesState, ValuesConfig>
    {
        /// <summary>
        /// Seeds the engine with a generated baseline profile. <see cref="ValuesState.Current"/>
        /// starts identical to the baseline. Call once after factory construction.
        /// </summary>
        void SeedFromBaseline(ValuesProfile baseline);
    }

    #endregion IValuesEngine
}
