// IBehavior.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior.Intent;
    using GameEngineTools.World.Utils.Time;

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

    public interface IBehaviorEngine : IEngine<BehaviorState, BehaviorConfig>
    { }

    public sealed record PlannedAction(string Name, WDateTime Start, WTimeSpan ExpectedDuration, double Utility);

    public sealed record ActionProposed(WDateTime OccurredAt, HumanId Human, string ActionName, double Utility) : IDomainEvent;
    public sealed record ActionCommitted(WDateTime OccurredAt, HumanId Human, string ActionName, WTimeSpan Duration) : IDomainEvent;
}
