// DefaultPhysiologyEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using System;
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using static ActionNames;

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
            IOptions<MenstrualCycleConfig> cycleCfg,
            ILoggerFactory loggerFactory,
            IRandomSource rng,
            SexBiology biology,
            WDateOnly birthDate,
            WDateOnly now)
        {
            Config = cfg.Value;
            _cycleCfg = cycleCfg.Value;

            _log = loggerFactory.CreateLogger<DefaultPhysiologyEngine>();
            _rng = rng;

            var initialCycle = (Config.EnableMenstrualCycle && biology == SexBiology.Female && (now.Year - birthDate.Year) >= Config.MenstrualCycleBeginsInAge && now != default)
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
                SelfCare => -0.5 * h,
                Sleep => 0,
                _ => -2 * h
            };

            var hungerDelta = action switch
            {
                Eat => -40 * h,
                Sleep => 2.0 * h,
                _ => 6 * h
            };

            var thirstDelta = action switch
            {
                Drink => -50 * h,
                Sleep => 2.0 * h,
                _ => 8 * h
            };

            var painDelta = action switch
            {
                SelfCare => -10 * h,
                Sleep => -(Config.PainPassiveRecoveryPerHour + Config.PainSleepRecoveryPerHour) * h,
                _ => -Config.PainPassiveRecoveryPerHour * h
            };

            var immuneDelta = action switch
            {
                SelfCare => -0.5 * h,
                _ => -0.3
            };

            s = s with
            {
                Energy = Clamp01p(s.Energy + energyDelta),
                Hunger = Clamp01p(s.Hunger + hungerDelta),
                Thirst = Clamp01p(s.Thirst + thirstDelta),
                Pain = Clamp01p(s.Pain + painDelta),
                ImmuneLoad = Clamp01p(s.ImmuneLoad + immuneDelta),
                BodyTempDelta = Approach(s.BodyTempDelta, 0, 0.1 * h)
            };

            if (s.Pregnancy is { } pregnancy)
            {
                s = AdvancePregnancy(s, now, ctx, outbox, pregnancy);
            }
            else if (s.Cycle is not null)
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
            var s = State;

            switch (@event)
            {
                // --- Konec spánkové session ---
                // Průběžný drift (energie, hlad, žízeň) probíhá v Tick() přes CurrentPlan.Name == "Sleep".
                // Zde aplikujeme jednorázový souhrnný efekt na základě kvality a délky spánku.
                case Sleep.SleepEnded se:
                    {
                        var h = Math.Max(0, se.TotalHoursSlept);

                        // Kvalita (0–100) moduluje efektivitu obnovy.
                        // Při kvalitě 100 = plná obnova, při 0 = žádná.
                        var qualityFactor = se.Quality / 100.0;

                        s = s with
                        {
                            // Spánkový dluh: maximální splacení závisí na kvalitě
                            SleepDebtHours = Math.Max(0, s.SleepDebtHours - h * 0.9 * qualityFactor),

                            // Imunitní systém: regenerace hlubokého spánku
                            ImmuneLoad = Clamp01p(s.ImmuneLoad - 3.0 * qualityFactor),

                            Pain = se.Quality >= 40
                                ? Clamp01p(s.Pain - 5.0 * qualityFactor)
                                : s.Pain,

                            // Energie se obnoví spánkem
                            // 8h kvalitního spánku (quality=100) → +80 energie
                            // Špatný spánek (quality=40) → +32 energie
                            Energy = Clamp01p(s.Energy + h * Config.EnergyRecoveryPerSleepHour * qualityFactor)
                        };

                        using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultPhysiologyEngine))))
                        {
                            _log.PhysiologySleepEnded(ctx.Id.Value.ToString(), h, se.Quality, s.SleepDebtHours);
                        }

                        break;
                    }

                case SexualEncounterOutcome se when se.Accepted && se.ReproductivePotential:
                    s = TryStartPregnancy(s, se, ctx, outbox);
                    break;
            }

            State = s;
        }

        private PhysiologyState AdvanceCycleDay(PhysiologyState s, WDateTime now, IHumanContext ctx, IEventCollector box)
        {
            var c = s.Cycle!;
            var length = Math.Max(21, Math.Min(35, _cycleCfg.MeanCycleLengthDays + (int)Math.Round(Normal(_rng, 0, _cycleCfg.VariabilityDaysStdDev))));
            var day = c.DayInCycle + 1;
            if (day > length)
            {
                day = 1;
            }

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
            {
                box.Add(new OvulationWindowOpened(now, ctx.Id));
            }

            var next = c with { DayInCycle = day, Phase = phase, OvulationWindow = ovulWindow, LastMensesStart = (day == 1) ? now.Date : c.LastMensesStart };
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

        private PhysiologyState AdvancePregnancy(PhysiologyState s, WDateTime now, IHumanContext ctx, IEventCollector outbox, PregnancyState pregnancy)
        {
            var daysPregnant = pregnancy.ConceivedOn.DaysUntil(now.Date);

            if (!pregnancy.Discovered && daysPregnant >= Config.PregnancyDiscoveryMinDays)
            {
                pregnancy = pregnancy with { Discovered = true, DiscoveredOn = now.Date };
                s = s with { Pregnancy = pregnancy };
                outbox.Add(new PregnancyDiscovered(now, ctx.Id, pregnancy.OtherParent));
            }

            if (now.Date >= pregnancy.EstimatedDueDate)
            {
                outbox.Add(new ChildBorn(now, ctx.Id, pregnancy.OtherParent));
                return s with
                {
                    Pregnancy = null,
                    Cycle = s.Cycle is null
                        ? null
                        : s.Cycle with { Phase = CyclePhase.Paused, OvulationWindow = false, LibidoMod = 0.8 }
                };
            }

            return s with
            {
                Pregnancy = pregnancy,
                Cycle = s.Cycle is null
                    ? null
                    : s.Cycle with { Phase = CyclePhase.Paused, OvulationWindow = false, LibidoMod = 0.8 }
            };
        }

        private PhysiologyState TryStartPregnancy(PhysiologyState s, SexualEncounterOutcome encounter, IHumanContext ctx, IEventCollector outbox)
        {
            if (ctx.Biology != SexBiology.Female || s.Pregnancy is not null || (encounter.From != ctx.Id && encounter.To != ctx.Id))
            {
                return s;
            }

            var otherParent = encounter.From == ctx.Id
                ? encounter.To
                : encounter.From;
            var conceptionChance = ConceptionChance(s, encounter);
            if (!ctx.Random.Chance(conceptionChance))
            {
                return s;
            }

            var pregnancy = new PregnancyState(
                otherParent,
                encounter.OccurredAt.Date,
                encounter.OccurredAt.Date.AddDays(Config.PregnancyTermDays));

            outbox.Add(new PregnancyStarted(encounter.OccurredAt, ctx.Id, otherParent, pregnancy.EstimatedDueDate));
            return s with
            {
                Pregnancy = pregnancy,
                Cycle = s.Cycle is null
                    ? null
                    : s.Cycle with { Phase = CyclePhase.Paused, OvulationWindow = false, LibidoMod = 0.8 }
            };
        }

        private double ConceptionChance(PhysiologyState s, SexualEncounterOutcome encounter)
        {
            var chance = Config.BaseConceptionChancePerEncounter;

            if (s.Cycle?.OvulationWindow == true)
            {
                chance *= Config.OvulationConceptionMultiplier;
            }

            chance *= encounter.Intent switch
            {
                ReproductiveIntent.AvoidPregnancy => 0.35,
                ReproductiveIntent.TryingForChild => 1.35,
                ReproductiveIntent.OpenToPregnancy => 1.10,
                _ => 1.0
            };

            chance *= encounter.Contraception switch
            {
                ContraceptionLevel.None => 1.0,
                ContraceptionLevel.Low => 0.55,
                ContraceptionLevel.Moderate => 0.25,
                ContraceptionLevel.High => 0.04,
                _ => 0.25
            };

            return Math.Clamp(chance, 0.0, 0.65);
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
            if (day <= mensesDays)
            {
                return CyclePhase.Menses;
            }

            if (day < ovulDay)
            {
                return CyclePhase.Follicular;
            }

            if (day >= ovulDay && day <= ovulDay + 1)
            {
                return CyclePhase.Ovulation;
            }

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

        private double SafeHours(WTimeSpan dt) => Math.Max(0, dt.TotalHours);

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
