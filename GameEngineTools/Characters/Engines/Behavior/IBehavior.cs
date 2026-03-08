// IBehavior.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{

    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    public sealed record BehaviorConfig(
        double InertiaWeight = 0.25,
        double NoveltyPenalty = 0.1,
        double PlanningHorizonHours = 2)
    {
        public BehaviorConfig() : this(0.25, 0.1, 2) { }
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
