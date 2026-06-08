// SleepEvents.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Sleep
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    #region Prompt a potvrzení

    /// <summary>
    /// The BehaviorEngine emits this event when <c>NeedRest</c> crosses the threshold.
    /// <br/>
    /// — For NPCs: the system immediately responds with <see cref="SleepConfirmed"/>.<br/>
    /// — For the PC: the UI shows a prompt; the player confirms or declines.
    /// </summary>
    /// <param name="OccurredAt">Game time the prompt was sent.</param>
    /// <param name="Human">Character identifier.</param>
    /// <param name="SleepNeed">Current sleep need (0–100) at the moment of the prompt.</param>
    public sealed record SleepPromptRequested(
        WDateTime OccurredAt,
        HumanId Human,
        double SleepNeed) : IDomainEvent;

    /// <summary>
    /// Confirmation to start sleeping — comes from the system (NPC) or the UI (PC).
    /// Triggers creation of an <see cref="ISleepSession"/>.
    /// </summary>
    /// <param name="OccurredAt">Game time of confirmation.</param>
    /// <param name="Human">Character identifier.</param>
    /// <param name="PlannedWakeUp">Planned wake-up time. May be interrupted earlier.</param>
    /// <param name="Companion">Optional companion for shared sleep.</param>
    /// <param name="SharedType">The shared-sleep type, or <c>null</c> if the character sleeps alone.</param>
    public sealed record SleepConfirmed(
        WDateTime OccurredAt,
        HumanId Human,
        WDateTime PlannedWakeUp,
        HumanId? Companion = null,
        SharedSleepType? SharedType = null) : IDomainEvent;

    /// <summary>
    /// The player declined the sleep prompt.
    /// The BehaviorEngine starts a grace period and then re-emits <see cref="SleepPromptRequested"/>.
    /// </summary>
    /// <param name="OccurredAt">Game time of the decline.</param>
    /// <param name="Human">Character identifier.</param>
    /// <param name="DeclineCount">Which decline in the series this is (resets after sleeping).</param>
    public sealed record SleepDeclined(
        WDateTime OccurredAt,
        HumanId Human,
        int DeclineCount) : IDomainEvent;

    #endregion Prompt a potvrzení

    #region Průběh spánku

    /// <summary>
    /// The character entered a new phase of the sleep cycle.
    /// Published by <see cref="ISleepSession"/> on every phase transition.
    /// </summary>
    /// <param name="OccurredAt">Game time of the transition.</param>
    /// <param name="Human">Character identifier.</param>
    /// <param name="Phase">The new current phase.</param>
    public sealed record SleepPhaseChanged(
        WDateTime OccurredAt,
        HumanId Human,
        SleepPhase Phase) : IDomainEvent;

    /// <summary>
    /// During the REM phase the character dreams — a narrative hook for the game.
    /// The dream content (memories, events, premonitions) is defined by the game layer.
    /// </summary>
    /// <param name="OccurredAt">Game time of the dream.</param>
    /// <param name="Human">Character identifier.</param>
    /// <param name="DreamSeed">Seed for the dream-content generator (deterministic).</param>
    public sealed record DreamOccurred(
        WDateTime OccurredAt,
        HumanId Human,
        int DreamSeed) : IDomainEvent;

    /// <summary>
    /// The character experienced a nightmare during the REM phase.
    /// Causes the sleep to be interrupted and stress to rise.
    /// The probability rises with the stress level before falling asleep.
    /// </summary>
    /// <param name="OccurredAt">Game time of the nightmare.</param>
    /// <param name="Human">Character identifier.</param>
    /// <param name="StressAtSleepStart">Stress level at the moment of falling asleep (0–100).</param>
    public sealed record NightmareTriggered(
        WDateTime OccurredAt,
        HumanId Human,
        double StressAtSleepStart) : IDomainEvent;

    /// <summary>
    /// Sleep was interrupted before its planned end.
    /// The BehaviorEngine treats the insufficient recovery as partial sleep.
    /// </summary>
    /// <param name="OccurredAt">Game time of the interruption.</param>
    /// <param name="Human">Character identifier.</param>
    /// <param name="Cause">Cause of the interruption.</param>
    /// <param name="PhaseAtInterrupt">The sleep phase in which the interruption occurred.</param>
    public sealed record SleepInterrupted(
        WDateTime OccurredAt,
        HumanId Human,
        InterruptCause Cause,
        SleepPhase PhaseAtInterrupt) : IDomainEvent;

    #endregion Průběh spánku

    #region Konec spánku

    /// <summary>
    /// Sleep ended naturally (the planned wake-up time) or by interruption.
    /// Contains the resulting sleep quality for the PhysiologyEngine and PsychologyEngine.
    /// </summary>
    /// <param name="OccurredAt">Game time of waking.</param>
    /// <param name="Human">Character identifier.</param>
    /// <param name="TotalHoursSlept">Total sleep duration in game hours.</param>
    /// <param name="Quality">Resulting sleep quality (0–100). Affects energy and stress recovery.</param>
    /// <param name="WasInterrupted">True if the sleep was interrupted before its planned end.</param>
    public sealed record SleepEnded(
        WDateTime OccurredAt,
        HumanId Human,
        double TotalHoursSlept,
        double Quality,
        bool WasInterrupted) : IDomainEvent;

    #endregion Konec spánku

    #region Sdílený spánek

    /// <summary>
    /// Two characters began sleeping together.
    /// Published when handling <see cref="SleepConfirmed"/> with a companion.
    /// </summary>
    /// <param name="OccurredAt">Game time the shared sleep began.</param>
    /// <param name="Who">The primary character.</param>
    /// <param name="Companion">The companion.</param>
    /// <param name="Type">The shared-sleep context.</param>
    public sealed record SharedSleepBegan(
        WDateTime OccurredAt,
        HumanId Who,
        HumanId Companion,
        SharedSleepType Type) : IDomainEvent;

    #endregion Sdílený spánek
}
