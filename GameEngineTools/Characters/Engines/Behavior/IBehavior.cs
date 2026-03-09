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

    /// <summary>
    /// Aktuální stav behaviorálního enginu postavy.
    /// Obsahuje přepočítané potřeby, aktuální plán a stav čekání na spánek.
    /// </summary>
    /// <param name="NeedRest">Potřeba odpočinku (0–100). Roste se spánkovým dluhem a únavou.</param>
    /// <param name="NeedFood">Potřeba jídla (0–100). Přímo mapuje na <c>PhysiologyState.Hunger</c>.</param>
    /// <param name="NeedWater">Potřeba vody (0–100). Přímo mapuje na <c>PhysiologyState.Thirst</c>.</param>
    /// <param name="NeedBelonging">Potřeba sounáležitosti (0–100). Klesá s blízkostí vztahů.</param>
    /// <param name="NeedCompetence">Potřeba kompetence (0–100). Závisí na osobnostním rysu motivace.</param>
    /// <param name="NeedIntimacy">Potřeba intimity (0–100). Modulována libidem a přitažlivostí.</param>
    /// <param name="CurrentPlan">Aktuálně prováděná akce, nebo <c>null</c> pokud postava nic nedělá.</param>
    /// <param name="Cooldowns">Slovník zbývajících cooldownů v hodinách pro jednotlivé akce.</param>
    /// <param name="WaitingForSleepConfirmation">
    /// True pokud engine vyslal <see cref="Sleep.SleepEvents.SleepPromptRequested"/>
    /// a čeká na odpověď hráče nebo systému.
    /// Dokud je True, engine neprovádí výběr nové akce.
    /// </param>
    /// <param name="SleepDeclineCount">
    /// Počet za sebou jdoucích odmítnutí spánku. Resetuje se po <see cref="Sleep.SleepEvents.SleepEnded"/>.
    /// Ovlivňuje délku grace periody a velikost penalizace.
    /// </param>
    /// <param name="SleepGraceExpiresAt">
    /// Herní čas, kdy vyprší grace perioda po odmítnutém promptu.
    /// Po vypršení engine vyšle nový <see cref="Sleep.SleepEvents.SleepPromptRequested"/>.
    /// <c>null</c> pokud žádná grace perioda neběží.
    /// </param>
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
        WDateTime? SleepGraceExpiresAt = null);

    public interface IBehaviorEngine : IEngine<BehaviorState, BehaviorConfig>
    { }

    public sealed record PlannedAction(string Name, WDateTime Start, WTimeSpan ExpectedDuration, double Utility);

    public sealed record ActionProposed(WDateTime OccurredAt, HumanId Human, string ActionName, double Utility) : IDomainEvent;
    public sealed record ActionCommitted(WDateTime OccurredAt, HumanId Human, string ActionName, WTimeSpan Duration) : IDomainEvent;
}
