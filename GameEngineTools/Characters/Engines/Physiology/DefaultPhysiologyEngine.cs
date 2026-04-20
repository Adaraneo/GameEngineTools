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
    /// Default implementation of the physiology engine.
    /// Models homeostasis (energy, hunger, thirst, pain, immune system, fever) and, for female
    /// characters of sufficient age, the menstrual cycle including ovulation, conception, pregnancy
    /// discovery, and birth.
    /// </summary>
    /// <remarks>
    /// Continuous drift is applied in <see cref=”Tick”/> proportional to elapsed game hours.
    /// Discrete one-time effects (sleep recovery, sexual encounters) are applied in <see cref=”Handle”/>.
    /// All state mutations produce a new <see cref=”PhysiologyState”/> record; the engine is
    /// functionally pure once the random source is fixed.
    /// </remarks>
    internal sealed class DefaultPhysiologyEngine : IPhysiologyEngine
    {
        /// <summary>Gets the current physiological state of the character.</summary>
        public PhysiologyState State { get; private set; }

        /// <summary>Gets the configuration used to drive physiological drift and cycle behaviour.</summary>
        public PhysiologyConfig Config { get; }

        private readonly ILogger _log;
        private readonly IRandomSource _rng;
        private readonly MenstrualCycleConfig _cycleCfg;

        private double _accHours;
        private double _injuryAccHours;
        private double _postpartumAccHours;
        private bool _mensesOn;

        /// <summary>
        /// Initialises the engine, computing an initial <see cref="PhysiologyState"/> including
        /// a seeded menstrual cycle when <paramref name="biology"/> is
        /// <see cref="SexBiology.Female"/>, the cycle is enabled in config, and
        /// <paramref name="now"/> minus <paramref name="birthDate"/> meets the minimum age.
        /// </summary>
        /// <param name="cfg">Physiology configuration (energy recovery, pain recovery, conception rates, etc.).</param>
        /// <param name="cycleCfg">Menstrual cycle configuration (mean length, variability, menses days).</param>
        /// <param name="loggerFactory">Logger factory injected by the DI container.</param>
        /// <param name="rng">Random source; use <c>ZeroRandom</c> in tests for determinism.</param>
        /// <param name="biology">Biological sex of the character — cycle only initialises for Female.</param>
        /// <param name="birthDate">Character birth date, used to check minimum cycle age.</param>
        /// <param name="now">Current in-world date at engine construction.</param>
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
                Cycle: initialCycle,
                Nutrition: Config.EnableNutrition ? new NutritionState() : null);

            _mensesOn = initialCycle?.Phase == CyclePhase.Menses;
        }

        /// <summary>
        /// Advances continuous physiological drift by one time step.
        /// Called each game tick to apply gradual changes to energy, hunger, thirst, pain,
        /// immune load, and body temperature, modulated by the character's current action.
        /// Also advances the menstrual cycle day counter (once per accumulated 24 game hours)
        /// or pregnancy progression if the character is pregnant.
        /// </summary>
        /// <param name="now">Current in-world date-time, used for pregnancy/cycle date calculations.</param>
        /// <param name="dt">Elapsed time since the last tick; drift is scaled by <c>dt.TotalHours</c>.</param>
        /// <param name="ctx">
        /// Character context providing <c>ctx.Snapshot.Behavior.CurrentPlan?.Name</c> to select
        /// action-specific drift branches (Sleep, Eat, Drink, SelfCare, or default awake rate).
        /// </param>
        /// <param name="outbox">Collector for cycle and pregnancy domain events.</param>
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
                _ => -0.3 * h
            };

            var feverDelta = s.ImmuneLoad > 30 ? (s.ImmuneLoad - 30) / 70.0 * 2.0 : 0.0;

            s = s with
            {
                Energy = Clamp01p(s.Energy + energyDelta),
                Hunger = Clamp01p(s.Hunger + hungerDelta),
                Thirst = Clamp01p(s.Thirst + thirstDelta),
                Pain = Clamp01p(s.Pain + painDelta),
                ImmuneLoad = Clamp01p(s.ImmuneLoad + immuneDelta),
                BodyTempDelta = Math.Clamp(Approach(s.BodyTempDelta, feverDelta, 0.1 * h), -1.0, 3.5)
            };

            // Nutriční drift — Calories/Protein klesají, jsou doplňovány jídlem;
            // Iron se obnovuje spánkem; VitaminD pomalu klesá
            if (s.Nutrition is { } nut)
            {
                var caloriesDelta = action == Eat  ?  Config.CaloriesEatingGainPerHour * h : -Config.NutritionDecayPerHour * h;
                var proteinDelta  = action == Eat  ?  Config.ProteinEatingGainPerHour  * h : -Config.NutritionDecayPerHour * h;
                var ironDelta     = action == Sleep ?  Config.IronSleepRecoveryPerHour  * h : -Config.NutritionDecayPerHour * h * 0.3;
                s = s with
                {
                    Nutrition = nut with
                    {
                        Calories = Clamp01p(nut.Calories + caloriesDelta),
                        VitaminD = Clamp01p(nut.VitaminD - Config.NutritionDecayPerHour * h * 0.5),
                        Iron     = Clamp01p(nut.Iron     + ironDelta),
                        Protein  = Clamp01p(nut.Protein  + proteinDelta)
                    }
                };
            }

            // Zranění — přidání bolesti a postupné hojení (1× za 24h)
            if (s.Injury is { } inj)
            {
                s = s with { Pain = Clamp01p(s.Pain + inj.Severity * 0.05 * h) };
                if (inj.Type == InjuryType.Infection)
                    s = s with { ImmuneLoad = Clamp01p(s.ImmuneLoad + Config.InjuryInfectionImmuneLoadPerDay / 24.0 * h) };

                _injuryAccHours += h;
                while (_injuryAccHours >= 24.0)
                {
                    _injuryAccHours -= 24.0;
                    var recoveryPerDay = action is Sleep or SelfCare
                        ? Config.InjuryRestRecoveryPerDay
                        : Config.InjuryActiveRecoveryPerDay;
                    inj = inj with { Severity = Math.Max(0, inj.Severity - recoveryPerDay), DaysSinceOnset = inj.DaysSinceOnset + 1 };
                    if (inj.Severity <= 0)
                    {
                        s = s with { Injury = null };
                        outbox.Add(new InjuryHealed(now, ctx.Id));
                        _injuryAccHours = 0;
                        break;
                    }
                    s = s with { Injury = inj };
                }
            }

            // Šestinedělí — minimální bolest a maximální energie podle fáze
            if (s.Postpartum is not null)
            {
                var (painFloor, energyCap) = s.Postpartum.Phase switch
                {
                    PostpartumPhase.Immediate => (70.0, 30.0),
                    PostpartumPhase.FirstWeek => (40.0, 45.0),
                    PostpartumPhase.SixWeeks  => (15.0, 65.0),
                    _                         => ( 0.0, 100.0)
                };
                if (s.Postpartum.Phase != PostpartumPhase.FullRecovery)
                    s = s with { Pain = Math.Max(s.Pain, painFloor), Energy = Math.Min(s.Energy, energyCap) };

                _postpartumAccHours += h;
                while (_postpartumAccHours >= 24.0 && s.Postpartum is not null)
                {
                    _postpartumAccHours -= 24.0;
                    s = AdvancePostpartum(s, now, ctx, outbox);
                }
            }

            if (s.Pregnancy is { } pregnancy)
            {
                s = AdvancePregnancy(s, now, ctx, outbox, pregnancy);
            }
            else if (s.Cycle is not null && s.Cycle.Phase != CyclePhase.Paused)
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

        /// <summary>
        /// Reacts to discrete domain events by applying instantaneous state mutations.
        /// Handled events:
        /// <list type="bullet">
        ///   <item><description>
        ///     <see cref="Sleep.SleepEnded"/> — applies sleep-quality-weighted recovery to
        ///     <c>SleepDebtHours</c>, <c>ImmuneLoad</c>, <c>Pain</c>, and <c>Energy</c>.
        ///   </description></item>
        ///   <item><description>
        ///     <see cref="SexualEncounterOutcome"/> with <c>ReproductivePotential = true</c> —
        ///     rolls for conception via <see cref="ConceptionChance"/> and, on success, emits
        ///     <see cref="PregnancyStarted"/> and transitions the cycle to <c>Paused</c>.
        ///   </description></item>
        /// </list>
        /// </summary>
        /// <param name="event">The domain event to react to.</param>
        /// <param name="ctx">Character context, used to check biology and roll random chance.</param>
        /// <param name="outbox">Collector for follow-on events (PregnancyStarted).</param>
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

                        var qualityFactor = se.Quality / 100.0;
                        var remainingDept = s.SleepDebtHours;
                        var maxRecovery = remainingDept * 0.55; // Max 55 % za jednu noc
                        var actualRecovery = Math.Min(maxRecovery, h * 0.9 * qualityFactor);

                        s = s with
                        {
                            // Spánkový dluh: maximální splacení závisí na kvalitě
                            SleepDebtHours = Math.Max(0, remainingDept - actualRecovery),

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

                        using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPhysiologyEngine)))
                        {
                            _log.PhysiologySleepEnded(ctx.Id.Value.ToString(), h, se.Quality, s.SleepDebtHours);
                        }

                        break;
                    }

                case SexualEncounterOutcome se when se.Accepted && se.ReproductivePotential:
                    s = TryStartPregnancy(s, se, ctx, outbox);
                    break;

                case InjuryReceived ir:
                    s = s with { Injury = new InjuryState(Math.Clamp(ir.Severity, 0, 100), 0, ir.Type) };
                    break;
            }

            State = s;
        }

        private PhysiologyState AdvanceCycleDay(PhysiologyState s, WDateTime now, IHumanContext ctx, IEventCollector box)
        {
            var (day, phase) = CalculateCycleProgression(s.Cycle!);
            s = EmitCycleProgressionEvents(s, now, ctx, box, day, phase);
            s = ApplyCycleSymptoms(s);

            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPhysiologyEngine)))
            {
                _log.PhysiologyCycle(ctx.Id.Value.ToString(), s.Cycle!.Phase.ToString(), s.Cycle.DayInCycle);
            }

            return s;
        }

        private (int day, CyclePhase phase) CalculateCycleProgression(MenstrualCycleState c)
        {
            var length = Math.Max(_cycleCfg.MinCycleLengthDays, Math.Min(_cycleCfg.MaxCycleLengthDays,
                _cycleCfg.MeanCycleLengthDays + (int)Math.Round(Normal(_rng, 0, _cycleCfg.VariabilityDaysStdDev))));
            var day = c.DayInCycle + 1;
            if (day > length) day = 1;
            var phase = PhaseFor(day, length, _cycleCfg.MensesMeanDays, _cycleCfg.OvulationDayOfCycle);
            return (day, phase);
        }

        private PhysiologyState EmitCycleProgressionEvents(PhysiologyState s, WDateTime now, IHumanContext ctx, IEventCollector box, int day, CyclePhase phase)
        {
            var c = s.Cycle!;
            box.Add(new CycleDayAdvanced(now, ctx.Id, day, phase));

            var isMenses = phase == CyclePhase.Menses;
            if (!_mensesOn && isMenses) { box.Add(new MensesStarted(now, ctx.Id)); _mensesOn = true; }
            if (_mensesOn && !isMenses) { box.Add(new MensesEnded(now, ctx.Id)); _mensesOn = false; }

            var ovulWindow = phase == CyclePhase.Ovulation;
            if (_cycleCfg.EnableOvulationWindowEvents && ovulWindow && !c.OvulationWindow)
                box.Add(new OvulationWindowOpened(now, ctx.Id));

            var next = c with
            {
                DayInCycle = day, Phase = phase, OvulationWindow = ovulWindow,
                LastMensesStart = (day == 1) ? now.Date : c.LastMensesStart
            };
            return s with { Cycle = next };
        }

        private PhysiologyState ApplyCycleSymptoms(PhysiologyState s)
        {
            var (pain, bloat, tender, libido) = SymptomsFor(s.Cycle!);
            return s with
            {
                Pain = Clamp01p(s.Pain + pain),
                Cycle = s.Cycle with
                {
                    SymptomBloat = Clamp01p(s.Cycle.SymptomBloat + bloat),
                    SymptomBreastTender = Clamp01p(s.Cycle.SymptomBreastTender + tender),
                    LibidoMod = libido
                }
            };
        }

        private PhysiologyState AdvancePregnancy(PhysiologyState s, WDateTime now, IHumanContext ctx, IEventCollector outbox, PregnancyState pregnancy)
        {
            var daysPregnant = pregnancy.ConceivedOn.DaysUntil(now.Date);

            if (!pregnancy.Discovered && daysPregnant >= Config.PregnancyDiscoveryMinDays)
            {
                pregnancy = pregnancy with { Discovered = true, DiscoveredOn = now.Date };
                s = s with { Pregnancy = pregnancy };
                outbox.Add(new PregnancyDiscovered(now, ctx.Id, pregnancy.OtherParent));
                using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPhysiologyEngine)))
                {
                    _log.PhysiologyPregnancyDiscovered(
                        ctx.Id.Value.ToString(),
                        pregnancy.OtherParent.Value.ToString(),
                        daysPregnant);
                }
            }

            if (now.Date >= pregnancy.EstimatedDueDate)
            {
                outbox.Add(new ChildBorn(now, ctx.Id, pregnancy.OtherParent));
                using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPhysiologyEngine)))
                {
                    _log.PhysiologyChildBorn(
                        ctx.Id.Value.ToString(),
                        pregnancy.OtherParent.Value.ToString(),
                        pregnancy.ConceivedOn.ToString(),
                        pregnancy.EstimatedDueDate.ToString());
                }

                return s with
                {
                    Pregnancy  = null,
                    Pain       = 90,  // porod je akutně bolestivý
                    Energy     = 20,  // vyčerpaná po porodu
                    Cycle = s.Cycle is null
                        ? null
                        : s.Cycle with { Phase = CyclePhase.Paused, OvulationWindow = false, LibidoMod = 0.8 },
                    Postpartum = new PostpartumState(0, PostpartumPhase.Immediate)
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

        private PhysiologyState AdvancePostpartum(PhysiologyState s, WDateTime now, IHumanContext ctx, IEventCollector outbox)
        {
            var pp = s.Postpartum!;
            var days = pp.DaysSinceBirth + 1;
            var newPhase = days switch
            {
                <= 3  => PostpartumPhase.Immediate,
                <= 7  => PostpartumPhase.FirstWeek,
                <= 42 => PostpartumPhase.SixWeeks,
                _     => PostpartumPhase.FullRecovery
            };

            if (newPhase != pp.Phase)
                outbox.Add(new PostpartumPhaseChanged(now, ctx.Id, newPhase));

            if (newPhase == PostpartumPhase.FullRecovery)
            {
                return s with
                {
                    Postpartum = null,
                    Cycle = s.Cycle is null ? null : s.Cycle with { Phase = CyclePhase.Follicular, LibidoMod = 1.0 }
                };
            }

            return s with { Postpartum = pp with { DaysSinceBirth = days, Phase = newPhase } };
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
            var conceived = ctx.Random.Chance(conceptionChance);

            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPhysiologyEngine)))
            {
                _log.PhysiologyConceptionEvaluated(
                    ctx.Id.Value.ToString(),
                    otherParent.Value.ToString(),
                    conceptionChance,
                    s.Cycle?.OvulationWindow == true,
                    encounter.Intent.ToString(),
                    encounter.Contraception.ToString(),
                    conceived ? "Conceived" : "NotConceived");
            }

            if (!conceived)
            {
                return s;
            }

            var pregnancy = new PregnancyState(
                otherParent,
                encounter.OccurredAt.Date,
                encounter.OccurredAt.Date.AddDays(Config.PregnancyTermDays));

            outbox.Add(new PregnancyStarted(encounter.OccurredAt, ctx.Id, otherParent, pregnancy.EstimatedDueDate));
            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPhysiologyEngine)))
            {
                _log.PhysiologyPregnancyStarted(
                    ctx.Id.Value.ToString(),
                    otherParent.Value.ToString(),
                    pregnancy.EstimatedDueDate.ToString());
            }

            return s with
            {
                Pregnancy = pregnancy,
                Cycle = s.Cycle is null
                    ? null
                    : s.Cycle with { Phase = CyclePhase.Paused, OvulationWindow = false, LibidoMod = 0.8 }
            };
        }

        /// <summary>
        /// Calculates the probability of conception for a given encounter, clamped to [0.0, 0.65].
        /// </summary>
        /// <remarks>
        /// Calculation pipeline:
        /// <code>
        /// chance = BaseConceptionChancePerEncounter
        ///   × (OvulationConceptionMultiplier if OvulationWindow is open, else 1.0)
        ///   × intent factor  (AvoidPregnancy=0.35, Indifferent=1.0, Open=1.10, TryingForChild=1.35)
        ///   × contraception factor (None=1.0, Low=0.55, Moderate=0.25, High=0.04)
        /// </code>
        /// The intent factor models behavioral differences (e.g., timing intercourse); the
        /// contraception factor models method efficacy. The hard cap of 0.65 prevents edge-case
        /// probabilities from becoming near-certain.
        /// </remarks>
        /// <param name="s">Current physiology state, checked for an open ovulation window.</param>
        /// <param name="encounter">Sexual encounter carrying intent and contraception metadata.</param>
        /// <returns>Conception probability in [0.0, 0.65].</returns>
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

        /// <summary>
        /// Returns per-day physiological symptom deltas for the current cycle phase.
        /// Values represent increments applied once per 24 accumulated game hours.
        /// </summary>
        /// <param name="c">Current menstrual cycle state from which <c>Phase</c> is read.</param>
        /// <returns>
        /// A tuple of (pain delta, bloat delta, breast-tenderness delta, libido multiplier):
        /// <list type="bullet">
        ///   <item><description>
        ///     <b>Menses</b>: Pain +3, Bloat +2, Tenderness +2, LibidoMod 0.90.
        ///     Elevated prostaglandin levels cause cramping and bloating;
        ///     reduced libido is driven by discomfort and low estrogen.
        ///   </description></item>
        ///   <item><description>
        ///     <b>Follicular</b>: Pain −2, Bloat −1, Tenderness −1, LibidoMod 1.05.
        ///     Rising estrogen reduces inflammation; energy and libido recover.
        ///   </description></item>
        ///   <item><description>
        ///     <b>Ovulation</b>: Pain 0, Bloat 0, Tenderness 0, LibidoMod 1.15.
        ///     Peak estrogen and LH surge; libido is highest; symptoms minimal.
        ///   </description></item>
        ///   <item><description>
        ///     <b>Luteal</b>: Pain +1, Bloat +1, Tenderness +1, LibidoMod 0.95.
        ///     Progesterone dominance; mild PMS precursors, slightly reduced libido.
        ///   </description></item>
        ///   <item><description>
        ///     <b>Paused</b> (pregnancy/other): neutral zero deltas, LibidoMod 1.0.
        ///   </description></item>
        /// </list>
        /// </returns>
        private (double pain, double bloat, double tender, double libidoMod) SymptomsFor(MenstrualCycleState c)
        {
            var (rawPain, rawBloat, rawTender, libido) = c.Phase switch
            {
                CyclePhase.Menses     => (+3.0, +2.0, +2.0, 0.90),
                CyclePhase.Follicular => (-2.0, -1.0, -1.0, 1.05),
                CyclePhase.Ovulation  => (+0.0, +0.0, +0.0, 1.15),
                CyclePhase.Luteal     => (+1.0, +1.0, +1.0, 0.95),
                _                     => ( 0.0,  0.0,  0.0, 1.00)
            };
            return (rawPain  * _cycleCfg.PainBaseMultiplier,
                    rawBloat * _cycleCfg.BloatBaseMultiplier,
                    rawTender * _cycleCfg.BreastTenderMultiplier,
                    libido);
        }

        private static CyclePhase PhaseFor(int day, int length, int mensesDays, int ovulationDay)
        {
            if (day <= mensesDays)
            {
                return CyclePhase.Menses;
            }

            if (day < ovulationDay)
            {
                return CyclePhase.Follicular;
            }

            if (day >= ovulationDay && day <= ovulationDay + 1)
            {
                return CyclePhase.Ovulation;
            }

            return CyclePhase.Luteal;
        }

        private static MenstrualCycleState SeedCycle(MenstrualCycleConfig cfg, IRandomSource rng, WDateOnly now)
        {
            var day = rng.Next(1, Math.Max(2, cfg.MeanCycleLengthDays));
            day = Math.Clamp(day, 1, 35);
            var phase = PhaseFor(day, cfg.MeanCycleLengthDays, cfg.MensesMeanDays, cfg.OvulationDayOfCycle);

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

        /// <summary>
        /// Replaces the current state with the provided snapshot.
        /// Used by the persistence layer to reload serialized state after a save/load cycle,
        /// and by tests to set up specific initial conditions.
        /// </summary>
        /// <param name="state">The state to restore.</param>
        public void RestoreState(PhysiologyState state) => State = state;
    }
}
