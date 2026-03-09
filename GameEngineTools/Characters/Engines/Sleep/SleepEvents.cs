// SleepEvents.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Sleep
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    #region Prompt a potvrzení

    /// <summary>
    /// BehaviorEngine vyšle tento event, když <c>NeedRest</c> překročí threshold.
    /// <br/>
    /// — Pro NPC: systém okamžitě odpoví <see cref="SleepConfirmed"/>.<br/>
    /// — Pro PC: UI zobrazí prompt, hráč potvrdí nebo odmítne.
    /// </summary>
    /// <param name="OccurredAt">Herní čas vyslání promptu.</param>
    /// <param name="Human">Identifikátor postavy.</param>
    /// <param name="SleepNeed">Aktuální hodnota potřeby spánku (0–100) v okamžiku promptu.</param>
    public sealed record SleepPromptRequested(
        WDateTime OccurredAt,
        HumanId Human,
        double SleepNeed) : IDomainEvent;

    /// <summary>
    /// Potvrzení zahájení spánku — přichází od systému (NPC) nebo od UI (PC).
    /// Spustí vytvoření <see cref="ISleepSession"/>.
    /// </summary>
    /// <param name="OccurredAt">Herní čas potvrzení.</param>
    /// <param name="Human">Identifikátor postavy.</param>
    /// <param name="PlannedWakeUp">Plánovaný čas probuzení. Může být přerušen dřív.</param>
    /// <param name="Companion">Volitelný společník sdíleného spánku.</param>
    /// <param name="SharedType">Typ sdíleného spánku, nebo <c>null</c> pokud spí postava sama.</param>
    public sealed record SleepConfirmed(
        WDateTime OccurredAt,
        HumanId Human,
        WDateTime PlannedWakeUp,
        HumanId? Companion = null,
        SharedSleepType? SharedType = null) : IDomainEvent;

    /// <summary>
    /// Hráč odmítl prompt spánku.
    /// BehaviorEngine spustí grace periodu a poté znovu vyšle <see cref="SleepPromptRequested"/>.
    /// </summary>
    /// <param name="OccurredAt">Herní čas odmítnutí.</param>
    /// <param name="Human">Identifikátor postavy.</param>
    /// <param name="DeclineCount">Kolikátý odmítnutí v sérii (resetuje se po spánku).</param>
    public sealed record SleepDeclined(
        WDateTime OccurredAt,
        HumanId Human,
        int DeclineCount) : IDomainEvent;

    #endregion

    #region Průběh spánku

    /// <summary>
    /// Postava vstoupila do nové fáze spánkového cyklu.
    /// Publikováno <see cref="ISleepSession"/> při každém přechodu fáze.
    /// </summary>
    /// <param name="OccurredAt">Herní čas přechodu.</param>
    /// <param name="Human">Identifikátor postavy.</param>
    /// <param name="Phase">Nová aktuální fáze.</param>
    public sealed record SleepPhaseChanged(
        WDateTime OccurredAt,
        HumanId Human,
        SleepPhase Phase) : IDomainEvent;

    /// <summary>
    /// V průběhu REM fáze se postava zdá — narrative hook pro hru.
    /// Obsah snu (vzpomínky, události, předtuchy) je definován herní vrstvou.
    /// </summary>
    /// <param name="OccurredAt">Herní čas snu.</param>
    /// <param name="Human">Identifikátor postavy.</param>
    /// <param name="DreamSeed">Seed pro generátor obsahu snu (deterministický).</param>
    public sealed record DreamOccurred(
        WDateTime OccurredAt,
        HumanId Human,
        int DreamSeed) : IDomainEvent;

    /// <summary>
    /// Postava zažila noční můru v průběhu REM fáze.
    /// Způsobuje přerušení spánku a nárůst stresu.
    /// Pravděpodobnost roste s hodnotou stresu před usnutím.
    /// </summary>
    /// <param name="OccurredAt">Herní čas noční můry.</param>
    /// <param name="Human">Identifikátor postavy.</param>
    /// <param name="StressAtSleepStart">Hodnota stresu v okamžiku usnutí (0–100).</param>
    public sealed record NightmareTriggered(
        WDateTime OccurredAt,
        HumanId Human,
        double StressAtSleepStart) : IDomainEvent;

    /// <summary>
    /// Spánek byl přerušen před plánovaným koncem.
    /// BehaviorEngine zpracuje nedostatečnou obnovu jako částečný spánek.
    /// </summary>
    /// <param name="OccurredAt">Herní čas přerušení.</param>
    /// <param name="Human">Identifikátor postavy.</param>
    /// <param name="Cause">Příčina přerušení.</param>
    /// <param name="PhaseAtInterrupt">Fáze spánku, ve které k přerušení došlo.</param>
    public sealed record SleepInterrupted(
        WDateTime OccurredAt,
        HumanId Human,
        InterruptCause Cause,
        SleepPhase PhaseAtInterrupt) : IDomainEvent;

    #endregion

    #region Konec spánku

    /// <summary>
    /// Spánek skončil přirozeně (plánovaný čas probuzení) nebo přerušením.
    /// Obsahuje výslednou kvalitu spánku pro PhysiologyEngine a PsychologyEngine.
    /// </summary>
    /// <param name="OccurredAt">Herní čas probuzení.</param>
    /// <param name="Human">Identifikátor postavy.</param>
    /// <param name="TotalHoursSlept">Celková délka spánku v herních hodinách.</param>
    /// <param name="Quality">Výsledná kvalita spánku (0–100). Ovlivňuje obnovu energie a stresu.</param>
    /// <param name="WasInterrupted">True pokud byl spánek přerušen před plánovaným koncem.</param>
    public sealed record SleepEnded(
        WDateTime OccurredAt,
        HumanId Human,
        double TotalHoursSlept,
        double Quality,
        bool WasInterrupted) : IDomainEvent;

    #endregion

    #region Sdílený spánek

    /// <summary>
    /// Dvě postavy začaly spát společně.
    /// Publikováno při zpracování <see cref="SleepConfirmed"/> se společníkem.
    /// </summary>
    /// <param name="OccurredAt">Herní čas zahájení sdíleného spánku.</param>
    /// <param name="Who">Primární postava.</param>
    /// <param name="Companion">Společník.</param>
    /// <param name="Type">Kontext sdíleného spánku.</param>
    public sealed record SharedSleepBegan(
        WDateTime OccurredAt,
        HumanId Who,
        HumanId Companion,
        SharedSleepType Type) : IDomainEvent;

    #endregion
}
