// SleepConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Sleep
{
    /// <summary>
    /// Konfigurace spánkového subsystému.
    /// Řídí thresholdy pro prompt, penalizace za odmítnutí,
    /// délky fází a rizikové pravděpodobnosti.
    /// </summary>
    /// <remarks>
    /// Herní den má 26 hodin — parametry jsou kalibrované pro tento rytmus.
    /// Základní délka spánku (<see cref="BaseSleepHours"/>) odpovídá cca 30 % herního dne.
    /// </remarks>
    public sealed record SleepConfig(
        #region Prompt a odmítnutí

        /// <summary>
        /// Minimální hodnota <c>NeedRest</c> (0–100), při které BehaviorEngine
        /// vyšle <see cref="SleepEvents.SleepPromptRequested"/>.
        /// </summary>
        double SleepPromptThreshold,

        /// <summary>
        /// Počet hodin, po které BehaviorEngine počká po odmítnutí spánku
        /// před novým promptem. Každé další odmítnutí periodu zkracuje.
        /// </summary>
        double SleepGraceHours,

        /// <summary>
        /// Maximální počet odmítnutí, po jejichž překročení se grace perioda
        /// přestane zkracovat a penalizace dosáhne maxima.
        /// </summary>
        int MaxDeclineCount,

        /// <summary>
        /// Přírůstek stresu za každou herní hodinu po expiraci grace periody
        /// (po odmítnutém promptu). Roste s počtem odmítnutí.
        /// </summary>
        double DeclinePenaltyStressPerHour,

        #endregion

        #region Délky fází (v herních hodinách)

        /// <summary>
        /// Délka fáze <see cref="SleepPhase.Falling"/> v hodinách.
        /// </summary>
        double FallingDurationHours,

        /// <summary>
        /// Délka jednoho průchodu fází <see cref="SleepPhase.Light"/> v hodinách.
        /// </summary>
        double LightDurationHours,

        /// <summary>
        /// Délka fáze <see cref="SleepPhase.Deep"/> v hodinách.
        /// </summary>
        double DeepDurationHours,

        /// <summary>
        /// Délka fáze <see cref="SleepPhase.Rem"/> v hodinách.
        /// Narrative eventy (sny, noční můry) jsou generovány v průběhu této fáze.
        /// </summary>
        double RemDurationHours,

        #endregion

        #region Rizika

        /// <summary>
        /// Základní pravděpodobnost přepadení za každou hodinu spánku (0–1).
        /// Modifikována fází spánku a přítomností společníka.
        /// </summary>
        double AmbushBaseChancePerHour,

        /// <summary>
        /// Multiplikátor rizika přepadení, pokud spí postava se společníkem v táboře.
        /// Hodnota pod 1.0 snižuje riziko (společník hlídá).
        /// </summary>
        double CompanionGuardModifier,

        /// <summary>
        /// Pravděpodobnost noční můry v průběhu REM fáze, pokud je stres > 50.
        /// </summary>
        double NightmareChanceHighStress,

        /// <summary>
        /// Pravděpodobnost noční můry v průběhu REM fáze při normálním stresu.
        /// </summary>
        double NightmareChanceNormal

        #endregion
    )
    {
        /// <summary>
        /// Výchozí konfigurace kalibrovaná pro 26hodinový herní den.
        /// </summary>
        public SleepConfig() : this(
            SleepPromptThreshold:         70.0,
            SleepGraceHours:              4.0,
            MaxDeclineCount:              3,
            DeclinePenaltyStressPerHour:  2.0,
            FallingDurationHours:         0.25,   // 15 min
            LightDurationHours:           0.75,   // 45 min
            DeepDurationHours:            2.5,    // 2.5 hod
            RemDurationHours:             1.5,    // 1.5 hod
            AmbushBaseChancePerHour:      0.03,
            CompanionGuardModifier:       0.4,
            NightmareChanceHighStress:    0.25,
            NightmareChanceNormal:        0.05
        )
        { }
    }
}
