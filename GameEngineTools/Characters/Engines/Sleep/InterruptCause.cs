// InterruptCause.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Sleep
{
    /// <summary>
    /// Příčina přerušení spánku postavy.
    /// Používá se v eventu <see cref="SleepInterrupted"/> pro rozlišení
    /// externích a interních zdrojů přerušení.
    /// </summary>
    public enum InterruptCause
    {
        /// <summary>
        /// Přerušení způsobené hráčem (manuální probuzení).
        /// </summary>
        PlayerForced,

        /// <summary>
        /// Přepadení nebo útok z vnějšku.
        /// Postava se probouzí do bojového stavu.
        /// </summary>
        Ambush,

        /// <summary>
        /// Noční můra — psychologické přerušení z REM fáze.
        /// Postava se budí vystrašená, stres roste.
        /// </summary>
        Nightmare,

        /// <summary>
        /// Hluk nebo rušivý podnět z prostředí (ne útok).
        /// </summary>
        EnvironmentalNoise,

        /// <summary>
        /// Fyzická bolest nebo zdravotní stav.
        /// </summary>
        Pain,

        /// <summary>External interruption (noise, another character, environment event).</summary>
        External
    }
}
