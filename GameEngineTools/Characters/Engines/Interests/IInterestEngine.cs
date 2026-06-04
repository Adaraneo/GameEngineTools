// IInterestEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    #region InterestState

    /// <summary>
    /// Holds the drifting <see cref="Current"/> RIASEC interest profile plus the immutable
    /// <see cref="Baseline"/> seeded at creation.
    /// </summary>
    /// <remarks>
    /// Shares the "prior, not constant" pattern with values (R4). Rewarding experience raises a
    /// matching interest; regression toward <see cref="Baseline"/> is the necessary brake on the
    /// interest → salience → interest positive-feedback loop.
    /// </remarks>
    public sealed record InterestState(InterestProfile Current, InterestProfile Baseline)
    {
        /// <summary>Creates a state where Current equals Baseline.</summary>
        public static InterestState FromBaseline(InterestProfile baseline) => new(baseline, baseline);
    }

    #endregion InterestState

    #region IInterestEngine

    /// <summary>Owns and evolves a character's RIASEC interest profile.</summary>
    public interface IInterestEngine : IEngine<InterestState, InterestConfig>
    {
        /// <summary>Seeds the engine with a generated baseline profile (Current starts equal).</summary>
        void SeedFromBaseline(InterestProfile baseline);
    }

    #endregion IInterestEngine
}
