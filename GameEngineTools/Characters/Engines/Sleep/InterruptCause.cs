// InterruptCause.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Sleep
{
    /// <summary>
    /// Cause of an interruption to a character's sleep.
    /// Used in the <see cref="SleepInterrupted"/> event to distinguish
    /// external and internal sources of interruption.
    /// </summary>
    public enum InterruptCause
    {
        /// <summary>
        /// Interruption caused by the player (manual awakening).
        /// </summary>
        PlayerForced,

        /// <summary>
        /// An ambush or external attack.
        /// The character wakes into a combat state.
        /// </summary>
        Ambush,

        /// <summary>
        /// Nightmare — a psychological interruption from the REM phase.
        /// The character wakes frightened; stress rises.
        /// </summary>
        Nightmare,

        /// <summary>
        /// Noise or a disruptive environmental stimulus (not an attack).
        /// </summary>
        EnvironmentalNoise,

        /// <summary>
        /// Physical pain or a medical condition.
        /// </summary>
        Pain,

        /// <summary>External interruption (noise, another character, environment event).</summary>
        External
    }
}
