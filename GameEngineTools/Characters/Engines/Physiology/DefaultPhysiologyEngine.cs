// DefaultPhysiologyEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using System;
    using Characters.Core;
    using GameEngineTools.World.Core.Time;
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
        private readonly WorldTimeContext _wtctx;

        private double _accHours;
        private bool _mensesOn;

        public DefaultPhysiologyEngine(
            IOptions<PhysiologyConfig> cfg,
            IOptions<MenstrualCycleConfig> cycleCfg,
            ILoggerFactory loggerFactory,
            IRandomSource rng,
            SexBiology biology,
            WDateOnly now,
            WorldTimeContext wtctx)
        {
            Config = cfg.Value;
            _cycleCfg = cycleCfg.Value;

            _log = loggerFactory.CreateLogger("Characters.Physiology");
            _rng = rng;
            _wtctx = wtctx;

            var initialCycle = (Config.EnableMenstrualCycle && biology == SexBiology.Female)
                ? SeedCycle(_cycleCfg, rng, now)
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
            var s = State;

            var action = ctx.Snapshot.Behavior.CurrentPlan?.Name;

            // Modifikátory driftu podle akce
            var energyDelta = action switch
            {
                "Sleep" => 15 * h,
                "SelfCare" => -0.5 * h,
                _ => -2 * h
            };

            var sleepDeptDelta = action switch
            {
                "Sleep" => -0.9 * h,
                _ => 0.6 * h
            };

            var hungerDelta = action switch
            {
                "Eat" => -40 * h,
                "Sleep" => 2 * h,
                _ => 6 * h
            };

            var thirstDelta = action switch
            {
                "Drink" => -50 * h,
                "Sleep" => 1 * h,
                _ => 8 * h
            };

            var painDelta = action switch
            {
                "SelfCare" => -10 * h,
                _ => 0
            };

            var immuneDelta = action switch
            {
                "Sleep" => -0.5 * h,
                "SelfCare" => -0.5 * h,
                _ => -0.3
            };

            s = s with
            {
                Energy = Clamp01p(s.Energy + energyDelta),
                Hunger = Clamp01p(s.Hunger + hungerDelta),
                Thirst = Clamp01p(s.Thirst + thirstDelta),
                Pain = Clamp01p(s.Pain + painDelta),
                SleepDebtHours = Math.Max(0, s.SleepDebtHours + sleepDeptDelta),
                ImmuneLoad = Clamp01p(s.ImmuneLoad + immuneDelta),
                BodyTempDelta = Approach(s.BodyTempDelta, 0, 0.1 * h)
            };

            if (s.Cycle is not null)
            {
                _accHours += h;
                while (_accHours >= 24.0)
                {
                    _accHours -= 24.0;
                    s = AdvanceCycleDay(s, now, ctx, outbox);
                }
            }

            State = s;
        }

        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            if (@event is not Behavior.ActionCommitted ac) return;

            var h = Math.Max(0, _wtctx.TotalHours(ac.Duration));
            var s = State;

            s = ac.ActionName switch
            {
                "Sleep" => s with
                {
                    SleepDebtHours = Math.Max(0, s.SleepDebtHours - h * 0.9),
                    Energy = Clamp01p(s.Energy + 15 * h),
                    Hunger = Clamp01p(s.Hunger + 2 * h),
                    Thirst = Clamp01p(s.Thirst + 1 * h)
                },
                "Eat" => s with
                {
                    Hunger = Clamp01p(s.Hunger - 40 * h),
                    Energy = Clamp01p(s.Energy + 5 * h),
                },
                "Drink" => s with
                {
                    Thirst = Clamp01p(s.Thirst - 50 * h)
                },
                "SelfCare" => s with
                {
                    Pain = Clamp01p(s.Pain - 10 * h),
                    ImmuneLoad = Clamp01p(s.ImmuneLoad - 5 * h)
                },
                _ => s
            };

            State = s;
        }

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
            s = s with { Cycle = next };

            // Symptomy jednou za den
            var (pain, bloat, tender, libido) = SymptomsFor(next);
            s = s with
            {
                Pain = Clamp01p(s.Pain + pain),
                Cycle = s.Cycle with
                {
                    SymptomBloat = Clamp01p(s.Cycle.SymptomBloat + bloat),
                    SymptomBreastTender = Clamp01p(s.Cycle.SymptomBreastTender + tender),
                    LibidoMod = libido
                }
            };

            return s;
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

        private static MenstrualCycleState SeedCycle(MenstrualCycleConfig cfg, IRandomSource rng, WDateOnly now)
        {
            var day = rng.Next(1, Math.Max(2, cfg.MeanCycleLengthDays));
            day = Math.Clamp(day, 1, 35);
            var phase = PhaseFor(day, cfg.MeanCycleLengthDays, cfg.MensesMeanDays);

            // Zpětný odhad, kdy začala menstruace
            var lastMensesStart = now.AddDays(-(day - 1));
            return new MenstrualCycleState(
                Phase: phase,
                DayInCycle: day,
                OvulationWindow: false,
                SymptomPain: 0, SymptomBreastTender: 0, SymptomBloat: 0,
                LibidoMod: 1.0,
                LastMensesStart: lastMensesStart);
        }

        private double SafeHours(WTimeSpan dt) => Math.Max(0, _wtctx.TotalHours(dt));
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

        public void RestoreState(PhysiologyState state) => State = state;
    }
}

