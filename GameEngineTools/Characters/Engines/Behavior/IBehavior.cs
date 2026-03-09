// IBehavior.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{

    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    internal static class ActionNames
    {
        public const string Sleep = "Sleep";
        public const string Eat = "Eat";
        public const string Drink = "Drink";
        public const string ReachOut = "ReachOut";
        public const string Work = "Work";
        public const string Create = "Create";
        public const string SelfCare = "SelfCare";
        public const string InviteIntimacy = "InviteIntimacy";
        public const string Idle = "Idle";
    }
    public sealed record BehaviorConfig(
        double InertiaWeight = 0.25,
        double NoveltyPenalty = 0.1,
        double PlanningHorizonHours = 2,
        double BaseSleepHours = 8,
        double MinSleepHours = 4,
        double MaxSleepHours = 12,
        double SleepCooldownHours = 16)
    {
        public BehaviorConfig() : this(0.25, 0.1, 2, 8, 4, 12, 16) { }
    }

    public sealed record BehaviorState(
        double NeedRest, double NeedFood, double NeedWater, double NeedBelonging, double NeedCompetence, double NeedIntimacy,
        PlannedAction? CurrentPlan,
        IReadOnlyDictionary<string, double>? Cooldowns = null);

    public interface IBehaviorEngine : IEngine<BehaviorState, BehaviorConfig> { }

    public sealed record PlannedAction(string Name, WDateTime Start, WTimeSpan ExpectedDuration, double Utility);

    public sealed record ActionProposed(WDateTime OccurredAt, HumanId Human, string ActionName, double Utility) : IDomainEvent;
    public sealed record ActionCommitted(WDateTime OccurredAt, HumanId Human, string ActionName, WTimeSpan Duration) : IDomainEvent;
}
