// DefaultPhysiologyEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using System;
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Homeostázy + základní menstruační cyklus. Bez „spánku“ či jídla z eventů – jen drift a symptomy.
    /// </summary>
    internal sealed class DefaultPhysiologyEngine : IPhysiologyEngine
    {
        public PhysiologyState State { get; private set; }
        public PhysiologyConfig Config { get; }

        private readonly ILogger _log;
        private readonly IRandomSource _rng;
        private readonly MenstrualCycleConfig _cycleCfg;

        private double _accHours;
        private bool _mensesOn;

        public DefaultPhysiologyEngine(
            IOptions<PhysiologyConfig> cfg,
            IOptions<MenstrualCycleConfig>? cycleCfg,
            ILoggerFactory loggerFactory,
            IHumanContext ctx)
        {
            Config = cfg.Value;
            _cycleCfg = (cycleCfg?.Value) ?? new MenstrualCycleConfig(
                MeanCycleLengthDays: 28,
                VariabilityDaysStdDev: 1.8,
                MensesMeanDays: 5,
                PmsRisk: 0.35,
                EnableOvulationWindowEvents: true,
                EnableSymptoms: true);

            _log = loggerFactory.CreateLogger("Characters.Physiology");
            _rng = ctx.Random;

            var initialCycle = (Config.EnableMenstrualCycle && ctx.Biology == SexBiology.Female)
                ? SeedCycle(_cycleCfg)
                : null;

            State = new PhysiologyState(
                Energy: 70,
                SleepDebtHours: 2,
                Hunger: 25,
                Thirst: 20,
                Pain: 5,
                ImmuneLoad: 10,
                BodyTempDelta: 0,
                Cycle: initialCycle);

            _mensesOn = initialCycle?.Phase == CyclePhase.Menses;
        }

        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            var h = SafeHours(dt);

            // --- Základní drift ---
            var energyDropPerHour = 2.0;                 // % za hod
            var hungerRisePerHour = 6.0;                 // % za hod
            var thirstRisePerHour = 8.0;                 // % za hod
            var sleepDebtRisePerHour = 0.6;              // hod/hod
            var immuneRecoveryPerHour = 0.3;             // % za hod
            var tempNormalizePerHour = 0.1;              // °C/hod

            var s = State;

            s = s with
            {
                Energy = Clamp01p(s.Energy - energyDropPerHour * h),
                Hunger = Clamp01p(s.Hunger + hungerRisePerHour * h),
                Thirst = Clamp01p(s.Thirst + thirstRisePerHour * h),
                SleepDebtHours = Math.Max(0, s.SleepDebtHours + sleepDebtRisePerHour * h),
                ImmuneLoad = Clamp01p(s.ImmuneLoad - immuneRecoveryPerHour * h),
                BodyTempDelta = Approach(s.BodyTempDelta, 0, tempNormalizePerHour * h)
            };

            // --- Menstruační cyklus (pokud aktivní) ---
            if (s.Cycle is not null)
            {
                _accHours += h;
                while (_accHours >= 24.0)
                {
                    _accHours -= 24.0;
                    s = AdvanceCycleDay(s, now, ctx, outbox);
                }

                // Symptomy a libido podle fáze (jemné modulace)
                var (pain, bloat, tender, libido) = SymptomsFor(s.Cycle);
                s = s with { Pain = Clamp01p(s.Pain + pain), Cycle = s.Cycle with { SymptomBloat = bloat, SymptomBreastTender = tender, LibidoMod = libido } };
            }

            State = s;
        }

        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            // Pro jednoduchost zatím nereagujeme; fyzio si jede drift.
            // (Volitelně: reagovat na ActionCommitted: "Sleep", "Eat", "Drink"…)
        }

        // ---- helpers ----
        private PhysiologyState AdvanceCycleDay(PhysiologyState s, WDateTime now, IHumanContext ctx, IEventCollector box)
        {
            var c = s.Cycle!;
            var length = Math.Max(21, Math.Min(35, _cycleCfg.MeanCycleLengthDays + (int)Math.Round(Normal(_rng, 0, _cycleCfg.VariabilityDaysStdDev))));
            var day = c.DayInCycle + 1;
            if (day > length) day = 1;

            var phase = PhaseFor(day, length, _cycleCfg.MensesMeanDays);

            // Event: CycleDayAdvanced
            box.Add(new CycleDayAdvanced(now, ctx.Id, day, phase));

            // Menses start/end
            var wasMenses = _mensesOn;
            var isMenses = phase == CyclePhase.Menses;
            if (!wasMenses && isMenses) { box.Add(new MensesStarted(now, ctx.Id)); _mensesOn = true; }
            if (wasMenses && !isMenses) { box.Add(new MensesEnded(now, ctx.Id)); _mensesOn = false; }

            // Ovulation window (jednoduše den 13–15)
            var ovulWindow = phase == CyclePhase.Ovulation;
            if (_cycleCfg.EnableOvulationWindowEvents && ovulWindow && c.OvulationWindow == false)
                box.Add(new OvulationWindowOpened(now, ctx.Id));

            var next = c with { DayInCycle = day, Phase = phase, OvulationWindow = ovulWindow };
            return s with { Cycle = next };
        }

        private static (double pain, double bloat, double tender, double libidoMod) SymptomsFor(MenstrualCycleState c)
        {
            // Jednoduché mapování (přírůstky za den; následně se vyhladí normalizací v Ticku)
            return c.Phase switch
            {
                CyclePhase.Menses => (+3, +2, +2, 0.90),
                CyclePhase.Follicular => (-2, -1, -1, 1.05),
                CyclePhase.Ovulation => (+0, +0, +0, 1.15),
                CyclePhase.Luteal => (+1, +1, +1, 0.95),
                _ => (0, 0, 0, 1.00)
            };
        }

        private static CyclePhase PhaseFor(int day, int length, int mensesDays)
        {
            var ovulDay = 14;
            if (day <= mensesDays) return CyclePhase.Menses;
            if (day < ovulDay) return CyclePhase.Follicular;
            if (day >= ovulDay && day <= ovulDay + 1) return CyclePhase.Ovulation;
            return CyclePhase.Luteal;
        }

        private static MenstrualCycleState SeedCycle(MenstrualCycleConfig cfg)
        {
            // Výchozí náhodný den; poslední začátek menses necháme default (není-li k dispozici WDateOnly now)
            var day = Math.Clamp(new Random().Next(1, Math.Max(2, cfg.MeanCycleLengthDays)), 1, 35);
            var phase = CyclePhase.Follicular;
            return new MenstrualCycleState(
                Phase: phase,
                DayInCycle: day,
                OvulationWindow: false,
                SymptomPain: 0, SymptomBreastTender: 0, SymptomBloat: 0,
                LibidoMod: 1.0,
                LastMensesStart: default);
        }

        private static double SafeHours(WTimeSpan dt) => Math.Max(0, dt.TotalHours);
        private static double Normal(IRandomSource r, double mean, double std)
        {
            // Box-Muller
            var u1 = 1.0 - r.NextUnit();
            var u2 = 1.0 - r.NextUnit();
            var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return mean + std * z;
        }
        private static double Approach(double value, double target, double by) =>
            (value < target) ? Math.Min(target, value + by) : Math.Max(target, value - by);
        private static double Clamp01p(double v) => Math.Max(0, Math.Min(100, v));
    }
}

