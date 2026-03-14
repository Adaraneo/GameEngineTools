// IPhysiology.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    public sealed record PhysiologyConfig(
        double RestingMetabolicRate = 1600,
        double MaxSleepDebtHours = 12,
        bool EnableMenstrualCycle = true,
        int MenstrualCycleBeginsInAge = 12,
        double EnergyRecoveryPerSleepHour = 10.0)
    {
        public PhysiologyConfig() : this(1600, 12, true, 12, 10) { }
    }

    public sealed record PhysiologyState(
        double Energy,          // 0..100
        double SleepDebtHours,  // >= 0
        double Hunger,          // 0..100
        double Thirst,          // 0..100
        double Pain,            // 0..100
        double ImmuneLoad,      // 0..100
        double BodyTempDelta,   // °C deviation
        MenstrualCycleState? Cycle);

    public interface IPhysiologyEngine : IEngine<PhysiologyState, PhysiologyConfig>
    { }

    // --- Menstruační modul ---
    public enum CyclePhase
    { Menses, Follicular, Ovulation, Luteal, Paused /* např. těhotenství/antiko */ }

    public sealed record MenstrualCycleConfig(
        int MeanCycleLengthDays = 28,
        double VariabilityDaysStdDev = 2.0,
        int MensesMeanDays = 5,
        double PmsRisk = 0.35,
        bool EnableOvulationWindowEvents = true,
        bool EnableSymptoms = true)
    {
        public MenstrualCycleConfig() : this(28, 2.0, 5, 0.35, true, true) { }
    }

    public sealed record MenstrualCycleState(
        CyclePhase Phase,
        int DayInCycle,
        bool OvulationWindow,
        double SymptomPain,         // 0..100
        double SymptomBreastTender, // 0..100
        double SymptomBloat,        // 0..100
        double LibidoMod,           // multiplikátor 0.5..1.5
        WDateOnly LastMensesStart);

    // Události
    public sealed record MensesStarted(WDateTime OccurredAt, HumanId Human) : IDomainEvent;
    public sealed record MensesEnded(WDateTime OccurredAt, HumanId Human) : IDomainEvent;
    public sealed record OvulationWindowOpened(WDateTime OccurredAt, HumanId Human) : IDomainEvent;
    public sealed record CycleDayAdvanced(WDateTime OccurredAt, HumanId Human, int DayInCycle, CyclePhase Phase) : IDomainEvent;
}
