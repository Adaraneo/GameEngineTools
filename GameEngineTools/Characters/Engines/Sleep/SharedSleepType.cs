// SharedSleepType.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Sleep
{
    /// <summary>
    /// Type of shared sleep — defines the context in which the character sleeps with someone else.
    /// Affects the risk profile (keeping watch) and narrative opportunities (conversation, shared dream).
    /// </summary>
    public enum SharedSleepType
    {
        /// <summary>
        /// Open-air camp — the companion keeps watch in turns.
        /// Lowers ambush risk (<see cref="SleepConfig.CompanionGuardModifier"/>).
        /// </summary>
        Camp,

        /// <summary>
        /// A shared bed or room — an intimate context.
        /// Enables relationship narrative events (a night conversation, a shared dream).
        /// The watch bonus is lower than for a camp.
        /// </summary>
        Bed,

        /// <summary>
        /// Emergency shelter — sleeping under stress (hiding, fleeing, danger).
        /// Raises stress, shortens the deep phase, with no watch bonus.
        /// </summary>
        Emergency,

        /// <summary>Romantic shared sleep — intimate partners; supports closeness narrative.</summary>
        Romantic,

        /// <summary>Protective shared sleep — one party watches over a vulnerable other.</summary>
        Protective
    }
}
