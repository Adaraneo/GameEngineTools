// SleepPhase.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Sleep
{
    /// <summary>
    /// Phase of the character's sleep cycle.
    /// Each phase has a different risk profile and different narrative opportunities.
    /// </summary>
    /// <remarks>
    /// Phase progression: <see cref="Falling"/> → <see cref="Light"/> → <see cref="Deep"/> → <see cref="Rem"/> → <see cref="Light"/> → ...
    /// The character always wakes from the <see cref="Light"/> phase.
    /// </remarks>
    public enum SleepPhase
    {
        /// <summary>
        /// Falling asleep — the transition between wakefulness and sleep.
        /// The character is easily interrupted; no dreams or recovery occur.
        /// </summary>
        Falling,

        /// <summary>
        /// Light sleep — shallow, interruptible (noise, attack).
        /// Energy recovery proceeds slowly.
        /// </summary>
        Light,

        /// <summary>
        /// Deep sleep — hard to interrupt, with maximum physical recovery.
        /// Ambush risk is low (the character hears less), but waking is harder.
        /// </summary>
        Deep,

        /// <summary>
        /// REM phase — the phase of dreams and psychological recovery.
        /// Narrative events (dreams, nightmares) are generated here.
        /// Physical ambush risk is lowest; psychological risk (nightmares) is highest.
        /// </summary>
        Rem,

        /// <summary>
        /// Waking — the transition from sleep to wakefulness.
        /// The character is not yet fully capable of acting.
        /// </summary>
        Waking
    }
}
