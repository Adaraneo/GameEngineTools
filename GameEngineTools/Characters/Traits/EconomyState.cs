// EconomyState.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    /// <summary>
    /// Immutable snapshot of a character's economic standing: current wealth in a single abstract
    /// currency unit ("coin"). Food-economy Tier 2.
    /// </summary>
    /// <remarks>
    /// Money is modeled as a single scalar, not a <c>WorldObject</c> — Kiyotaki &amp; Wright (1989,
    /// <i>JPE</i> 97(4):927-954) show a single accepted medium resolves the
    /// double-coincidence-of-wants problem; there is no in-simulation need for money to be a
    /// pickable/tradeable object with its own inventory slot. See
    /// <c>food-economy-tier2-implementation-plan.md</c> §2.1.
    /// </remarks>
    /// <param name="Wealth">
    /// Current wealth in abstract currency units. Never negative — Buy/Sell gating enforces this.
    /// </param>
    public sealed record EconomyState(double Wealth)
    {
        /// <summary>A character with no money — the seed for anyone lacking economic state.</summary>
        public static readonly EconomyState Zero = new(0.0);
    }
}
