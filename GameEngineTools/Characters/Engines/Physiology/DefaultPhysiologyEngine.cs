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
        private readonly WDateOnly _birthDate;

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
            IWorldObjectProvider? objectProvider = null)
        {
            Config = cfg.Value;
            _cycleCfg = cycleCfg.Value;
            _birthDate = birthDate;

            _log = loggerFactory.CreateLogger<DefaultPhysiologyEngine>();
            _rng = rng;

            var initialCycle = (Config.EnableMenstrualCycle && biology == SexBiology.Female && (now.Year - birthDate.Year) >= Config.MenstrualCycleBeginsInAge && now != default)
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
                Aging: new PhysicalAgingState());

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
            var nutProfile = ResolveNutritionalProfile(ctx);

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
                Drink => -(nutProfile?.HydrationGain ?? 50) * h,
                Sleep => 2.0 * h,
                _ => 8 * h
            };

            var painDelta = action switch
            {
                SelfCare => -10 * h,
                Sleep => -(Config.PainPassiveRecoveryPerHour + Config.PainSleepRecoveryPerHour) * h,
                _ => -Config.PainPassiveRecoveryPerHour * h
            };

            // Post-menopauza: estrogen ztracen → imunitní recovery pomalejší (30 % zpomalení)
            var isPostMenopauseImmune = s.Cycle?.Phase == CyclePhase.Paused
                && s.Pregnancy is null && (s.Aging?.AgeYears ?? 0) >= 45;
            var immuneDecayFactor = isPostMenopauseImmune ? 0.7 : 1.0;
            var immuneDelta = action switch
            {
                SelfCare => -0.5 * h * immuneDecayFactor,
                _ => -0.3 * h * immuneDecayFactor
            };

            var feverDelta = s.ImmuneLoad > 30 ? (s.ImmuneLoad - 30) / 70.0 * 2.0 : 0.0;
            // Cirkadiánní tělesná teplota: sinusoidální vlna ±CircadianTempAmplitude (Waterhouse 2005)
            var hoursOfDayT = (double)(now.Hour % WWorld.Spec.HoursPerDay);
            // Peak ukotvíme na SolarNoon+5h pokud máme astronomický kontext (Waterhouse: tělesná teplota ~5h po poledni)
            var circTempPeakHour = ctx.Snapshot.Celestial is { } celT && !double.IsNaN(celT.SolarNoonHour)
                ? celT.SolarNoonHour + 5.0
                : Config.CircadianTempPeakHour;
            // cos: maximum na circTempPeakHour, minimum na ±HalfDay
            var circadianTempComponent = Config.CircadianTempAmplitude
                * Math.Cos((hoursOfDayT - circTempPeakHour) * 2 * Math.PI / WWorld.Spec.HoursPerDay);
            var targetBodyTemp = feverDelta + circadianTempComponent;

            s = s with
            {
                Energy = Clamp01p(s.Energy + energyDelta),
                Hunger = Clamp01p(s.Hunger + hungerDelta),
                Thirst = Clamp01p(s.Thirst + thirstDelta),
                Pain = Clamp01p(s.Pain + painDelta),
                ImmuneLoad = Clamp01p(s.ImmuneLoad + immuneDelta),
                BodyTempDelta = Math.Clamp(Approach(s.BodyTempDelta, targetBodyTemp, 0.1 * h), -1.0, 3.5)
            };

            // Nutriční drift — Calories/Protein klesají, jsou doplňovány jídlem;
            // Iron se obnovuje spánkem; VitaminD pomalu klesá
            if (s.Nutrition is { } nut)
            {
                var caloriesDelta = action == Eat ? (nutProfile?.CalorieGain ?? Config.CaloriesEatingGainPerHour) * h : -Config.NutritionDecayPerHour * h;
                var proteinDelta = action == Eat ? (nutProfile?.ProteinGain ?? Config.ProteinEatingGainPerHour) * h : -Config.NutritionDecayPerHour * h;
                var ironDelta = action == Sleep ? Config.IronSleepRecoveryPerHour * h : -Config.NutritionDecayPerHour * h * 0.3;
                // Glykemický stav: spike při jídle, rebound dip 1–2h po jídle
                var glucoseDelta = action == Eat ? Config.BloodGlucoseEatingGain * h : 0.0;
                var postMealHours = action == Eat ? 0.0 : nut.PostMealHours + h;
                var inDipWindow = postMealHours > Config.BloodGlucoseDipStartHours
                                 && postMealHours < Config.BloodGlucoseDipEndHours;
                var glucoseDecay = Config.BloodGlucoseBaseDecayPerHour + (inDipWindow ? Config.BloodGlucoseDipDecayBonus : 0);
                if (action != Eat) glucoseDelta -= glucoseDecay * h;

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

            // Allostatická zátěž — kumuluje se při chronickém neglektu potřeb
            {
                var alloAccum = 0.0;
                if (s.Hunger > Config.AllostaticLoadThresholdHunger) alloAccum += Config.AllostaticLoadAccumRatePerHour * h;
                if (s.Thirst > Config.AllostaticLoadThresholdThirst) alloAccum += Config.AllostaticLoadAccumRatePerHour * h;
                if (s.SleepDebtHours > Config.AllostaticLoadThresholdSleepDebt) alloAccum += Config.AllostaticLoadAccumRatePerHour * h;
                if (s.Pain > Config.AllostaticLoadThresholdPain) alloAccum += Config.AllostaticLoadAccumRatePerHour * h;
                if (s.ImmuneLoad > Config.AllostaticLoadThresholdImmune) alloAccum += Config.AllostaticLoadAccumRatePerHour * h;
                var alloDecay = action is Sleep or SelfCare ? Config.AllostaticLoadDecayRatePerHour * h : 0.0;
                s = s with { AllostaticLoad = Math.Clamp(s.AllostaticLoad + alloAccum - alloDecay, 0, 100) };
            }

            // Sleep Inertia — lineární decay po probuzení
            if (s.SleepInertiaHours > 0)
                s = s with { SleepInertiaHours = Math.Max(0, s.SleepInertiaHours - h) };

            // SAM systém — velmi rychlý decay (Sympatho-Adrenomedullary, adrenalin/noradrenalin)
            if (s.AcuteArousalLevel > 0)
                s = s with { AcuteArousalLevel = Math.Max(0, s.AcuteArousalLevel - Config.AcuteArousalDecayPerHour * h) };

            // Fyzická únava — akumulace při Work, decay při odpočinku/spánku
            // Sarkopenie: méně svalové hmoty = Work fatigue se akumuluje rychleji
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

            // Chronická bolest — kumulativní counter (Dantzer 2008)
            if (s.Pain > Config.ChronicPainAccumThreshold)
                s = s with { ChronicPainDays = s.ChronicPainDays + h / 24.0 };
            else
                s = s with { ChronicPainDays = Math.Max(0, s.ChronicPainDays - h / (24.0 * Config.ChronicPainDecayFactor * 2)) };

            // Kortizol — diurnální křivka (HPA osa) + chronický stres + imunitní elevace
            {
                var hoursOfDay = (double)(now.Hour % WWorld.Spec.HoursPerDay);
                // CAR (Cortisol Awakening Response) ukotvíme na SunriseHour+2h (světelný signál nastavuje peak)
                var cortisolPeakHour = ctx.Snapshot.Celestial is { } celC && !double.IsNaN(celC.SunriseHour)
                    ? celC.SunriseHour + 2.0
                    : Config.CortisolDiurnalPeakHour;
                var diurnal = Config.CortisolDiurnalAmplitude
                              * Math.Exp(-Math.Pow(hoursOfDay - cortisolPeakHour, 2) / 8.0);
                // Hypocortisolismus paradox (Fries 2005): při extrémním AlloLoad HPA downreguluje
                var alloComponent = s.AllostaticLoad < Config.HypocortisolismAlloThreshold
                    ? s.AllostaticLoad * Config.CortisolAlloWeight
                    : Math.Max(0, Config.HypocortisolismAlloThreshold * Config.CortisolAlloWeight
                               - (s.AllostaticLoad - Config.HypocortisolismAlloThreshold) * Config.HypocortisolismDeclineRate);
                var immuneComponent = Math.Max(0, s.ImmuneLoad - 40) * Config.CortisolImmuneWeight;
                var targetCortisol = Math.Clamp(50 + diurnal + alloComponent + immuneComponent, 0, 100);
                // Rychleji nahoru (CAR), pomaleji dolů — biologicky věrné
                var cortRate = targetCortisol > s.CortisolLevel ? 20.0 * h : 8.0 * h;
                s = s with { CortisolLevel = Math.Clamp(Approach(s.CortisolLevel, targetCortisol, cortRate), 0, 100) };
            }

            // Cirkadiánní fázový posun — social jet-lag model
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
                // Pomalé zotavení k chronotypu (tělo se resynchronizuje ~1 h/den)
                s = s with
                {
                    CircadianPhaseShiftHours = Approach(
                    s.CircadianPhaseShiftHours, Config.ChronotypeOffsetHours, Config.CircadianPhaseRecoveryPerHour * h)
                };
            }

            // Recovery Debt — fyzický deficit regenerace nad rámec spánkového dluhu
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

            // Věkové efekty
            {
                var ageYears = now.Date.Year - _birthDate.Year;

                // Menopauza: ženy ≥ MenopauseAge → cyklus trvale Paused
                if (s.Cycle is { Phase: not CyclePhase.Paused } && ageYears >= Config.MenopauseAge)
                    s = s with { Cycle = s.Cycle with { Phase = CyclePhase.Paused, OvulationWindow = false, LibidoMod = 1.0 } };

                // Stárnutí testosteronu u mužů (~1 %/rok po 25)
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

            // Fyzické stárnutí — vlasy, vrásky, svalová hmota, kostní hustota
            if (s.Aging is { } aging)
            {
                var ageYears = (double)(now.Date.Year - _birthDate.Year);
                var ageYearsInt = Math.Max(0, now.Date.Year - _birthDate.Year);

                // Růst vlasů (~1,25 cm/měsíc reálně, ~0,00175 cm/hod v herním světě)
                var newHairLen = Math.Min(120.0, aging.HairLengthCm + Config.HairGrowthCmPerHour * h);

                // Šedivění: věk + kortizol akcelerátor
                var greying = 0.0;
                if (ageYears > Config.HairGreyingAgeStart)
                {
                    greying += (ageYears - Config.HairGreyingAgeStart) * Config.HairGreyingRatePerYear * h / (365.25 * 24);
                    greying += s.CortisolLevel * Config.HairGreyingCortisolBoost * h;
                }

                // Hustota vlasů: androgenní alopécie (muži) + stres
                var densityChange = 0.0;
                if (s.Testosterone is { } _ && ageYears > Config.HairLossAgeStartMale)
                    densityChange -= (ageYears - Config.HairLossAgeStartMale) * Config.HairLossRatePerYearMale * h / (365.25 * 24);
                if (s.AllostaticLoad > Config.HairLossStressThreshold)
                    densityChange -= Config.HairLossStressRate * h;
                else
                    densityChange += Config.HairDensityRecoveryPerHour * h;

                // Vrásky: věk + kortizol
                var wrinkles = 0.0;
                if (ageYears > Config.WrinklingAgeStart)
                {
                    wrinkles += (ageYears - Config.WrinklingAgeStart) * Config.WrinklingRatePerYear * h / (365.25 * 24);
                    wrinkles += s.CortisolLevel * Config.WrinklingCortisolBoost * h;
                }

                // Sarkopenie: pokles svalové hmoty po 30. roce
                var muscleChange = ageYears > Config.SarcopeniaAgeStart
                    ? -(ageYears - Config.SarcopeniaAgeStart) * Config.SarcopeniaRatePerYear * h / (365.25 * 24)
                    : 0.0;

                // Kostní hustota: stárnutí + post-menopauza
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

            // Chronická sociální izolace → kortizol (Cacioppo 2015)
            {
                var needSocial = ctx.Snapshot.Psychology?.Motivations?.NeedSocial ?? 50;
                if (needSocial > Config.SocialIsolationCortisolThreshold)
                {
                    var isolSeverity = Math.Min((needSocial - Config.SocialIsolationCortisolThreshold) / 20.0, 1.0);
                    s = s with { CortisolLevel = Math.Clamp(s.CortisolLevel + isolSeverity * Config.SocialIsolationCortisolRatePerHour * h, 0, 100) };
                }
            }

            // Testosteron — diurnální rytmus + HPA-HPG cross-talk + spánkový dluh (jen muži)
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
                    // P(death in dt hours) = 1 − (1 − risk_per_hour)^dt
                    var tickRisk = 1.0 - Math.Pow(1.0 - risk, h);
                    if (ctx.Random.Chance(tickRisk))
                    {
                        var cause = NaturalMortalityCalculator.ResolveCause(s, ageYears);
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

        private static bool HasCriticalState(PhysiologyState s) =>
            s.Energy < 2 || s.Hunger > 98 || s.AllostaticLoad > 95;

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
                        var rawQuality = se.Quality / 100.0;

                        // Věkový faktor na kvalitu spánku: méně deep sleep po 50 → horší efektivní kvalita
                        var sleepAgeYears = se.OccurredAt.Date.Year - _birthDate.Year;
                        var ageQualityFactor = sleepAgeYears > Config.AgeSleepQualityThreshold
                            ? Math.Max(0.5, 1.0 - (sleepAgeYears - Config.AgeSleepQualityThreshold) * Config.AgeSleepQualityPenaltyPerYear)
                            : 1.0;
                        var qualityFactor = rawQuality * ageQualityFactor;

                        var remainingDept = s.SleepDebtHours;
                        var maxRecovery = remainingDept * 0.55; // Max 55 % za jednu noc
                        var actualRecovery = Math.Min(maxRecovery, h * 0.9 * qualityFactor);

                        // Recovery Debt zpomaluje obnovu energie (min. 30 % účinnosti)
                        var recoveryFactor = Math.Max(0.3, 1.0 - s.RecoveryDebtHours / 48.0);
                        // Věkový faktor: energie se obnovuje pomaleji po 40. roce
                        var ageFactor = sleepAgeYears > Config.AgingEnergyRecoveryPenaltyStart
                            ? Math.Max(0.3, 1.0 - (sleepAgeYears - Config.AgingEnergyRecoveryPenaltyStart) * Config.AgingEnergyRecoveryPenaltyPerYear)
                            : 1.0;
                        // Sleep Inertia: horší kvalita = delší inertia (quality=100 → 0.75h; quality=0 → 1.5h)
                        var inertiaHours = Config.SleepInertiaMaxHours * (1.0 - se.Quality / 100.0 * 0.5);

                        s = s with
                        {
                            // Spánkový dluh: maximální splacení závisí na kvalitě
                            SleepDebtHours = Math.Max(0, remainingDept - actualRecovery),

                            // Imunitní systém: regenerace hlubokého spánku
                            ImmuneLoad = Clamp01p(s.ImmuneLoad - 3.0 * qualityFactor),

                            Pain = rawQuality >= 0.40
                                ? Clamp01p(s.Pain - 5.0 * qualityFactor)
                                : s.Pain,

                            // Energie se obnoví spánkem; při recovery debt a věku je obnova snížena
                            Energy = Clamp01p(s.Energy + h * Config.EnergyRecoveryPerSleepHour * qualityFactor * recoveryFactor * ageFactor),

                            // Sleep inertia — kognitivní setrvačnost po probuzení
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
                        // Bone fragility: nízká hustota kostí → vyšší efektivní závažnost zranění
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

                // Sociální bolest = fyzická bolest (Eisenberger et al., 2003) — odmítnutí aktivuje HPA osu
                case InteractionOutcome io when io.From == ctx.Id && !io.Accepted:
                    {
                        var n = ctx.Personality.BigFive.Neuroticism;
                        var spike = Config.SocialPainCortisolSpike * (1.0 + n * 0.5);
                        s = s with { CortisolLevel = Math.Clamp(s.CortisolLevel + spike, 0, 100) };
                        break;
                    }

                // Sociální podpora jako kortizol buffer (Eisenberger 2007):
                // přijatá interakce od blízkého člověka tlumí HPA aktivitu
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
            var (day, phase, length, ovulDay) = CalculateCycleProgression(s.Cycle!);
            s = EmitCycleProgressionEvents(s, now, ctx, box, day, phase, length, ovulDay);
            s = ApplyCycleSymptoms(s);

            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPhysiologyEngine)))
            {
                _log.PhysiologyCycle(ctx.Id.Value.ToString(), s.Cycle!.Phase.ToString(), s.Cycle.DayInCycle);
            }

            return s;
        }

        private (int day, CyclePhase phase, int length, int ovulDay) CalculateCycleProgression(MenstrualCycleState c)
        {
            var length = Math.Max(_cycleCfg.MinCycleLengthDays, Math.Min(_cycleCfg.MaxCycleLengthDays,
                _cycleCfg.MeanCycleLengthDays + (int)Math.Round(Normal(_rng, 0, _cycleCfg.VariabilityDaysStdDev))));
            // Ovulace = délka cyklu − střední luteální fáze (Bull 2019: luteální ~11,7 dní, SD 2,8).
            // Folikulární fáze je hlavní zdroj variability — ovulDay se mění každý cyklus.
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

            // Antikoncepce High/Moderate potlačuje ovulaci
            var contraceptionSuppressesOvul = s.CurrentContraception is ContraceptionLevel.High or ContraceptionLevel.Moderate;
            var ovulWindow = phase == CyclePhase.Ovulation && !contraceptionSuppressesOvul;
            if (_cycleCfg.EnableOvulationWindowEvents && ovulWindow && !c.OvulationWindow)
                box.Add(new OvulationWindowOpened(now, ctx.Id));

            // Při resetu na den 1 uložit skutečnou délku tohoto cyklu (ovulDay se pak počítá z ní).
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
            var (pain, bloat, tender, libido) = SymptomsFor(s.Cycle!, s.CurrentContraception);
            var day = (double)s.Cycle!.DayInCycle;
            var ovulDay = (double)Math.Max(_cycleCfg.MensesMeanDays + 2, s.Cycle.CurrentCycleLength - _cycleCfg.LutealMeanDays);
            var lutealFactor = Math.Max(0, (day - (ovulDay + 7)) / 7.0);
            var isPmddActive = _cycleCfg.PmsRisk > 0.3 && lutealFactor > 0.5;
            return s with
            {
                Pain = Clamp01p(s.Pain + pain),
                Cycle = s.Cycle with
                {
                    SymptomBloat = Clamp01p(s.Cycle.SymptomBloat + bloat),
                    SymptomBreastTender = Clamp01p(s.Cycle.SymptomBreastTender + tender),
                    LibidoMod = libido,
                    PmddActive = isPmddActive
                }
            };
        }

        private PhysiologyState AdvancePregnancy(PhysiologyState s, WDateTime now, IHumanContext ctx, IEventCollector outbox, PregnancyState pregnancy)
        {
            var daysPregnant = pregnancy.ConceivedOn.DaysUntil(now.Date);
            // LibidoMod per trimestr (Basson 2006 review): 1. tri ↓ (nevolnost/únava), 2. tri ↑, 3. tri ↓↓
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

            // Postpartum LibidoMod: prolaktin-mediovaná suprese klesá za ~6 měsíců (0.3 → 1.0).
            // Kojení prodlužuje supresi přes prolaktin → násobení 0.7.
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

            // Hormonal crash deaktivovat po 7 dnech (progesteron/estrogen crash fáze končí)
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
        /// <summary>
        /// Sinusoidální model symptomů menstruačního cyklu (náhrada phase-step konstanty).
        /// Výpočet je kontinuální funkcí dne v cyklu — Gaussovy křivky pro menzes a luteální fázi.
        /// Vědecký základ: estrogen/progesteron drift je plynulý, nikoli binární přepínač
        /// (reference: physiology-psychology.md).
        /// </summary>
        private (double pain, double bloat, double tender, double libidoMod) SymptomsFor(
            MenstrualCycleState c,
            ContraceptionLevel contraception = ContraceptionLevel.Unspecified)
        {
            if (c.Phase == CyclePhase.Paused)
                return (0.0, 0.0, 0.0, 1.0);

            var day = (double)c.DayInCycle;
            // Ovulační den pro tento cyklus — dynamický per-cyklus (uložen v CurrentCycleLength).
            var ovulDay = (double)Math.Max(_cycleCfg.MensesMeanDays + 2, c.CurrentCycleLength - _cycleCfg.LutealMeanDays);
            var mensesMid = _cycleCfg.MensesMeanDays / 2.0;

            // Bolest: Gaussový spike v menstruaci + luteální eskalace
            var mensesPain = 4.0 * Math.Exp(-Math.Pow(day - mensesMid, 2) / (2 * mensesMid));
            var lutealPain = 1.5 * Math.Max(0, (day - (ovulDay + 7)) / 7.0);
            var rawPain = (mensesPain + lutealPain) * _cycleCfg.PainBaseMultiplier;

            // Bloat: peak v menstruaci, mírně v luteálu
            var mensesBloat = 2.5 * Math.Exp(-Math.Pow(day - mensesMid, 2) / (2 * mensesMid));
            var lutealBloat = 0.8 * Math.Max(0, (day - (ovulDay + 7)) / 7.0);
            var rawBloat = (mensesBloat + lutealBloat) * _cycleCfg.BloatBaseMultiplier;

            // Breast tenderness: dominantní v pozdní luteální fázi, mírně v menstruaci
            var mensesTender = 2.0 * Math.Exp(-Math.Pow(day - mensesMid, 2) / (2 * mensesMid));
            var lutealTender = 1.5 * Math.Max(0, (day - (ovulDay + 5)) / 7.0);
            var rawTender = (mensesTender + lutealTender) * _cycleCfg.BreastTenderMultiplier;

            // LibidoMod: Gaussový vrchol v ovulaci, mírný propad v menstruaci, baseline 0.95
            var libidoBoost = 0.25 * Math.Exp(-Math.Pow(day - ovulDay, 2) / 8.0);
            var mensesDip = -0.10 * Math.Exp(-Math.Pow(day - mensesMid, 2) / 4.0);
            var libidoMod = Math.Clamp(0.95 + libidoBoost + mensesDip, 0.80, 1.20);

            // PMDD amplifikátor — luteální symptomy závažnější u postav s PmsRisk > 0.3
            // Antikoncepce (High/Moderate) snižuje závažnost PMS/PMDD
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
            rawBloat *= pmddMultiplier;
            rawTender *= pmddMultiplier;

            return (rawPain, rawBloat, rawTender, libidoMod);
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
            // Vzorkuj délku tohoto inicializačního cyklu ze stejného rozdělení jako za běhu.
            var cycleLength = Math.Max(cfg.MinCycleLengthDays, Math.Min(cfg.MaxCycleLengthDays,
                cfg.MeanCycleLengthDays + (int)Math.Round(Normal(rng, 0, cfg.VariabilityDaysStdDev))));
            var ovulDay = Math.Max(cfg.MensesMeanDays + 2, cycleLength - cfg.LutealMeanDays);
            var day = rng.Next(1, Math.Max(2, cycleLength));
            day = Math.Clamp(day, 1, cfg.MaxCycleLengthDays);
            var phase = PhaseFor(day, cycleLength, cfg.MensesMeanDays, ovulDay);

            // Zpětný odhad, kdy začala menstruace
            var lastMensesStart = now.AddDays(-(day - 1));
            return new MenstrualCycleState(
                Phase: phase,
                DayInCycle: day,
                OvulationWindow: false,
                SymptomPain: 0, SymptomBreastTender: 0, SymptomBloat: 0,
                LibidoMod: 1.0,
                LastMensesStart: lastMensesStart,
                CurrentCycleLength: cycleLength);
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
        /// Vypočítá novou hladinu vitaminu D: odečte diurnální ztrátu a — pokud je postava
        /// venku za dostatečného ozáření — přidá sluneční restauraci.
        /// </summary>
        private double ComputeVitaminD(double current, double h, CelestialContext? celestial, SurfaceKind surface)
        {
            var net = -Config.NutritionDecayPerHour * h * 0.5;

            if (celestial is { } cel && cel.IrradianceFactor > Config.VitaminDSunThreshold)
            {
                if (surface is SurfaceKind.Public or SurfaceKind.Social)
                {
                    var restoration = Math.Min(
                        Config.VitaminDRestorationPerHourPerIrradiance * cel.IrradianceFactor * h,
                        Config.VitaminDMaxOutdoorRestorationPerHour * h);
                    net += restoration;
                }
            }

            return Clamp01p(current + net);
        }

        /// <summary>
        /// Replaces the current state with the provided snapshot.
        /// Used by the persistence layer to reload serialized state after a save/load cycle,
        /// and by tests to set up specific initial conditions.
        /// </summary>
        /// <param name="state">The state to restore.</param>
        public void RestoreState(PhysiologyState state) => State = state;
    }
}
