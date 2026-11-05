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
    /// <param name="Current">The drifting profile.</param>
    /// <param name="Baseline">The profile seeded at creation that Current regresses toward.</param>
    /// <param name="LastReinforcedTicks">World ticks of the last reinforcement per RIASEC dimension (−1 = never); drives the per-day cooldown.</param>
    /// <param name="LastWorkFitTicks">World ticks of the last <see cref="WorkFitFelt"/>, for its per-day cooldown.</param>
    public sealed record InterestState(InterestProfile Current, InterestProfile Baseline, long[]? LastReinforcedTicks = null, long? LastWorkFitTicks = null)
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
