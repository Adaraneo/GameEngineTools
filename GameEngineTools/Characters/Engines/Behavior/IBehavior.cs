// IBehavior.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior.Intent;
    using GameEngineTools.World.Utils.Time;

    #region Configuration and state

    /// <summary>
    /// Runtime tuning parameters for the orchestrated behavior system.
    /// </summary>
    public sealed record BehaviorConfig(
        double InertiaWeight = 0.25,
        double NoveltyPenalty = 0.1,
        double PlanningHorizonHours = 2,
        double BaseSleepHours = 8,
        double MinSleepHours = 4,
        double MaxSleepHours = 12,
        double SleepCooldownHours = 16,
        bool UseIntentManagement = true,
        double IntentSwitchMargin = 10,
        double IntentBaseBias = 8,
        double IntentCommitmentBiasStep = 1,
        double IntentTimeoutHours = 2,
        double EmergencyIntentOverrideThreshold = 75)
    {
        public BehaviorConfig() : this(0.25, 0.1, 2, 8, 4, 12, 16, true, 10, 8, 1, 2, 75) { }
    }

    /// <summary>
    /// Persistent per-character behavior state carried from tick to tick.
    /// </summary>
    public sealed record BehaviorState(
        double NeedRest,
        double NeedFood,
        double NeedWater,
        double NeedBelonging,
        double NeedCompetence,
        double NeedIntimacy,
        PlannedAction? CurrentPlan,
        IReadOnlyDictionary<string, double>? Cooldowns = null,
        bool WaitingForSleepConfirmation = false,
        int SleepDeclineCount = 0,
        WDateTime? SleepGraceExpiresAt = null,
        ActiveIntent? ActiveIntent = null);

    #endregion

    #region Engine contract

    /// <summary>
    /// Public behavior engine contract.
    /// </summary>
    public interface IBehaviorEngine : IEngine<BehaviorState, BehaviorConfig>
    { }

    #endregion

    #region Plans and events

    /// <summary>
    /// Represents the currently committed action and its expected runtime.
    /// </summary>
    public sealed record PlannedAction(string Name, WDateTime Start, WTimeSpan ExpectedDuration, double Utility);

    /// <summary>
    /// Emitted when the engine proposes a winning action for the current tick.
    /// </summary>
    public sealed record ActionProposed(WDateTime OccurredAt, HumanId Human, string ActionName, double Utility) : IDomainEvent;

    /// <summary>
    /// Emitted when the orchestrator commits an action to the behavior state.
    /// </summary>
    public sealed record ActionCommitted(WDateTime OccurredAt, HumanId Human, string ActionName, WTimeSpan Duration) : IDomainEvent;

    #endregion
}
