// DefaultPhysiologyEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using System;
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Core.Astro;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Objects;
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
    /// Continuous drift is applied in <see cref="Tick"/> proportional to elapsed game hours.
    /// Discrete one-time effects (sleep recovery, sexual encounters) are applied in <see cref="Handle"/>.
    /// All state mutations produce a new <see cref="PhysiologyState"/> record; the engine is
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
        private readonly WDateOnly _birthDate;
        private readonly SexBiology _biology;
        private readonly Bereavement.BereavementConfig _bereavementCfg;

        /// <summary>
        /// Optional world object provider for resolving nutritional profiles
        /// of consumed objects. <c>null</c> = use config defaults for all food.
        /// </summary>
        private readonly IWorldObjectProvider? _objectProvider;

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
            WDateOnly now,
            IWorldObjectProvider? objectProvider = null,
            IOptions<Bereavement.BereavementConfig>? bereavementCfg = null)
        {
            Config = cfg.Value;
            _cycleCfg = cycleCfg.Value;
            _birthDate = birthDate;
            _biology = biology;
            _bereavementCfg = bereavementCfg?.Value ?? new Bereavement.BereavementConfig();

            _log = loggerFactory.CreateLogger<DefaultPhysiologyEngine>();
            _rng = rng;

            var initialCycle = (Config.EnableMenstrualCycle && biology == SexBiology.Female && ComputeAgeYears(now) >= Config.MenstrualCycleBeginsInAge && now != default)
                ? SeedCycle(_cycleCfg, rng, now)
                : null;

            var initialTestosterone = (Config.EnableTestosteroneCycle && biology == SexBiology.Male)
                ? new TestosteroneState()
                : null;

            _objectProvider = objectProvider;

            State = new PhysiologyState(
                Energy: 70,
                SleepDebtHours: 2,
                Hunger: 25,
                Thirst: 20,
                Pain: 5,
                ImmuneLoad: 10,
                BodyTempDelta: 0,
                Cycle: initialCycle,
                Nutrition: Config.EnableNutrition ? new NutritionState() : null,
                Testosterone: initialTestosterone,
                Aging: new PhysicalAgingState(AgeYears: now != default ? ComputeAgeYears(now) : 0));

            _mensesOn = initialCycle?.Phase == CyclePhase.Menses;
        }

        /// <summary>
        /// Returns the character's age in whole years, correctly adjusted for birth month and day.
        /// Used for discrete guards (menopause, cycle init, testosterone aging).
        /// </summary>
        private int ComputeAgeYears(WDateOnly today)
        {
            var age = today.Year - _birthDate.Year;
            if (today.Month < _birthDate.Month ||
                (today.Month == _birthDate.Month && today.Day < _birthDate.Day))
                age--;
            return Math.Max(0, age);
        }

        /// <summary>
        /// Returns the character's fractional age in years for continuous growth calculations
        /// (hair greying, wrinkles, sarcopenia). More precise than integer age.
        /// </summary>
        private double ComputeAgeYearsFractional(WDateOnly today)
        {
            // Approximate days elapsed since birth, divided by mean year length.
            var days = today.DayIndex - _birthDate.DayIndex;
            return Math.Max(0.0, days / 365.25);
        }

        #region Drift computation

        /// <summary>
        /// Computes the continuous physiological drift for a single tick as a pure
        /// function of the current action, elapsed hours, and configuration.
        /// </summary>
        /// <remarks>
        /// Single source of truth for baseline action-driven drift. Intentionally
        /// <c>static</c> and side-effect free (no engine fields, no state mutation,
        /// no events, no RNG) so it can be unit-tested exhaustively and can never be
        /// accidentally duplicated in <see cref="Handle"/> — the cause of BUG-1/2/3.
        /// State-dependent inputs (post-menopause immune factor, per-object hydration)
        /// are passed in as parameters, keeping the decision logic in <see cref="Tick"/>.
        /// </remarks>
        /// <param name="action">Current plan name (see <see cref="ActionNames"/>), or null when idle.</param>
        /// <param name="h">Elapsed game hours this tick (already sanitised by <c>SafeHours</c>).</param>
        /// <param name="config">Drift rate configuration.</param>
        /// <param name="hydrationGain">Per-object hydration gain for <c>Drink</c>, or null for the config default.</param>
        /// <param name="immuneDecayFactor">Multiplier on immune recovery (e.g. 0.7 post-menopause); pass 1.0 for default.</param>
        /// <returns>The additive deltas to apply to the physiological state this tick.</returns>
        internal static PhysiologyDrift ComputeDrift(
            string? action,
            double h,
            PhysiologyConfig config,
            double? hydrationGain,
            double immuneDecayFactor)
        {
            // Energy: depletes while awake, slower during self-care, untouched during sleep
            // (sleep recovery is a one-time effect in Handle(SleepEnded), NOT here).
            var energy = action switch
            {
                SelfCare => config.EnergyDriftSelfCarePerHour * h,
                Sleep => 0.0,
                _ => config.EnergyDriftAwakePerHour * h
            };

            // Hunger: rises while awake, slower during sleep, drops while eating.
            // EatStored (pantry/held food, food-economy Tier 1) reduces hunger identically to Eat.
            var hunger = action switch
            {
                Eat or EatStored => config.HungerEatingGainPerHour * h,
                Sleep => config.HungerDriftSleepPerHour * h,
                _ => config.HungerDriftAwakePerHour * h
            };

            // Thirst: same pattern. Drink uses the object's hydration value when supplied.
            var thirst = action switch
            {
                Drink => -(hydrationGain ?? config.ThirstDrinkingGainPerHour) * h,
                Sleep => config.ThirstDriftSleepPerHour * h,
                _ => config.ThirstDriftAwakePerHour * h
            };

            // Pain: passive recovery always; sleep adds its bonus; self-care is strongest.
            var pain = action switch
            {
                SelfCare => -config.PainSelfCareRecoveryPerHour * h,
                Sleep => -(config.PainPassiveRecoveryPerHour + config.PainSleepRecoveryPerHour) * h,
                _ => -config.PainPassiveRecoveryPerHour * h
            };

            // Immune load: recovers slowly by default, faster during self-care; the decay
            // factor (<= 1.0) slows recovery for states such as post-menopause.
            var immune = action switch
            {
                SelfCare => config.ImmuneDriftSelfCarePerHour * h * immuneDecayFactor,
                _ => config.ImmuneDriftAwakePerHour * h * immuneDecayFactor
            };

            return new PhysiologyDrift(energy, hunger, thirst, pain, immune);
        }

        #endregion Drift computation

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

            // Dead characters do not drift — death is terminal. This also covers the
            // combat path: once CharacterDied is handled in Phase A (Status → Dead),
            // any remaining Phase B physiology tick is a no-op.
            if (s.Status == StatusType.Dead)
                return;

            var action = ctx.Snapshot.Behavior.CurrentPlan?.Name;
            var nutProfile = ResolveNutritionalProfile(ctx);

            // Post-menopause: estrogen lost → slower immune recovery (30% slowdown).
            // This state-driven factor stays in Tick() and enters ComputeDrift as a parameter.
            var isPostMenopauseImmune = s.Cycle?.Phase == CyclePhase.Paused
                && s.Pregnancy is null && (s.Aging?.AgeYears ?? 0) >= 45;
            var immuneDecayFactor = isPostMenopauseImmune ? 0.7 : 1.0;

            // DECISION (2026-06, peer-review verified): NOT modeling a cyclical (within-cycle) estradiol→
            // immune-recovery coupling here, despite the symmetry with the menopause factor above.
            // Estrogen's immune effect is biphasic/dose-dependent (Calabrese 2001, biphasic dose-response
            // review), and the female autoimmune burden (~78.8% of autoimmune-disease patients are women
            // — Fairweather & Rose) shows "more estrogen = better immunity" is too simple and, for
            // autoimmunity, roughly backwards. The best within-cycle meta-analysis (Notbohm et al. 2023,
            // Acta Physiologica 238:e14013; 159 studies) found NO systematic follicular-vs-luteal
            // difference in CRP or adaptive immune markers — only some innate cell counts, which is not
            // the same as recovery speed. The post-menopause factor above stays unchanged: it rests on a
            // different, more robust evidence base (chronic estrogen-loss state, not within-cycle
            // fluctuation).

            // Single source of truth for action-driven drift (energy, hunger, thirst, pain, immune).
            var drift = ComputeDrift(action, h, Config, nutProfile?.HydrationGain, immuneDecayFactor);

            var feverDelta = s.ImmuneLoad > 30 ? (s.ImmuneLoad - 30) / 70.0 * 2.0 : 0.0;
            // Circadian body temperature: a sinusoidal wave ±CircadianTempAmplitude (Waterhouse 2005)
            var hoursOfDayT = (double)(now.Hour % WWorld.Spec.HoursPerDay);
            // Anchor the peak at SolarNoon+5h when we have an astronomical context (Waterhouse: body temperature ~5h after noon)
            var circTempPeakHour = ctx.Snapshot.Celestial is { } celT && !double.IsNaN(celT.SolarNoonHour)
                ? celT.SolarNoonHour + 5.0
                : Config.CircadianTempPeakHour;
            // cos: maximum na circTempPeakHour, minimum na ±HalfDay
            var circadianTempComponent = Config.CircadianTempAmplitude
                * Math.Cos((hoursOfDayT - circTempPeakHour) * 2 * Math.PI / WWorld.Spec.HoursPerDay);
            var targetBodyTemp = feverDelta + circadianTempComponent;

            s = s with
            {
                Energy = Clamp01p(s.Energy + drift.Energy),
                Hunger = Clamp01p(s.Hunger + drift.Hunger),
                Thirst = Clamp01p(s.Thirst + drift.Thirst),
                Pain = Clamp01p(s.Pain + drift.Pain),
                ImmuneLoad = Clamp01p(s.ImmuneLoad + drift.Immune),
                BodyTempDelta = Math.Clamp(Approach(s.BodyTempDelta, targetBodyTemp, 0.1 * h), -1.0, 3.5)
            };

            // Nutrition drift — Calories/Protein decline and are replenished by eating;
            // Iron is restored by eating iron-rich food and decays passively; VitaminD declines slowly
            if (s.Nutrition is { } nut)
            {
                // Both Eat (world Food object) and EatStored (pantry/held food, Tier 1) count as a meal.
                var isEating = action is Eat or EatStored;
                var caloriesDelta = isEating ? (nutProfile?.CalorieGain ?? Config.CaloriesEatingGainPerHour) * h : -Config.NutritionDecayPerHour * h;
                var proteinDelta = isEating ? (nutProfile?.ProteinGain ?? Config.ProteinEatingGainPerHour) * h : -Config.NutritionDecayPerHour * h;
                // Iron is restored by eating iron-rich food (see NutritionalProfile.IronGain) and otherwise
                // decays slowly at a fixed passive rate. There is NO scientific basis for faster iron
                // restoration during sleep: hepcidin (the hormone that suppresses dietary iron absorption)
                // is LOWEST in the early morning and RISES 2-6x by afternoon, meaning absorption capacity is
                // highest in the morning, not overnight.
                // Source: Kemna EHJM et al., Clin Chem 2007;53(4):620-628 (primary study)
                // Source: Schaap CCM et al., Clin Chem 2013;59(3):527-535, DOI 10.1373/clinchem.2012.194977 (primary study)
                var ironDelta = isEating
                    ? (nutProfile?.IronGain ?? Config.IronEatingGainPerHour) * h
                    : -Config.IronDecayPerHour * h;
                // Glycemic state: a spike when eating, a rebound dip 1–2 h after a meal
                var glucoseDelta = isEating ? Config.BloodGlucoseEatingGain * h : 0.0;
                var postMealHours = isEating ? 0.0 : nut.PostMealHours + h;
                var inDipWindow = postMealHours > Config.BloodGlucoseDipStartHours
                                 && postMealHours < Config.BloodGlucoseDipEndHours;
                var glucoseDecay = Config.BloodGlucoseBaseDecayPerHour + (inDipWindow ? Config.BloodGlucoseDipDecayBonus : 0);
                if (!isEating) glucoseDelta -= glucoseDecay * h;

                s = s with
                {
                    Nutrition = nut with
                    {
                        Calories = Clamp01p(nut.Calories + caloriesDelta),
                        VitaminD = ComputeVitaminD(nut.VitaminD, h, ctx.Snapshot.Celestial, ctx.Snapshot.InteractionSurface.Kind),
                        Iron = Clamp01p(nut.Iron + ironDelta),
                        Protein = Clamp01p(nut.Protein + proteinDelta),
                        BloodGlucoseLevel = Math.Clamp(nut.BloodGlucoseLevel + glucoseDelta, 0, 100),
                        PostMealHours = postMealHours
                    }
                };
            }

            // Injury — adds pain and heals gradually (once per 24 h)
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

            // Postpartum — minimal pain and maximum energy depending on the phase
            if (s.Postpartum is not null)
            {
                var (painFloor, energyCap) = s.Postpartum.Phase switch
                {
                    PostpartumPhase.Immediate => (70.0, 30.0),
                    PostpartumPhase.FirstWeek => (40.0, 45.0),
                    PostpartumPhase.SixWeeks => (15.0, 65.0),
                    _ => (0.0, 100.0)
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

            // Allostatic load — accumulates under chronic neglect of needs
            {
                var alloAccum = 0.0;
                if (s.Hunger > Config.AllostaticLoadThresholdHunger) alloAccum += Config.AllostaticLoadAccumRatePerHour * h;
                if (s.Thirst > Config.AllostaticLoadThresholdThirst) alloAccum += Config.AllostaticLoadAccumRatePerHour * h;
                if (s.SleepDebtHours > Config.AllostaticLoadThresholdSleepDebt) alloAccum += Config.AllostaticLoadAccumRatePerHour * h;
                if (s.Pain > Config.AllostaticLoadThresholdPain) alloAccum += Config.AllostaticLoadAccumRatePerHour * h;
                if (s.ImmuneLoad > Config.AllostaticLoadThresholdImmune) alloAccum += Config.AllostaticLoadAccumRatePerHour * h;

                var alloDecay = action switch
                {
                    Sleep or SelfCare => Config.AllostaticLoadDecayRatePerHour * h,
                    Idle => Config.AllostaticLoadDecayRatePerHour * Config.AllostaticLoadIdleDecayFactor * h,
                    _ => 0
                };

                s = s with { AllostaticLoad = Math.Clamp(s.AllostaticLoad + alloAccum - alloDecay, 0, 100) };
            }

            // Sleep inertia — linear decay after waking
            if (s.SleepInertiaHours > 0)
                s = s with { SleepInertiaHours = Math.Max(0, s.SleepInertiaHours - h) };

            // Borbély two-process sleep regulation (Process S) + Van Dongen cognitive deficit.
            // Process S saturates; the cognitive deficit does not — preserving the dissociation.
            {
                var asleep = action == Sleep;
                // Seed on first tick from existing sleep debt so restored saves behave sensibly.
                var processS = s.ProcessS ?? Math.Clamp(0.3 + s.SleepDebtHours / Math.Max(1.0, Config.MaxSleepDebtHours) * 0.5, 0.0, 1.0);
                var deficit = s.CognitiveDeficit ?? 0.0;

                processS = asleep
                    ? SleepRegulationCalculator.DecayProcessS(processS, h, Config)
                    : SleepRegulationCalculator.BuildupProcessS(processS, h, Config);
                deficit = SleepRegulationCalculator.UpdateCognitiveDeficit(deficit, processS, h, asleep, Config);

                s = s with
                {
                    ProcessS = Math.Clamp(processS, 0.0, 1.0),
                    CognitiveDeficit = Math.Max(0.0, deficit)
                };
            }

            // SAM system — very fast decay (Sympatho-Adrenomedullary, adrenaline/noradrenaline)
            if (s.AcuteArousalLevel > 0)
                s = s with { AcuteArousalLevel = Math.Max(0, s.AcuteArousalLevel - Config.AcuteArousalDecayPerHour * h) };

            // Physical fatigue — accumulation during Work, decay during rest/sleep
            // Sarcopenia: less muscle mass = Work fatigue accumulates faster
            {
                var muscleFactor = s.Aging?.MuscleMassFraction ?? 1.0;
                var gravityFactor = ctx.Snapshot.Celestial?.SurfaceGravityVsEarth ?? 1.0;
                var fatigueDelta = action switch
                {
                    Work => (Config.PhysicalFatigueAccumPerWorkHour / Math.Max(0.1, muscleFactor)) * gravityFactor * h,
                    Sleep => -Config.PhysicalFatigueDecayPerSleepHour * h,
                    Idle => -Config.PhysicalFatigueDecayPerIdleHour * h,
                    SelfCare => -(Config.PhysicalFatigueDecayPerIdleHour + Config.PhysicalFatigueSelfCareDecayBonus) * h,
                    _ => -Config.PhysicalFatigueDecayPerIdleHour * 0.5 * h
                };
                s = s with { PhysicalFatigueLevel = Math.Clamp(s.PhysicalFatigueLevel + fatigueDelta, 0, 100) };
            }

            // Chronic pain — a cumulative counter (Dantzer 2008)
            if (s.Pain > Config.ChronicPainAccumThreshold)
                s = s with { ChronicPainDays = s.ChronicPainDays + h / 24.0 };
            else
                s = s with { ChronicPainDays = Math.Max(0, s.ChronicPainDays - h / (24.0 * Config.ChronicPainDecayFactor * 2)) };

            // Cortisol — diurnal curve (HPA axis) + chronic stress + immune elevation
            {
                var hoursOfDay = (double)(now.Hour % WWorld.Spec.HoursPerDay);
                // Anchor CAR (the Cortisol Awakening Response) at SunriseHour+2h (the light signal sets the peak)
                var cortisolPeakHour = ctx.Snapshot.Celestial is { } celC && !double.IsNaN(celC.SunriseHour)
                    ? celC.SunriseHour + 2.0
                    : Config.CortisolDiurnalPeakHour;
                var diurnal = Config.CortisolDiurnalAmplitude
                              * Math.Exp(-Math.Pow(hoursOfDay - cortisolPeakHour, 2) / 8.0);
                // Hypocortisolism paradox (Fries 2005): under extreme AlloLoad the HPA axis downregulates
                var alloComponent = s.AllostaticLoad < Config.HypocortisolismAlloThreshold
                    ? s.AllostaticLoad * Config.CortisolAlloWeight
                    : Math.Max(0, Config.HypocortisolismAlloThreshold * Config.CortisolAlloWeight
                               - (s.AllostaticLoad - Config.HypocortisolismAlloThreshold) * Config.HypocortisolismDeclineRate);
                var immuneComponent = Math.Max(0, s.ImmuneLoad - 40) * Config.CortisolImmuneWeight;
                var targetCortisol = Math.Clamp(50 + diurnal + alloComponent + immuneComponent, 0, 100);
                // Faster up (CAR), slower down — biologically faithful
                var cortRate = targetCortisol > s.CortisolLevel ? 20.0 * h : 8.0 * h;
                s = s with { CortisolLevel = Math.Clamp(Approach(s.CortisolLevel, targetCortisol, cortRate), 0, 100) };
            }

            // Circadian phase shift — the social jet-lag model
            {
                var hoursPerDay = (double)WWorld.Spec.HoursPerDay;
                var hoursOfDay = (double)(now.Hour % WWorld.Spec.HoursPerDay);
                var naturalSleep = (Config.NaturalSleepStartHour - Config.ChronotypeOffsetHours + hoursPerDay) % hoursPerDay;
                if (action == Sleep)
                {
                    var mismatch = Math.Abs(hoursOfDay - naturalSleep);
                    if (mismatch > hoursPerDay / 2) mismatch = hoursPerDay - mismatch;
                    if (mismatch > 2.0)
                        s = s with
                        {
                            CircadianPhaseShiftHours = Math.Clamp(
                            s.CircadianPhaseShiftHours + (mismatch - 2.0) * 0.05 * h, -6, 6)
                        };
                }
                // Slow recovery toward the chronotype (the body resynchronizes ~1 h/day)
                s = s with
                {
                    CircadianPhaseShiftHours = Approach(
                    s.CircadianPhaseShiftHours, Config.ChronotypeOffsetHours, Config.CircadianPhaseRecoveryPerHour * h)
                };
            }

            // Recovery debt — a physical recovery deficit beyond sleep debt
            {
                if (s.AllostaticLoad > Config.RecoveryDebtAccumAlloThreshold)
                    s = s with
                    {
                        RecoveryDebtHours = Math.Min(72,
                        s.RecoveryDebtHours + Config.RecoveryDebtAccumRatePerHour * h)
                    };
                var debtDecay = action switch
                {
                    Sleep => Config.RecoveryDebtDecayPerSleepHour * h,
                    SelfCare => Config.RecoveryDebtDecayPerSelfCareHour * h,
                    _ => 0.0
                };
                s = s with { RecoveryDebtHours = Math.Max(0, s.RecoveryDebtHours - debtDecay) };
            }

            // Age effects
            {
                var ageYears = ComputeAgeYears(now.Date);

                // Menopause: women ≥ MenopauseAge → cycle permanently Paused
                if (s.Cycle is { Phase: not CyclePhase.Paused } && ageYears >= Config.MenopauseAge)
                    s = s with { Cycle = s.Cycle with { Phase = CyclePhase.Paused, OvulationWindow = false, LibidoMod = 1.0 } };

                // Testosterone aging in men (~1%/year after 25)
                if (s.Testosterone is { } ageTesto && ageYears > Config.AgingTestosteronePenaltyStart)
                {
                    var agePenalty = (ageYears - Config.AgingTestosteronePenaltyStart) * Config.AgingTestosteronePenaltyPerYear;
                    s = s with { Testosterone = ageTesto with { Level = Math.Max(0, ageTesto.Level - agePenalty * h / (365.25 * 24)) } };
                }

                // Inflammaging: nad 60 → baseline ImmuneLoad pomalu roste
                if (ageYears > Config.AgingImmuneBaselineStart)
                {
                    var ageImmuneBonus = (ageYears - Config.AgingImmuneBaselineStart) * Config.AgingImmuneBaselinePerYear;
                    s = s with { ImmuneLoad = Math.Min(100, s.ImmuneLoad + ageImmuneBonus * h / (365.25 * 24)) };
                }
            }

            // Physical aging — hair, wrinkles, muscle mass, bone density
            if (s.Aging is { } aging)
            {
                var ageYears = ComputeAgeYearsFractional(now.Date);
                var ageYearsInt = ComputeAgeYears(now.Date);

                // Hair growth (~1.25 cm/month in reality, ~0.00175 cm/h in the game world)
                var newHairLen = Math.Min(120.0, aging.HairLengthCm + Config.HairGrowthCmPerHour * h);

                // Greying: age + cortisol accelerator
                var greying = 0.0;
                if (ageYears > Config.HairGreyingAgeStart)
                {
                    greying += (ageYears - Config.HairGreyingAgeStart) * Config.HairGreyingRatePerYear * h / (365.25 * 24);
                    greying += s.CortisolLevel * Config.HairGreyingCortisolBoost * h;
                }

                // Hair density: androgenic alopecia (men) + stress
                var densityChange = 0.0;
                if (s.Testosterone is { } _ && ageYears > Config.HairLossAgeStartMale)
                    densityChange -= (ageYears - Config.HairLossAgeStartMale) * Config.HairLossRatePerYearMale * h / (365.25 * 24);
                if (s.AllostaticLoad > Config.HairLossStressThreshold)
                    densityChange -= Config.HairLossStressRate * h;
                else
                    densityChange += Config.HairDensityRecoveryPerHour * h;

                // Wrinkles: age + cortisol
                var wrinkles = 0.0;
                if (ageYears > Config.WrinklingAgeStart)
                {
                    wrinkles += (ageYears - Config.WrinklingAgeStart) * Config.WrinklingRatePerYear * h / (365.25 * 24);
                    wrinkles += s.CortisolLevel * Config.WrinklingCortisolBoost * h;
                }

                // Sarcopenia: decline in muscle mass after age 30
                var muscleChange = ageYears > Config.SarcopeniaAgeStart
                    ? -(ageYears - Config.SarcopeniaAgeStart) * Config.SarcopeniaRatePerYear * h / (365.25 * 24)
                    : 0.0;

                // Bone density: aging + post-menopause
                var isPostMenoForBone = s.Cycle?.Phase == CyclePhase.Paused
                    && s.Pregnancy is null && ageYears >= Config.MenopauseAge;
                var boneDeclineRate = ageYears > Config.BoneDensityDeclineAgeStart
                    ? Config.BoneDensityDeclinePerYear * (isPostMenoForBone ? Config.BoneDensityMenopauseMultiplier : 1.0)
                    : 0.0;
                var newBoneDensity = Math.Max(0.2, aging.BoneDensity - boneDeclineRate * h / (365.25 * 24));

                s = s with
                {
                    Aging = aging with
                    {
                        AgeYears = ageYearsInt,
                        HairLengthCm = newHairLen,
                        GreyFraction = Math.Clamp(aging.GreyFraction + greying, 0, 1),
                        HairDensity = Math.Clamp(aging.HairDensity + densityChange, 0.1, 1),
                        WrinkleScore = Math.Clamp(aging.WrinkleScore + wrinkles, 0, 100),
                        MuscleMassFraction = Math.Clamp(aging.MuscleMassFraction + muscleChange,
                                                        Config.SarcopeniaMuscleMin, 1.0),
                        BoneDensity = newBoneDensity
                    }
                };
            }

            // Altitude — hypoxie a AMS
            {
                var alt = ctx.Snapshot.AltitudeMeters;
                if (alt > Config.AltitudeHypoxiaThreshold)
                {
                    var kmAbove = (alt - Config.AltitudeHypoxiaThreshold) / 1000.0;
                    s = s with { Energy = Clamp01p(s.Energy - Config.AltitudeEnergyDecayBonusPerKm * kmAbove * h) };
                    if (alt > Config.AltitudeAMSThreshold)
                        s = s with { Pain = Clamp01p(s.Pain + Config.AltitudeAMSPainPerHour * h) };
                }
            }

            // Chronic social isolation → cortisol (Cacioppo 2015)
            {
                var needSocial = ctx.Snapshot.Psychology?.Motivations?.NeedSocial ?? 50;
                if (needSocial > Config.SocialIsolationCortisolThreshold)
                {
                    var isolSeverity = Math.Min((needSocial - Config.SocialIsolationCortisolThreshold) / 20.0, 1.0);
                    s = s with { CortisolLevel = Math.Clamp(s.CortisolLevel + isolSeverity * Config.SocialIsolationCortisolRatePerHour * h, 0, 100) };
                }
            }

            // Testosterone — diurnal rhythm + HPA-HPG cross-talk + sleep debt (men only)
            if (s.Testosterone is { } testo)
            {
                var hoursOfDay = (double)(now.Hour % WWorld.Spec.HoursPerDay);
                var diurnal = 20.0 * Math.Exp(-Math.Pow(hoursOfDay - Config.TestosteronePeakHour, 2) / 10.0);
                var alloSuppression = Math.Max(0, s.AllostaticLoad - 50) / 10.0 * Config.TestosteroneAlloSuppression;
                var sleepPenalty = Math.Max(0, s.SleepDebtHours - 2) * Config.TestosteroneSleepDebtPenaltyPerHour;
                var targetLevel = Math.Clamp(50 + diurnal - alloSuppression - sleepPenalty, 0, 100);
                s = s with
                {
                    Testosterone = testo with
                    { Level = Math.Clamp(Approach(testo.Level, targetLevel, 10.0 * h), 0, 100) }
                };
            }

            #region Natural mortality check

            {
                var ageYears = now.Date.Year - _birthDate.Year;
                if (ageYears >= Config.NaturalMortalityGompertzStart || HasCriticalState(s))
                {
                    var risk = NaturalMortalityCalculator.ComputeHourlyRisk(s, ageYears, Config);

                    // Widowhood effect: a recently-bereaved surviving partner carries an elevated
                    // mortality hazard (acute cardiovascular stress), strongest in the first ~6 months
                    // and worse for men. Moon 2011; Shor 2012; Parkes 1969.
                    risk *= WidowhoodHazardMultiplier(ctx, now);

                    // P(death in dt hours) = 1 − (1 − risk_per_hour)^dt
                    var tickRisk = 1.0 - Math.Pow(1.0 - risk, h);
                    if (ctx.Random.Chance(tickRisk))
                    {
                        var cause = NaturalMortalityCalculator.ResolveCause(s, ageYears, Config);
                        using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPhysiologyEngine)))
                        {
                            _log.NaturalDeathOccurred(ctx.Id.Value.ToString(), cause.ToString(), ageYears, risk);
                        }
                        State = s with { Status = StatusType.Dead };
                        outbox.Add(new CharacterDied(now, ctx.Id, cause));

                        return;
                    }
                }
            }

            #endregion Natural mortality check

            State = s;
        }

        /// <summary>
        /// Resolves the nutritional profile of the object currently being consumed,
        /// or <c>null</c> when no provider is wired or the current action is not
        /// food/drink-related.
        /// </summary>
        private NutritionalProfile? ResolveNutritionalProfile(IHumanContext ctx)
        {
            if (_objectProvider is null)
                return null;

            var interaction = ctx.Snapshot.Behavior.CurrentPlan?.ObjectInteraction;
            if (interaction is null)
                return null;

            return _objectProvider.FindObject(interaction.ObjectId)?.NutritionalProfile;
        }

        /// <summary>
        /// The widowhood mortality-hazard multiplier (≥1) for this character, derived from any active
        /// partner-loss in the bereavement snapshot: <see cref="Bereavement.BereavementConfig.WidowhoodHazardFirst"/>
        /// during the acute window, tapering to <see cref="Bereavement.BereavementConfig.WidowhoodHazardTail"/>,
        /// then 1.0. Male survivors are scaled by <see cref="Bereavement.BereavementConfig.WidowhoodMaleFactor"/>.
        /// Returns 1.0 when the character has no partner loss (no effect, preserves legacy behaviour).
        /// </summary>
        private double WidowhoodHazardMultiplier(IHumanContext ctx, WDateTime now)
            => Bereavement.BereavementMath.WidowhoodHazardMultiplier(
                ctx.Snapshot.Bereavement, _biology, now, _bereavementCfg);

        private bool HasCriticalState(PhysiologyState s) =>
            s.Hunger >= Config.NaturalMortalityStarvationThreshold
            || s.Thirst >= Config.NaturalMortalityDehydrationThreshold
            || (s.Energy <= Config.NaturalMortalityExhaustionEnergyMax && s.SleepDebtHours >= Config.NaturalMortalityExhaustionSleepDebtMin)
            || s.AllostaticLoad >= Config.NaturalMortalityAlloThreshold
            || s.ImmuneLoad >= Config.NaturalMortalityImmuneThreshold;

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
                // --- End of the sleep session ---
                // Continuous drift (energy, hunger, thirst) happens in Tick() via CurrentPlan.Name == "Sleep".
                // Here we apply a one-time aggregate effect based on the quality and duration of sleep.
                case Sleep.SleepEnded se:
                    {
                        var h = Math.Max(0, se.TotalHoursSlept);
                        var rawQuality = se.Quality / 100.0;

                        // Age factor on sleep quality: less deep sleep after 50 → worse effective quality
                        var sleepAgeYears = se.OccurredAt.Date.Year - _birthDate.Year;
                        var ageQualityFactor = sleepAgeYears > Config.AgeSleepQualityThreshold
                            ? Math.Max(0.5, 1.0 - (sleepAgeYears - Config.AgeSleepQualityThreshold) * Config.AgeSleepQualityPenaltyPerYear)
                            : 1.0;
                        var qualityFactor = rawQuality * ageQualityFactor;

                        var remainingDept = s.SleepDebtHours;
                        var maxRecovery = remainingDept * 0.55; // Max 55 % za jednu noc
                        var actualRecovery = Math.Min(maxRecovery, h * 0.9 * qualityFactor);

                        // Recovery debt slows energy recovery (min. 30% efficiency)
                        var recoveryFactor = Math.Max(0.3, 1.0 - s.RecoveryDebtHours / 48.0);
                        // Age factor: energy recovers more slowly after age 40
                        var ageFactor = sleepAgeYears > Config.AgingEnergyRecoveryPenaltyStart
                            ? Math.Max(0.3, 1.0 - (sleepAgeYears - Config.AgingEnergyRecoveryPenaltyStart) * Config.AgingEnergyRecoveryPenaltyPerYear)
                            : 1.0;
                        // Sleep inertia: worse quality = longer inertia (quality=100 → 0.75h; quality=0 → 1.5h)
                        var inertiaHours = Config.SleepInertiaMaxHours * (1.0 - se.Quality / 100.0 * 0.5);

                        s = s with
                        {
                            // Sleep debt: the maximum repayment depends on quality
                            SleepDebtHours = Math.Max(0, remainingDept - actualRecovery),

                            // Immune system: deep-sleep regeneration
                            ImmuneLoad = Clamp01p(s.ImmuneLoad - 3.0 * qualityFactor),

                            Pain = rawQuality >= 0.40
                                ? Clamp01p(s.Pain - 5.0 * qualityFactor)
                                : s.Pain,

                            // Energy is restored by sleep; recovery is reduced under recovery debt and age
                            Energy = Clamp01p(s.Energy + h * Config.EnergyRecoveryPerSleepHour * qualityFactor * recoveryFactor * ageFactor),

                            // Sleep inertia — cognitive sluggishness after waking
                            SleepInertiaHours = inertiaHours
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
                    {
                        // Bone fragility: low bone density → higher effective injury severity
                        var boneFactor = s.Aging is { BoneDensity: < 1.0 } bone
                            ? 1.0 + (1.0 - bone.BoneDensity) * Config.BoneFragilityInjuryMultiplier
                            : 1.0;
                        var effectiveSeverity = Math.Clamp(ir.Severity * boneFactor, 0, 100);
                        s = s with
                        {
                            Injury = new InjuryState(effectiveSeverity, 0, ir.Type),
                            AcuteArousalLevel = Math.Min(100, s.AcuteArousalLevel + Config.InjuryAcuteArousalSpike)
                        };
                        break;
                    }

                // Social pain = physical pain (Eisenberger et al., 2003) — rejection activates the HPA axis
                case InteractionOutcome io when io.From == ctx.Id && !io.Accepted:
                    {
                        var n = ctx.Personality.BigFive.Neuroticism;
                        var spike = Config.SocialPainCortisolSpike * (1.0 + n * 0.5);
                        s = s with { CortisolLevel = Math.Clamp(s.CortisolLevel + spike, 0, 100) };
                        break;
                    }

                // Social support as a cortisol buffer (Eisenberger 2007):
                // an accepted interaction from a close person dampens HPA activity
                case InteractionOutcome io when io.To == ctx.Id && io.Accepted:
                    {
                        ctx.Snapshot.Relationships.Edges.TryGetValue(io.From, out var edge);
                        var closeness = edge?.Closeness ?? 0;
                        if (closeness >= Config.SocialSupportClosenessThreshold)
                        {
                            var bufferStrength = (closeness - Config.SocialSupportClosenessThreshold) / 50.0;
                            s = s with { CortisolLevel = Math.Max(0, s.CortisolLevel - Config.SocialSupportCortisolBuffer * bufferStrength) };
                        }
                        break;
                    }

                case ContraceptionChanged cc:
                    s = s with { CurrentContraception = cc.Level };
                    break;

                case HairCut hc:
                    if (s.Aging is { } hairAging)
                        s = s with { Aging = hairAging with { HairLengthCm = Math.Clamp(hc.NewLengthCm, 0, 120) } };
                    break;

                case HairDyed:
                    if (s.Aging is { } dyedAging)
                        s = s with { Aging = dyedAging with { GreyFraction = 0.0 } };
                    break;

                case BreastfeedingChanged bc:
                    if (s.Postpartum is { } ppBf)
                        s = s with { Postpartum = ppBf with { IsBreastfeeding = bc.IsBreastfeeding } };
                    break;

                // Combat death routed in from CharacterBase.DecreaseHealth() via the inbox.
                // Mirror it into the persisted state so the snapshot reflects death from any
                // cause — not just the natural-mortality path in Tick().
                case CharacterDied:
                    s = s with { Status = StatusType.Dead };
                    break;

                // Object affordance applied via UseInPlace (DefaultObjectInteractionEngine →
                // AffordanceApplicationService). Hunger/Thirst reduction is proportional to
                // the object's satisfaction value — roast beef (0.80) beats an apple (0.25).
                case Objects.ObjectAffordanceApplied oaa when oaa.Actor == ctx.Id:
                    s = ApplyObjectAffordance(s, oaa);
                    break;
            }

            // SAM spiky mimo switch — NightmareTriggered a StressSpiked
            if (@event is Sleep.NightmareTriggered)
                s = s with { AcuteArousalLevel = Math.Min(100, s.AcuteArousalLevel + Config.NightmareAcuteArousalSpike) };
            if (@event is Psychology.StressSpiked sp2 && sp2.NewStress > 70)
            {
                var samSpike = (sp2.NewStress - 70) * Config.StressSpikedAcuteArousalWeight;
                s = s with { AcuteArousalLevel = Math.Min(100, s.AcuteArousalLevel + samSpike) };
            }

            State = s;
        }

        private PhysiologyState AdvanceCycleDay(PhysiologyState s, WDateTime now, IHumanContext ctx, IEventCollector box)
        {
            if (s.Cycle is null || s.Cycle.Phase == CyclePhase.Paused)
                return s;

            var (day, phase, length, ovulDay) = CalculateCycleProgression(s.Cycle!);

            s = s with { Cycle = UpdateSuppressionAccumulator(s, ctx, now, box) };

            s = EmitCycleProgressionEvents(s, now, ctx, box, day, phase, length, ovulDay);
            s = ApplyCycleSymptoms(s);

            // PhysiologyCycle (5001) is logged once per tick by OrchestratedHuman.LogState
            // (alongside the 5000/5100/5200 snapshots). Logging it here too double-sourced it.

            return s;
        }

        private (int day, CyclePhase phase, int length, int ovulDay) CalculateCycleProgression(MenstrualCycleState c)
        {
            var length = Math.Max(_cycleCfg.MinCycleLengthDays, Math.Min(_cycleCfg.MaxCycleLengthDays,
                _cycleCfg.MeanCycleLengthDays + (int)Math.Round(Normal(_rng, 0, _cycleCfg.VariabilityDaysStdDev))));
            // Ovulation = cycle length − mean luteal phase. Luteal length ≈ 11.7-12.4 days across two
            // pooled cohort studies (Najmabadi et al. 2020: 11.7 days SD 2.8; Bull et al. 2019: 12.4 days,
            // 95% CI 7-17) — NOT the textbook fixed 14 days.
            // The follicular phase is the main source of variability — ovulDay changes every cycle.
            var ovulDay = Math.Max(_cycleCfg.MensesMeanDays + 2, length - _cycleCfg.LutealMeanDays);
            var day = c.DayInCycle + 1;
            if (day > length) day = 1;
            var phase = PhaseFor(day, length, _cycleCfg.MensesMeanDays, ovulDay);
            return (day, phase, length, ovulDay);
        }

        private PhysiologyState EmitCycleProgressionEvents(PhysiologyState s, WDateTime now, IHumanContext ctx, IEventCollector box, int day, CyclePhase phase, int length, int ovulDay)
        {
            var c = s.Cycle!;
            box.Add(new CycleDayAdvanced(now, ctx.Id, day, phase));

            var isMenses = phase == CyclePhase.Menses;
            if (!_mensesOn && isMenses) { box.Add(new MensesStarted(now, ctx.Id)); _mensesOn = true; }
            if (_mensesOn && !isMenses) { box.Add(new MensesEnded(now, ctx.Id)); _mensesOn = false; }

            // High/Moderate contraception suppresses ovulation
            // AnovulatoryCycleActive suppresses ovulation due to HPA suppression.
            var contraceptionSuppressesOvul = s.CurrentContraception is ContraceptionLevel.High or ContraceptionLevel.Moderate;
            var ovulWindow = phase == CyclePhase.Ovulation && !contraceptionSuppressesOvul && !s.Cycle!.AnovulatoryCycleActive;
            if (_cycleCfg.EnableOvulationWindowEvents && ovulWindow && !c.OvulationWindow)
                box.Add(new OvulationWindowOpened(now, ctx.Id));

            // On reset to day 1, store the actual length of this cycle (ovulDay is then computed from it).
            var currentLength = day == 1 ? length : c.CurrentCycleLength;

            var next = c with
            {
                DayInCycle = day,
                Phase = phase,
                OvulationWindow = ovulWindow,
                LastMensesStart = (day == 1) ? now.Date : c.LastMensesStart,
                CurrentCycleLength = currentLength
            };
            return s with { Cycle = next };
        }

        private PhysiologyState ApplyCycleSymptoms(PhysiologyState s)
        {
            var (pain, bloatTarget, tenderTarget, libido, estradiol, progesterone) = SymptomsFor(s.Cycle!, s.CurrentContraception);
            var day = (double)s.Cycle!.DayInCycle;
            var ovulDay = (double)Math.Max(_cycleCfg.MensesMeanDays + 2, s.Cycle.CurrentCycleLength - _cycleCfg.LutealMeanDays);
            var lutealFactor = Math.Max(0, (day - (ovulDay + 7)) / 7.0);
            var isPmddActive = _cycleCfg.PmsRisk > 0.3 && lutealFactor > 0.5;
            return s with
            {
                Pain = Clamp01p(s.Pain + pain),
                Cycle = s.Cycle with
                {
                    SymptomBloat = ApproachClamped(s.Cycle.SymptomBloat, bloatTarget, _cycleCfg.SymptomTrackingRatePerDay),
                    SymptomBreastTender = ApproachClamped(s.Cycle.SymptomBreastTender, tenderTarget, _cycleCfg.SymptomTrackingRatePerDay),
                    LibidoMod = libido,
                    PmddActive = isPmddActive,
                    Estradiol = estradiol,
                    Progesterone = progesterone
                }
            };
        }

        private PhysiologyState AdvancePregnancy(PhysiologyState s, WDateTime now, IHumanContext ctx, IEventCollector outbox, PregnancyState pregnancy)
        {
            var daysPregnant = pregnancy.ConceivedOn.DaysUntil(now.Date);
            // LibidoMod per trimester (Basson 2006 review): 1st tri ↓ (nausea/fatigue), 2nd tri ↑, 3rd tri ↓↓
            var pregnancyLibidoMod = daysPregnant < 93 ? 0.5 : daysPregnant < 186 ? 0.8 : 0.4;

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
                    Pregnancy = null,
                    Pain = 90,  // porod je akutně bolestivý
                    Energy = 20,  // vyčerpaná po porodu
                    Cycle = s.Cycle is null
                        ? null
                        : s.Cycle with { Phase = CyclePhase.Paused, OvulationWindow = false, LibidoMod = 0.3 },
                    Postpartum = new PostpartumState(0, PostpartumPhase.Immediate, HormonalCrashActive: true),
                    // Postpartum HairLoss: estrogen propad → telogen effluvium
                    Aging = s.Aging is { } birthAging
                        ? birthAging with { HairDensity = Math.Max(0.1, birthAging.HairDensity - Config.HairLossPostpartumAmount) }
                        : s.Aging
                };
            }

            return s with
            {
                Pregnancy = pregnancy,
                Cycle = s.Cycle is null
                    ? null
                    : s.Cycle with { Phase = CyclePhase.Paused, OvulationWindow = false, LibidoMod = pregnancyLibidoMod }
            };
        }

        private PhysiologyState AdvancePostpartum(PhysiologyState s, WDateTime now, IHumanContext ctx, IEventCollector outbox)
        {
            var pp = s.Postpartum!;
            var days = pp.DaysSinceBirth + 1;
            var newPhase = days switch
            {
                <= 3 => PostpartumPhase.Immediate,
                <= 7 => PostpartumPhase.FirstWeek,
                <= 42 => PostpartumPhase.SixWeeks,
                _ => PostpartumPhase.FullRecovery
            };

            if (newPhase != pp.Phase)
                outbox.Add(new PostpartumPhaseChanged(now, ctx.Id, newPhase));

            // Postpartum LibidoMod: the prolactin-mediated suppression eases over ~6 months (0.3 → 1.0).
            // Breastfeeding prolongs the suppression via prolactin → multiply by 0.7.
            var postpartumLibidoMod = Math.Clamp(0.3 + (days / 180.0) * 0.7, 0.3, 1.0);
            if (pp.IsBreastfeeding) postpartumLibidoMod = Math.Max(0.3, postpartumLibidoMod * 0.7);
            if (s.Cycle?.Phase == CyclePhase.Paused)
                s = s with { Cycle = s.Cycle with { LibidoMod = postpartumLibidoMod } };

            if (newPhase == PostpartumPhase.FullRecovery)
            {
                return s with
                {
                    Postpartum = null,
                    Cycle = s.Cycle is null ? null : s.Cycle with { Phase = CyclePhase.Follicular, LibidoMod = 1.0 }
                };
            }

            // Deactivate the hormonal crash after 7 days (the progesterone/estrogen crash phase ends)
            var hormonalCrash = days <= 7;
            return s with { Postpartum = pp with { DaysSinceBirth = days, Phase = newPhase, HormonalCrashActive = hormonalCrash } };
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
                    : s.Cycle with { Phase = CyclePhase.Paused, OvulationWindow = false, LibidoMod = 0.5 } // 1. trimestr
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
        /// A tuple of (pain delta, bloat <b>target</b>, breast-tenderness <b>target</b>, libido multiplier).
        /// Pain is a per-day delta accumulated into the general Pain channel (which decays separately);
        /// bloat and tenderness are absolute target levels (0..100) that the state relaxes toward in
        /// <see cref="ApplyCycleSymptoms"/>, so they oscillate with the cycle instead of accumulating:
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
        /// <summary>
        /// Sinusoidal model of menstrual-cycle symptoms (replacing the phase-step constants).
        /// The calculation is a continuous function of the day in the cycle — Gaussian curves for menses and the luteal phase.
        /// Scientific basis: the estrogen/progesterone drift is smooth, not a binary switch
        /// (reference: physiology-psychology.md).
        /// </summary>
        private (double pain, double bloatTarget, double tenderTarget, double libidoMod, double estradiol, double progesterone) SymptomsFor(
            MenstrualCycleState c,
            ContraceptionLevel contraception = ContraceptionLevel.Unspecified)
        {
            if (c.Phase == CyclePhase.Paused)
                return (0.0, 0.0, 0.0, 1.0, 20.0, 10.0);

            var day = (double)c.DayInCycle;
            // Ovulation day for this cycle — dynamic per cycle (stored in CurrentCycleLength).
            var ovulDay = (double)Math.Max(_cycleCfg.MensesMeanDays + 2, c.CurrentCycleLength - _cycleCfg.LutealMeanDays);
            var mensesMid = _cycleCfg.MensesMeanDays / 2.0;

            // Pain: a Gaussian spike during menses + luteal escalation
            var mensesPain = 4.0 * Math.Exp(-Math.Pow(day - mensesMid, 2) / (2 * mensesMid));
            var lutealPain = 1.5 * Math.Max(0, (day - (ovulDay + 7)) / 7.0);
            var rawPain = (mensesPain + lutealPain) * _cycleCfg.PainBaseMultiplier;

            // Bloat TARGET (0..100): a Gaussian peak during menses + a late-luteal (PMS) ramp.
            // Returns the target level the state relaxes toward in ApplyCycleSymptoms — not an increment.
            var mensesBloat = _cycleCfg.MensesBloatPeak * Math.Exp(-Math.Pow(day - mensesMid, 2) / (2 * mensesMid));
            var lutealBloat = _cycleCfg.LutealBloatPeak * Math.Max(0, (day - (ovulDay + 7)) / 7.0);
            var bloatTarget = (mensesBloat + lutealBloat) * _cycleCfg.BloatBaseMultiplier;

            // Breast tenderness TARGET (0..100): dominant in the late luteal phase, mild during menses.
            var mensesTender = _cycleCfg.MensesBreastTenderPeak * Math.Exp(-Math.Pow(day - mensesMid, 2) / (2 * mensesMid));
            var lutealTender = _cycleCfg.LutealBreastTenderPeak * Math.Max(0, (day - (ovulDay + 5)) / 7.0);
            var tenderTarget = (mensesTender + lutealTender) * _cycleCfg.BreastTenderMultiplier;

            // Ovarian hormones (proxies, 0..100) → desire (estradiol+ / progesterone−).
            var (estradiol, progesterone) = CycleHormones(day, ovulDay, mensesMid);
            var libidoMod = CycleLibido(estradiol, progesterone, day, mensesMid);

            // PMDD amplifier — luteal symptoms are more severe for characters with PmsRisk > 0.3
            // Contraception (High/Moderate) reduces PMS/PMDD severity
            var contraFactor = contraception switch
            {
                ContraceptionLevel.High => 0.2,
                ContraceptionLevel.Moderate => 0.5,
                ContraceptionLevel.Low => 0.8,
                _ => 1.0
            };
            var lutealFactor = Math.Max(0, (day - (ovulDay + 7)) / 7.0);   // 0..1 v luteálu
            var pmddMultiplier = 1.0 + _cycleCfg.PmsRisk * lutealFactor * 1.5 * contraFactor;
            rawPain *= pmddMultiplier;
            bloatTarget *= pmddMultiplier;
            tenderTarget *= pmddMultiplier;

            return (rawPain, bloatTarget, tenderTarget, libidoMod, estradiol, progesterone);
        }

        /// <summary>
        /// Ovarian-hormone proxies [0..100] for a given cycle day. Estradiol: follicular ramp +
        /// periovulatory surge (~ovulDay) + smaller mid-luteal bump. Progesterone: ~0 before
        /// ovulation, sharp rise to a mid-luteal peak (~ovulDay+7), falls pre-menses.
        /// Source: human-behavior-npc B.2 (estradiol+/progesteron− → touha; replicated).
        /// </summary>
        private static (double estradiol, double progesterone) CycleHormones(double day, double ovulDay, double mensesMid)
        {
            var follicularRamp = 18.0 * Math.Clamp((day - mensesMid) / Math.Max(1.0, ovulDay - mensesMid), 0, 1);
            var ovulSurge = 80.0 * Math.Exp(-Math.Pow(day - ovulDay, 2) / (2 * 2.0 * 2.0));
            var lutealEstroBump = 35.0 * Math.Exp(-Math.Pow(day - (ovulDay + 7), 2) / (2 * 4.0 * 4.0));
            var estradiol = Math.Clamp(10.0 + follicularRamp + ovulSurge + lutealEstroBump, 5, 100);

            var progRaw = 90.0 * Math.Exp(-Math.Pow(day - (ovulDay + 7), 2) / (2 * 4.5 * 4.5));
            var progesterone = Math.Clamp(4.0 + (day >= ovulDay ? progRaw : progRaw * 0.05), 3, 100);
            return (estradiol, progesterone);
        }

        /// <summary>
        /// Cycle desire multiplier from ovarian hormones: <c>1 + 0.30·E − 0.20·P</c> (E,P normalised
        /// 0..1) plus a mild menses dip, clamped to [0.80, 1.25]. Direction and within-subject
        /// magnitude confirmed: estradiol+ on desire (Roney &amp; Simmons 2013, Hormones and Behavior
        /// 63(4):636-645, γ≈+0.16 at 2-day lag), progesterone− on desire (γ≈−0.13 to −0.20).
        /// Periovulatory peak (~+25% at this clamp) matches the modest, replicated fertile-window
        /// desire increase (Arslan et al. 2018/2021, JPSP, 26,000-entry preregistered diary study;
        /// d-equivalent effect sizes 0.12-0.43) — NOT the often-cited but overstated 25-60% figure.
        /// The 0.30/0.20 coefficients are a tuned heuristic translation of those within-subject betas
        /// onto GET's 0-100 proxy scale, not a direct unit-for-unit mapping.
        /// ✅ VERIFIED 2026-06 (see GET_MenstrualCycle_Hormone_Calibration_Implementation_Plan_v2.md).
        /// </summary>
        // DECISION (2026-06, peer-review verified): do NOT add a fertile-window shift in
        // preferred-partner traits (masculinity, facial/body symmetry — "ovulatory shift hypothesis").
        // Large preregistered studies (Jones et al. 2018; Jünger et al. 2018; Marcinkowska et al.
        // 2016/2018; Stern et al. 2020/2021) consistently find null effects; the supporting 2014
        // meta-analysis (Gildersleeve et al.) is contradicted by a same-year counter-analysis
        // (Wood et al.) and has not held up. Only the overall desire/initiation increase modeled
        // here is well-replicated.
        private static double CycleLibido(double estradiol, double progesterone, double day, double mensesMid)
        {
            var mensesDip = -0.10 * Math.Exp(-Math.Pow(day - mensesMid, 2) / 4.0);
            // Upper clamp tightened to 1.25 — Roney & Simmons (2013) and Arslan et al. (2018/2021)
            // support a modest ~10-25% periovulatory desire increase, not higher; 1.30 left unused
            // headroom beyond what's evidenced.
            return Math.Clamp(
                1.0 + 0.30 * (estradiol / 100.0) - 0.20 * (progesterone / 100.0) + mensesDip,
                0.80, 1.25);
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
            // Sample the length of this initialization cycle from the same distribution as at runtime.
            var cycleLength = Math.Max(cfg.MinCycleLengthDays, Math.Min(cfg.MaxCycleLengthDays,
                cfg.MeanCycleLengthDays + (int)Math.Round(Normal(rng, 0, cfg.VariabilityDaysStdDev))));
            var ovulDay = Math.Max(cfg.MensesMeanDays + 2, cycleLength - cfg.LutealMeanDays);
            var day = rng.Next(1, Math.Max(2, cycleLength));
            day = Math.Clamp(day, 1, cfg.MaxCycleLengthDays);
            var phase = PhaseFor(day, cycleLength, cfg.MensesMeanDays, ovulDay);

            // Back-estimate when menstruation began
            var lastMensesStart = now.AddDays(-(day - 1));

            // Seed hormones consistent with the starting day (so a fresh cycle isn't at defaults
            // until the first day rollover).
            var mensesMid = cfg.MensesMeanDays / 2.0;
            var (estradiol, progesterone) = CycleHormones(day, ovulDay, mensesMid);
            return new MenstrualCycleState(
                Phase: phase,
                DayInCycle: day,
                OvulationWindow: false,
                SymptomPain: 0, SymptomBreastTender: 0, SymptomBloat: 0,
                LibidoMod: CycleLibido(estradiol, progesterone, day, mensesMid),
                LastMensesStart: lastMensesStart,
                CurrentCycleLength: cycleLength,
                Estradiol: estradiol,
                Progesterone: progesterone);
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
        /// Moves <paramref name="current"/> toward <paramref name="target"/> by a fraction
        /// <paramref name="rate"/> (0..1) and clamps the result to [0, 100]. Used for cyclic
        /// symptoms that must track a phase target (rise <i>and</i> fall) rather than accumulate.
        /// </summary>
        private static double ApproachClamped(double current, double target, double rate)
            => Clamp01p(current + (target - current) * Math.Clamp(rate, 0.0, 1.0));

        /// <summary>
        /// Computes the new vitamin D level: subtracts the diurnal loss and — if the character
        /// is outdoors under sufficient irradiance — adds solar restoration.
        /// </summary>
        private double ComputeVitaminD(double current, double h, CelestialContext? celestial, SurfaceKind surface)
        {
            // Outdoors under sufficient irradiance: skin synthesis restores vitamin D.
            if (celestial is { } cel
                && cel.IrradianceFactor > Config.VitaminDSunThreshold
                && surface is SurfaceKind.Public or SurfaceKind.Social)
            {
                var restoration = Math.Min(
                    Config.VitaminDRestorationPerHourPerIrradiance * cel.IrradianceFactor * h,
                    Config.VitaminDMaxOutdoorRestorationPerHour * h);
                return Clamp01p(current + restoration);
            }

            // No sufficient sun exposure: passive decay only, deliberately set to a fraction of
            // IronDecayPerHour to reflect 25(OH)D's much longer serum half-life (~15 days).
            return Clamp01p(current - Config.VitaminDDecayPerHour * h);
        }

        /// <inheritdoc/>
        public void RestoreState(PhysiologyState state) => State = state;

        /// <summary>
        /// Replaces the current state with the provided snapshot.
        /// Used by the persistence layer to reload serialized state after a save/load cycle,
        /// and by tests to set up specific initial conditions.
        /// </summary>
        /// <param name="state">The state to restore.</param>
        public void RestoreState(PhysiologyState state, WDateOnly today = default)
        {
            var ageYears = ComputeAgeYears(today);

            if (state.Aging is not null)
                state = state with { Aging = state.Aging with { AgeYears = ageYears } };

            if (state.Cycle is not null && ageYears > 0 && ageYears < Config.MenstrualCycleBeginsInAge)
                state = state with { Cycle = null };

            State = state;
        }

        #region Object affordance application

        /// <summary>
        /// Applies the physiological effect of a single object affordance event.
        /// Only <see cref="AffordanceType.Hunger"/> and <see cref="AffordanceType.Thirst"/>
        /// map to direct physiology changes. All other types (MoodBoost, Warmth, Social…)
        /// belong to <c>DefaultPsychologyEngine</c>.
        /// </summary>
        /// <param name="s">Current physiology state.</param>
        /// <param name="oaa">Affordance event carrying type and satisfaction [0..1].</param>
        private PhysiologyState ApplyObjectAffordance(PhysiologyState s, Objects.ObjectAffordanceApplied oaa)
            => oaa.AffordanceType switch
            {
                // satisfaction=0.80 (roast) → Hunger -= 20; satisfaction=0.25 (apple) → Hunger -= 6.25
                AffordanceType.Hunger => s with
                {
                    Hunger = Clamp01p(s.Hunger - oaa.Satisfaction * Config.AffordanceHungerMaxDelta)
                },

                AffordanceType.Thirst => s with
                {
                    Thirst = Clamp01p(s.Thirst - oaa.Satisfaction * Config.AffordanceThirstMaxDelta)
                },

                // All other affordance types are not physiology concerns.
                _ => s
            };

        #endregion Object affordance application

        #region Cycle — HPA suppression

        /// <summary>
        /// Updates the HPA-axis suppression accumulator based on current stress and sleep debt.
        /// Called once per game-day during cycle progression.
        /// </summary>
        /// <remarks>
        /// Biological mechanism: elevated cortisol (from chronic stress or sleep deprivation)
        /// suppresses GnRH pulse frequency via the hypothalamic KNDy neuron network,
        /// reducing LH amplitude below the threshold needed to trigger ovulation.
        /// References: Fenster et al. (1999); Schliep et al. (2015, Human Reproduction).
        /// </remarks>
        /// <param name="s">Current physiology state — provides sleep debt.</param>
        /// <param name="ctx">Character context — provides previous-tick stress via snapshot.</param>
        /// <param name="now">Current in-world time for event timestamps.</param>
        /// <param name="outbox">Collector for suppression events.</param>
        /// <returns>Updated <see cref="MenstrualCycleState"/> with recalculated suppression fields.</returns>
        private MenstrualCycleState UpdateSuppressionAccumulator(
            PhysiologyState s,
            IHumanContext ctx,
            WDateTime now,
            IEventCollector outbox)
        {
            var c = s.Cycle!;

            // Read stress from the previous tick's snapshot (1-tick lag is intentional and acceptable
            // for a slow-acting HPA mechanism that operates over days, not minutes).
            var stress = ctx.Snapshot.Psychology.Stress;
            var sleepDebt = s.SleepDebtHours;

            // Suppression load: stress above threshold OR significant sleep debt both activate HPA suppression.
            // Sleep debt contribution: each hour above threshold = 0.5 stress-equivalent points.
            var sleepDebtContribution = Math.Max(0, sleepDebt - _cycleCfg.AnovulatorySleepDebtThresholdHours) * 0.5;
            var effectiveLoad = stress + sleepDebtContribution;
            var isSuppressive = effectiveLoad >= _cycleCfg.AnovulatoryStressThreshold;

            var wasAnovulatory = c.AnovulatoryCycleActive;
            double newAccDays;

            if (isSuppressive)
            {
                // Accumulate: count up toward onset threshold.
                newAccDays = c.StressSuppressionAccDays + 1.0;
            }
            else
            {
                // Recovery: decay faster (asymmetric — matches allostatic load recovery pattern).
                newAccDays = Math.Max(0, c.StressSuppressionAccDays - 1.5);
            }

            // Determine new suppression flag.
            var isNowAnovulatory = newAccDays >= _cycleCfg.AnovulatoryOnsetDays
                                   || (wasAnovulatory && newAccDays > 0);
            // ^ Keep suppression active until accumulator fully clears (hysteresis).

            // Emit transition events.
            if (!wasAnovulatory && isNowAnovulatory)
                outbox.Add(new CycleSuppressionStarted(now, ctx.Id, stress));

            if (wasAnovulatory && !isNowAnovulatory)
                outbox.Add(new CycleSuppressionLifted(now, ctx.Id));

            return c with
            {
                StressSuppressionAccDays = newAccDays,
                AnovulatoryCycleActive = isNowAnovulatory
            };
        }

        #endregion Cycle — HPA suppression
    }
}
