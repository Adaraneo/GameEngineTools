// IPhysiology.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Tuning constants for <see cref="IPhysiologyEngine"/>. Every rate, threshold and
    /// multiplier the physiology pipeline uses lives here so scenarios can be retuned via
    /// configuration without recompiling. Bound from <c>Characters:Physiology</c>.
    /// </summary>
    /// <param name="RestingMetabolicRate">Resting metabolic rate (kcal/day) used as the metabolic baseline.</param>
    /// <param name="MaxSleepDebtHours">Upper bound on accumulated sleep-debt hours.</param>
    /// <param name="EnableMenstrualCycle">When <c>true</c>, female characters run the menstrual-cycle module.</param>
    /// <param name="MenstrualCycleBeginsInAge">Age (years) at which menarche begins.</param>
    /// <param name="EnergyRecoveryPerSleepHour">Energy points restored per hour of sleep.</param>
    /// <param name="PainPassiveRecoveryPerHour">Pain points decayed per hour while awake and idle.</param>
    /// <param name="PainSleepRecoveryPerHour">Pain points decayed per hour of sleep.</param>
    /// <param name="BaseConceptionChancePerEncounter">Base conception probability per reproductively-relevant encounter.</param>
    /// <param name="OvulationConceptionMultiplier">Multiplier applied to conception chance during the ovulation window.</param>
    /// <param name="PregnancyDiscoveryMinDays">Minimum days after conception before pregnancy can be discovered.</param>
    /// <param name="PregnancyTermDays">Gestation length in days from conception to birth.</param>
    /// <param name="EnableNutrition">When <c>true</c>, the nutrition sub-state (calories, vitamins, glucose) is tracked.</param>
    /// <param name="NutritionDecayPerHour">Per-hour decay of nutrition stores while awake.</param>
    /// <param name="CaloriesEatingGainPerHour">Calories restored per hour while eating.</param>
    /// <param name="ProteinEatingGainPerHour">Protein restored per hour while eating.</param>
    /// <param name="IronSleepRecoveryPerHour">Iron restored per hour of sleep (critical for post-menses recovery).</param>
    /// <param name="InjuryRestRecoveryPerDay">Injury severity healed per day while resting.</param>
    /// <param name="InjuryActiveRecoveryPerDay">Injury severity healed per day while active.</param>
    /// <param name="InjuryInfectionImmuneLoadPerDay">Immune load added per day by an infected injury.</param>
    /// <param name="AllostaticLoadThresholdHunger">Hunger level above which allostatic load accumulates.</param>
    /// <param name="AllostaticLoadThresholdThirst">Thirst level above which allostatic load accumulates.</param>
    /// <param name="AllostaticLoadThresholdSleepDebt">Sleep-debt hours above which allostatic load accumulates.</param>
    /// <param name="AllostaticLoadThresholdPain">Pain level above which allostatic load accumulates.</param>
    /// <param name="AllostaticLoadThresholdImmune">Immune load above which allostatic load accumulates.</param>
    /// <param name="AllostaticLoadAccumRatePerHour">Allostatic load gained per hour while a threshold is breached.</param>
    /// <param name="AllostaticLoadDecayRatePerHour">Allostatic load shed per hour during active recovery (sleep/self-care).</param>
    /// <param name="CortisolDiurnalPeakHour">Hour of day at which the cortisol diurnal curve peaks.</param>
    /// <param name="CortisolDiurnalAmplitude">Amplitude of the cortisol diurnal oscillation.</param>
    /// <param name="CortisolAlloWeight">Weight of allostatic load on the cortisol baseline.</param>
    /// <param name="CortisolImmuneWeight">Weight of immune activation on the cortisol baseline.</param>
    /// <param name="ChronotypeOffsetHours">Stable circadian phase offset (positive = morning type, negative = night owl).</param>
    /// <param name="NaturalSleepStartHour">Hour of day the character naturally falls asleep.</param>
    /// <param name="CircadianPhaseRecoveryPerHour">Rate at which jet-lag phase disruption realigns per hour.</param>
    /// <param name="RecoveryDebtAccumAlloThreshold">Allostatic load above which recovery debt accumulates.</param>
    /// <param name="RecoveryDebtAccumRatePerHour">Recovery-debt hours gained per hour above the allostatic threshold.</param>
    /// <param name="RecoveryDebtDecayPerSleepHour">Recovery-debt hours shed per hour of sleep.</param>
    /// <param name="RecoveryDebtDecayPerSelfCareHour">Recovery-debt hours shed per hour of self-care.</param>
    /// <param name="EnableTestosteroneCycle">When <c>true</c>, male characters run the testosterone cycle.</param>
    /// <param name="TestosteronePeakHour">Hour of day the testosterone diurnal rhythm peaks.</param>
    /// <param name="TestosteroneAlloSuppression">Fraction by which allostatic load suppresses testosterone.</param>
    /// <param name="TestosteroneSleepDebtPenaltyPerHour">Testosterone reduction per hour of sleep debt.</param>
    /// <param name="SleepInertiaMaxHours">Maximum sleep-inertia duration after waking.</param>
    /// <param name="SocialPainCortisolSpike">Cortisol spike emitted on social rejection (HPA activation).</param>
    /// <param name="AcuteArousalDecayPerHour">Per-hour decay of acute SAM arousal (fast adrenaline response).</param>
    /// <param name="InjuryAcuteArousalSpike">Acute arousal spike on injury.</param>
    /// <param name="NightmareAcuteArousalSpike">Acute arousal spike on a nightmare.</param>
    /// <param name="StressSpikedAcuteArousalWeight">Weight converting a stress spike into acute arousal.</param>
    /// <param name="PhysicalFatigueAccumPerWorkHour">Physical (muscular) fatigue gained per hour of work.</param>
    /// <param name="PhysicalFatigueDecayPerSleepHour">Physical fatigue shed per hour of sleep.</param>
    /// <param name="PhysicalFatigueDecayPerIdleHour">Physical fatigue shed per hour while idle.</param>
    /// <param name="PhysicalFatigueSelfCareDecayBonus">Extra physical-fatigue decay per hour of self-care.</param>
    /// <param name="BloodGlucoseEatingGain">Blood-glucose gain from eating.</param>
    /// <param name="BloodGlucoseBaseDecayPerHour">Baseline blood-glucose decay per hour.</param>
    /// <param name="BloodGlucoseDipDecayBonus">Extra glucose decay during the post-meal rebound dip.</param>
    /// <param name="BloodGlucoseDipStartHours">Hours after a meal at which the glucose dip begins.</param>
    /// <param name="BloodGlucoseDipEndHours">Hours after a meal at which the glucose dip ends.</param>
    /// <param name="HypocortisolismAlloThreshold">Allostatic load above which the HPA axis downregulates (hypocortisolism paradox).</param>
    /// <param name="HypocortisolismDeclineRate">Rate of cortisol decline once hypocortisolism sets in.</param>
    /// <param name="SocialSupportCortisolBuffer">Cortisol reduction from sufficient social support (Eisenberger 2007).</param>
    /// <param name="SocialSupportClosenessThreshold">Closeness level required to trigger the social-support buffer.</param>
    /// <param name="SocialIsolationCortisolThreshold">Isolation duration/level above which cortisol rises (Cacioppo 2015).</param>
    /// <param name="SocialIsolationCortisolRatePerHour">Cortisol gained per hour of chronic social isolation.</param>
    /// <param name="ChronicPainAccumThreshold">Pain level above which chronic-pain days accumulate (Dantzer 2008).</param>
    /// <param name="ChronicPainDecayFactor">Decay factor applied to accumulated chronic-pain days.</param>
    /// <param name="CircadianTempAmplitude">Amplitude of the circadian body-temperature oscillation (Waterhouse 2005).</param>
    /// <param name="CircadianTempPeakHour">Hour of day at which body temperature peaks.</param>
    /// <param name="MenopauseAge">Age at which menopause occurs.</param>
    /// <param name="AgingEnergyRecoveryPenaltyStart">Age at which energy-recovery aging penalty begins.</param>
    /// <param name="AgingEnergyRecoveryPenaltyPerYear">Energy-recovery penalty accrued per year past the start age.</param>
    /// <param name="AgingImmuneBaselineStart">Age at which the immune baseline begins to drift upward.</param>
    /// <param name="AgingImmuneBaselinePerYear">Immune baseline increase per year past the start age.</param>
    /// <param name="AgingTestosteronePenaltyStart">Age at which testosterone aging penalty begins.</param>
    /// <param name="AgingTestosteronePenaltyPerYear">Testosterone penalty accrued per year past the start age.</param>
    /// <param name="AltitudeHypoxiaThreshold">Altitude (m) above which hypoxia effects begin.</param>
    /// <param name="AltitudeAMSThreshold">Altitude (m) above which acute mountain sickness occurs.</param>
    /// <param name="AltitudeEnergyDecayBonusPerKm">Extra energy decay per km of altitude above the hypoxia threshold.</param>
    /// <param name="AltitudeAMSPainPerHour">Pain gained per hour while above the AMS threshold.</param>
    /// <param name="HairGrowthCmPerHour">Hair growth in cm per hour.</param>
    /// <param name="HairGreyingAgeStart">Age at which hair greying begins.</param>
    /// <param name="HairGreyingRatePerYear">Grey-fraction increase per year.</param>
    /// <param name="HairGreyingCortisolBoost">Extra greying per unit of chronic cortisol.</param>
    /// <param name="HairLossAgeStartMale">Age at which male androgenic hair loss begins.</param>
    /// <param name="HairLossRatePerYearMale">Hair-density loss per year for males.</param>
    /// <param name="HairLossStressThreshold">Stress level above which telogen-effluvium hair loss occurs.</param>
    /// <param name="HairLossStressRate">Hair-density loss per unit time above the stress threshold.</param>
    /// <param name="HairLossPostpartumAmount">Hair-density loss applied at postpartum onset.</param>
    /// <param name="HairDensityRecoveryPerHour">Hair-density recovery per hour.</param>
    /// <param name="WrinklingAgeStart">Age at which wrinkling begins.</param>
    /// <param name="WrinklingRatePerYear">Wrinkle-score increase per year.</param>
    /// <param name="WrinklingCortisolBoost">Extra wrinkling per unit of chronic cortisol.</param>
    /// <param name="SarcopeniaAgeStart">Age at which muscle-mass decline (sarcopenia) begins.</param>
    /// <param name="SarcopeniaRatePerYear">Muscle-mass fraction lost per year.</param>
    /// <param name="SarcopeniaMuscleMin">Lower bound on muscle-mass fraction.</param>
    /// <param name="BoneDensityDeclineAgeStart">Age at which bone-density decline begins.</param>
    /// <param name="BoneDensityDeclinePerYear">Bone-density fraction lost per year.</param>
    /// <param name="BoneDensityMenopauseMultiplier">Multiplier on bone-density decline after menopause.</param>
    /// <param name="BoneFragilityInjuryMultiplier">Injury-severity amplification from low bone density.</param>
    /// <param name="AgeSleepQualityThreshold">Age above which sleep quality declines.</param>
    /// <param name="AgeSleepQualityPenaltyPerYear">Sleep-quality penalty accrued per year past the threshold.</param>
    /// <param name="MaxLifespanYears">Hard upper bound on character lifespan in years.</param>
    /// <param name="NaturalMortalityAlloWeight">Hourly mortality risk contribution per allostatic-load point above threshold.</param>
    /// <param name="NaturalMortalityAlloSpikeMultiplier">Risk multiplier applied above the allostatic spike threshold.</param>
    /// <param name="NaturalMortalityImmuneWeight">Hourly mortality risk contribution per immune-load point above threshold.</param>
    /// <param name="NaturalMortalityImmuneSpikeMultiplier">Risk multiplier applied above the immune (sepsis) spike threshold.</param>
    /// <param name="NaturalMortalityBoneFragilityWeight">Hourly mortality risk per unit of bone fragility below threshold.</param>
    /// <param name="NaturalMortalitySarcopeniaWeight">Hourly mortality risk per unit of sarcopenia.</param>
    /// <param name="NaturalMortalityMaxRiskPerHour">Hard cap on total hourly mortality risk.</param>
    /// <param name="EnergyDriftAwakePerHour">Energy drift per hour while awake.</param>
    /// <param name="EnergyDriftSelfCarePerHour">Energy drift per hour while doing self-care.</param>
    /// <param name="HungerDriftAwakePerHour">Hunger drift per hour while awake.</param>
    /// <param name="HungerDriftSleepPerHour">Hunger drift per hour while asleep (slowed metabolism).</param>
    /// <param name="HungerEatingGainPerHour">Hunger change per hour while eating (negative = reduces hunger).</param>
    /// <param name="ThirstDriftAwakePerHour">Thirst drift per hour while awake.</param>
    /// <param name="ThirstDriftSleepPerHour">Thirst drift per hour while asleep.</param>
    /// <param name="ThirstDrinkingGainPerHour">Thirst reduction magnitude per hour while drinking.</param>
    /// <param name="PainSelfCareRecoveryPerHour">Pain reduced per hour of self-care.</param>
    /// <param name="ImmuneDriftAwakePerHour">Immune-load drift per hour while awake.</param>
    /// <param name="ImmuneDriftSelfCarePerHour">Immune-load drift per hour while doing self-care.</param>
    public sealed record PhysiologyConfig(
        double RestingMetabolicRate = 1600,
        double MaxSleepDebtHours = 12,
        bool EnableMenstrualCycle = true,
        int MenstrualCycleBeginsInAge = 12,
        double EnergyRecoveryPerSleepHour = 10.0,
        double PainPassiveRecoveryPerHour = 0.3,
        double PainSleepRecoveryPerHour = 0.5,
        double BaseConceptionChancePerEncounter = 0.03,
        double OvulationConceptionMultiplier = 4.0,
        int PregnancyDiscoveryMinDays = 21,
        int PregnancyTermDays = 280,
        bool EnableNutrition = true,
        double NutritionDecayPerHour = 1.0,
        double CaloriesEatingGainPerHour = 40.0,
        double ProteinEatingGainPerHour = 20.0,
        double IronSleepRecoveryPerHour = 0.5,
        double InjuryRestRecoveryPerDay = 2.0,
        double InjuryActiveRecoveryPerDay = 0.5,
        double InjuryInfectionImmuneLoadPerDay = 5.0,
        double AllostaticLoadThresholdHunger = 70,
        double AllostaticLoadThresholdThirst = 70,
        double AllostaticLoadThresholdSleepDebt = 5,
        double AllostaticLoadThresholdPain = 50,
        double AllostaticLoadThresholdImmune = 60,
        double AllostaticLoadAccumRatePerHour = 0.5,
        double AllostaticLoadDecayRatePerHour = 0.1,
        /// <summary>
        /// Fraction of <see cref="AllostaticLoadDecayRatePerHour"/> applied during Idle.
        /// Science: passive rest (TV, phone, lying around) has negligible effect on allostatic
        /// load — the nervous system stays stress-adapted even without active demands.
        /// Active recovery (SelfCare) is required for meaningful reduction.
        /// Reference: McEwen 2006; reachlink.com allostatic load review 2026.
        /// Default 0.05.
        /// </summary>
        double AllostaticLoadIdleDecayFactor = 0.05,
        // Kortizol (HPA osa)
        double CortisolDiurnalPeakHour = 8.0,
        double CortisolDiurnalAmplitude = 30.0,
        double CortisolAlloWeight = 0.25,
        double CortisolImmuneWeight = 0.15,
        // Chronotyp + cirkadiánní fázový posun
        double ChronotypeOffsetHours = 0.0,
        double NaturalSleepStartHour = 22.0,
        double CircadianPhaseRecoveryPerHour = 0.08,
        // Recovery Debt
        double RecoveryDebtAccumAlloThreshold = 60.0,
        double RecoveryDebtAccumRatePerHour = 0.2,
        double RecoveryDebtDecayPerSleepHour = 0.15,
        double RecoveryDebtDecayPerSelfCareHour = 0.05,
        // Testosteron (mužský cyklus)
        bool EnableTestosteroneCycle = true,
        double TestosteronePeakHour = 8.0,
        double TestosteroneAlloSuppression = 0.20,
        double TestosteroneSleepDebtPenaltyPerHour = 0.8,
        // Sleep Inertia
        double SleepInertiaMaxHours = 1.5,
        // Sociální bolest (HPA aktivace při odmítnutí)
        double SocialPainCortisolSpike = 8.0,
        // SAM systém (Sympatho-Adrenomedullary — okamžitá sympatická odpověď)
        double AcuteArousalDecayPerHour = 200.0,
        double InjuryAcuteArousalSpike = 40.0,
        double NightmareAcuteArousalSpike = 25.0,
        double StressSpikedAcuteArousalWeight = 0.3,
        // Fyzická únava (svalová — odlišná od kognitivní SleepDebt)
        double PhysicalFatigueAccumPerWorkHour = 5.0,
        double PhysicalFatigueDecayPerSleepHour = 25.0,
        double PhysicalFatigueDecayPerIdleHour = 5.0,
        double PhysicalFatigueSelfCareDecayBonus = 8.0,
        // Glykemický stav
        double BloodGlucoseEatingGain = 50.0,
        double BloodGlucoseBaseDecayPerHour = 3.0,
        double BloodGlucoseDipDecayBonus = 8.0,
        double BloodGlucoseDipStartHours = 1.0,
        double BloodGlucoseDipEndHours = 2.0,
        // Hypocortisolismus paradox (HPA downregulace při extrémním AlloLoad)
        double HypocortisolismAlloThreshold = 75.0,
        double HypocortisolismDeclineRate = 0.1,
        // Sociální podpora jako kortizol buffer (Eisenberger 2007)
        double SocialSupportCortisolBuffer = 6.0,
        double SocialSupportClosenessThreshold = 50.0,
        // Chronická sociální izolace → kortizol (Cacioppo 2015)
        double SocialIsolationCortisolThreshold = 80.0,
        double SocialIsolationCortisolRatePerHour = 0.8,
        // Chronická bolest (Dantzer 2008)
        double ChronicPainAccumThreshold = 30.0,
        double ChronicPainDecayFactor = 0.5,
        // Cirkadiánní tělesná teplota (Waterhouse et al. 2005)
        double CircadianTempAmplitude = 0.3,
        double CircadianTempPeakHour = 17.0,
        // Věkové efekty
        int MenopauseAge = 50,
        double AgingEnergyRecoveryPenaltyStart = 40,
        double AgingEnergyRecoveryPenaltyPerYear = 0.005,
        double AgingImmuneBaselineStart = 60,
        double AgingImmuneBaselinePerYear = 0.2,
        double AgingTestosteronePenaltyStart = 25,
        double AgingTestosteronePenaltyPerYear = 0.8,
        // Altitude — hypoxie a AMS
        double AltitudeHypoxiaThreshold = 2000.0,
        double AltitudeAMSThreshold = 4000.0,
        double AltitudeEnergyDecayBonusPerKm = 0.3,
        double AltitudeAMSPainPerHour = 2.0,
        // Fyzické stárnutí — vlasy, vrásky, svalová hmota
        double HairGrowthCmPerHour = 0.00175,
        double HairGreyingAgeStart = 30.0,
        double HairGreyingRatePerYear = 0.02,
        double HairGreyingCortisolBoost = 0.0001,
        double HairLossAgeStartMale = 25.0,
        double HairLossRatePerYearMale = 0.005,
        double HairLossStressThreshold = 70.0,
        double HairLossStressRate = 0.0005,
        double HairLossPostpartumAmount = 0.15,
        double HairDensityRecoveryPerHour = 0.00002,
        double WrinklingAgeStart = 25.0,
        double WrinklingRatePerYear = 0.5,
        double WrinklingCortisolBoost = 0.001,
        double SarcopeniaAgeStart = 30.0,
        double SarcopeniaRatePerYear = 0.005,
        double SarcopeniaMuscleMin = 0.3,
        // Kostní hustota + osteoporóza
        double BoneDensityDeclineAgeStart = 30.0,
        double BoneDensityDeclinePerYear = 0.005,
        double BoneDensityMenopauseMultiplier = 2.5,
        double BoneFragilityInjuryMultiplier = 0.5,
        // Spánek a věk
        double AgeSleepQualityThreshold = 50.0,
        double AgeSleepQualityPenaltyPerYear = 0.008,
        // Sluneční světlo — restaurace vitaminu D
        /// <summary>
        /// Minimální <c>IrradianceFactor</c> ze <see cref="CelestialContext"/>, při kterém
        /// se vitamín D obnovuje (pod touto hodnotou záření nestačí ke kožní syntéze).
        /// </summary>
        double VitaminDSunThreshold = 0.3,
        /// <summary>
        /// Rychlost obnovy vitaminu D (0..100) na hodinu na jednotku <c>IrradianceFactor</c>
        /// při pobytu venku (SurfaceKind.Public nebo Social).
        /// </summary>
        double VitaminDRestorationPerHourPerIrradiance = 4.0,
        /// <summary>
        /// Maximální obnova vitaminu D za hodinu bez ohledu na výši ozáření.
        /// Chrání před nerealistickými hodnotami při velmi vysokém <c>IrradianceFactor</c>.
        /// </summary>
        double VitaminDMaxOutdoorRestorationPerHour = 6.0,
        // ── Natural mortality ─────────────────────────────────────────────────────
        // All hourly-risk values are calibrated against a 9360-hour game year
        // (VIWorld calendar: 10 months × 36 days × 26 hours). Annual probability of
        // a contributor with hourly risk r is 1 − (1 − r)^9360.
        double MaxLifespanYears = 110.0,
        /// <summary>
        /// Age at which the Gompertz mortality curve begins to contribute.
        /// </summary>
        double NaturalMortalityGompertzStart = 35.0,
        /// <summary>
        /// Steepness of the age-mortality exponential. 0.085 ≈ risk doubling every ~8 years (human-like).
        /// </summary>
        double NaturalMortalityGompertzScale = 0.085,
        /// <summary>
        /// Hourly mortality risk at <see cref="NaturalMortalityGompertzStart"/>.
        /// 1.3e-7/h ≈ 0.12 %/yr at the start age, rising to ~1 %/yr at 60 and ~5 %/yr at 80.
        /// </summary>
        double NaturalMortalityAgeBaseline = 1.3e-7,
        /// <summary>AllostaticLoad point above which it contributes to mortality (chronic HPA burden).</summary>
        double NaturalMortalityAlloThreshold = 80.0,
        double NaturalMortalityAlloWeight = 1.0e-6,    // AllostaticLoad contribution per point above threshold
        /// <summary>AllostaticLoad point above which an acute-decompensation spike multiplier applies.</summary>
        double NaturalMortalityAlloSpikeThreshold = 90.0,
        double NaturalMortalityAlloSpikeMultiplier = 10.0,
        /// <summary>ImmuneLoad point above which it contributes to mortality (systemic infection).</summary>
        double NaturalMortalityImmuneThreshold = 75.0,
        double NaturalMortalityImmuneWeight = 4.0e-6,  // ImmuneLoad contribution per point above threshold
        /// <summary>ImmuneLoad point above which an acute-sepsis spike multiplier applies.</summary>
        double NaturalMortalityImmuneSpikeThreshold = 90.0,
        double NaturalMortalityImmuneSpikeMultiplier = 8.0,
        /// <summary>Hunger level at or above which starvation mortality applies.</summary>
        double NaturalMortalityStarvationThreshold = 95.0,
        /// <summary>Hourly risk from terminal hunger. 0.0013/h → median death ~3 weeks.</summary>
        double NaturalMortalityStarvationRisk = 0.0013,
        /// <summary>Thirst level at or above which dehydration mortality applies.</summary>
        double NaturalMortalityDehydrationThreshold = 95.0,
        /// <summary>Hourly risk from terminal thirst. 0.008/h → median death ~3.5 days.</summary>
        double NaturalMortalityDehydrationRisk = 0.008,
        /// <summary>Energy level at or below which exhaustion mortality can apply.</summary>
        double NaturalMortalityExhaustionEnergyMax = 5.0,
        /// <summary>Sleep-debt hours at or above which exhaustion mortality can apply.</summary>
        double NaturalMortalityExhaustionSleepDebtMin = 48.0,
        /// <summary>Hourly risk from extreme energy depletion with sustained sleep debt.</summary>
        double NaturalMortalityExhaustionRisk = 0.001,
        /// <summary>BoneDensity (0..1) below which fragility-fracture mortality applies.</summary>
        double NaturalMortalityBoneFragilityThreshold = 0.25,
        double NaturalMortalityBoneFragilityWeight = 0.0002,
        double NaturalMortalitySarcopeniaWeight = 0.0001,
        double NaturalMortalityMaxRiskPerHour = 0.01,
        // ── Object affordance application ─────────────────────────────────────────
        /// <summary>
        /// Maximum hunger reduction applied by a single <c>UseInPlace</c> object interaction
        /// at full satisfaction (1.0). Scales linearly with <see cref="WorldObjectAffordance.Satisfaction"/>.
        /// Example: tavern roast (0.80) reduces Hunger by 0.80 × 25 = 20 pts.
        /// </summary>
        double AffordanceHungerMaxDelta = 25.0,

        /// <summary>
        /// Maximum thirst reduction applied by a single <c>UseInPlace</c> object interaction
        /// at full satisfaction (1.0).
        /// </summary>
        double AffordanceThirstMaxDelta = 20.0, // hard cap on hourly risk

        // ── Baseline action-driven drift (abstract [0..100] scale, not physical units) ──
        // Gameplay tuning knobs, not literature-derived constants. They live in config
        // (like every other rate in this engine) so the awake/sleep/consume rates stay
        // in one place and can be tuned per-scenario without recompiling.
        double EnergyDriftAwakePerHour = -2.0,
        double EnergyDriftSelfCarePerHour = -0.5,
        double HungerDriftAwakePerHour = 6.0,
        double HungerDriftSleepPerHour = 2.0,   // slowed metabolism during sleep
        double HungerEatingGainPerHour = -40.0, // negative: eating reduces hunger
        double ThirstDriftAwakePerHour = 8.0,
        double ThirstDriftSleepPerHour = 2.0,
        double ThirstDrinkingGainPerHour = 50.0, // magnitude; negated in ComputeDrift
        double PainSelfCareRecoveryPerHour = 10.0,
        double ImmuneDriftAwakePerHour = -0.3,
        double ImmuneDriftSelfCarePerHour = -0.5)
    {
        /// <summary>Parameterless constructor — all fields use their defaults.</summary>
        public PhysiologyConfig() : this(
            // ── Core metabolism ───────────────────────────────────────────────────────
            RestingMetabolicRate: 1600,
            MaxSleepDebtHours: 12,
            EnableMenstrualCycle: true,
            MenstrualCycleBeginsInAge: 12,
            EnergyRecoveryPerSleepHour: 10.0,
            PainPassiveRecoveryPerHour: 0.3,
            PainSleepRecoveryPerHour: 0.5,
            // ── Conception & pregnancy ────────────────────────────────────────────────
            BaseConceptionChancePerEncounter: 0.03,
            OvulationConceptionMultiplier: 4.0,
            PregnancyDiscoveryMinDays: 21,
            PregnancyTermDays: 280,
            // ── Nutrition ─────────────────────────────────────────────────────────────
            EnableNutrition: true,
            NutritionDecayPerHour: 1.0,
            CaloriesEatingGainPerHour: 40.0,
            ProteinEatingGainPerHour: 20.0,
            IronSleepRecoveryPerHour: 0.5,
            // ── Injury ────────────────────────────────────────────────────────────────
            InjuryRestRecoveryPerDay: 2.0,
            InjuryActiveRecoveryPerDay: 0.5,
            InjuryInfectionImmuneLoadPerDay: 5.0,
            // ── Allostatic load ───────────────────────────────────────────────────────
            AllostaticLoadThresholdHunger: 70,
            AllostaticLoadThresholdThirst: 70,
            AllostaticLoadThresholdSleepDebt: 12,   // CHANGED: 5 → 12 (1 bad night is not enough)
            AllostaticLoadThresholdPain: 50,
            AllostaticLoadThresholdImmune: 60,
            AllostaticLoadAccumRatePerHour: 0.15, // CHANGED: 0.5 → 0.15 (slower accumulation)
            AllostaticLoadDecayRatePerHour: 0.35, // CHANGED: 0.1 → 0.35 (meaningful recovery)
            AllostaticLoadIdleDecayFactor: 0.05, // NEW: passive rest has negligible recovery (McEwen 2006)
            // ── Cortisol (HPA axis) ───────────────────────────────────────────────────
            CortisolDiurnalPeakHour: 8.0,
            CortisolDiurnalAmplitude: 30.0,
            CortisolAlloWeight: 0.25,
            CortisolImmuneWeight: 0.15,
            HypocortisolismAlloThreshold: 75.0,
            HypocortisolismDeclineRate: 0.1,
            SocialSupportCortisolBuffer: 6.0,
            SocialSupportClosenessThreshold: 50.0,
            SocialIsolationCortisolThreshold: 80.0,
            SocialIsolationCortisolRatePerHour: 0.8,
            // ── Circadian rhythm ─────────────────────────────────────────────────────
            ChronotypeOffsetHours: 0.0,
            NaturalSleepStartHour: 22.0,
            CircadianPhaseRecoveryPerHour: 0.08,
            CircadianTempAmplitude: 0.3,
            CircadianTempPeakHour: 17.0,
            // ── Recovery debt ─────────────────────────────────────────────────────────
            RecoveryDebtAccumAlloThreshold: 60.0,
            RecoveryDebtAccumRatePerHour: 0.2,
            RecoveryDebtDecayPerSleepHour: 0.15,
            RecoveryDebtDecayPerSelfCareHour: 0.05,
            // ── Testosterone ──────────────────────────────────────────────────────────
            EnableTestosteroneCycle: true,
            TestosteronePeakHour: 8.0,
            TestosteroneAlloSuppression: 0.20,
            TestosteroneSleepDebtPenaltyPerHour: 0.8,
            // ── Sleep inertia & arousal ───────────────────────────────────────────────
            SleepInertiaMaxHours: 1.5,
            SocialPainCortisolSpike: 8.0,
            AcuteArousalDecayPerHour: 200.0,
            InjuryAcuteArousalSpike: 40.0,
            NightmareAcuteArousalSpike: 25.0,
            StressSpikedAcuteArousalWeight: 0.3,
            // ── Physical fatigue ──────────────────────────────────────────────────────
            PhysicalFatigueAccumPerWorkHour: 5.0,
            PhysicalFatigueDecayPerSleepHour: 25.0,
            PhysicalFatigueDecayPerIdleHour: 5.0,
            PhysicalFatigueSelfCareDecayBonus: 8.0,
            // ── Blood glucose ─────────────────────────────────────────────────────────
            BloodGlucoseEatingGain: 50.0,
            BloodGlucoseBaseDecayPerHour: 3.0,
            BloodGlucoseDipDecayBonus: 8.0,
            BloodGlucoseDipStartHours: 1.0,
            BloodGlucoseDipEndHours: 2.0,
            // ── Chronic pain ──────────────────────────────────────────────────────────
            ChronicPainAccumThreshold: 30.0,
            ChronicPainDecayFactor: 0.5,
            // ── Age effects ───────────────────────────────────────────────────────────
            MenopauseAge: 50,
            AgingEnergyRecoveryPenaltyStart: 40,
            AgingEnergyRecoveryPenaltyPerYear: 0.005,
            AgingImmuneBaselineStart: 60,
            AgingImmuneBaselinePerYear: 0.2,
            AgingTestosteronePenaltyStart: 25,
            AgingTestosteronePenaltyPerYear: 0.8,
            // ── Altitude ──────────────────────────────────────────────────────────────
            AltitudeHypoxiaThreshold: 2000.0,
            AltitudeAMSThreshold: 4000.0,
            AltitudeEnergyDecayBonusPerKm: 0.3,
            AltitudeAMSPainPerHour: 2.0,
            // ── Physical aging — hair ─────────────────────────────────────────────────
            HairGrowthCmPerHour: 0.00175,
            HairGreyingAgeStart: 30.0,
            HairGreyingRatePerYear: 0.02,
            HairGreyingCortisolBoost: 0.0001,
            HairLossAgeStartMale: 25.0,
            HairLossRatePerYearMale: 0.005,
            HairLossStressThreshold: 70.0,
            HairLossStressRate: 0.0005,
            HairLossPostpartumAmount: 0.15,
            HairDensityRecoveryPerHour: 0.00002,
            // ── Physical aging — skin & muscle ────────────────────────────────────────
            WrinklingAgeStart: 25.0,
            WrinklingRatePerYear: 0.5,
            WrinklingCortisolBoost: 0.001,
            SarcopeniaAgeStart: 30.0,
            SarcopeniaRatePerYear: 0.005,
            SarcopeniaMuscleMin: 0.3,
            // ── Physical aging — bone ─────────────────────────────────────────────────
            BoneDensityDeclineAgeStart: 30.0,
            BoneDensityDeclinePerYear: 0.005,
            BoneDensityMenopauseMultiplier: 2.5,
            BoneFragilityInjuryMultiplier: 0.5,
            // ── Sleep quality & aging ─────────────────────────────────────────────────
            AgeSleepQualityThreshold: 50.0,
            AgeSleepQualityPenaltyPerYear: 0.008,
            // ── Vitamin D ─────────────────────────────────────────────────────────────
            VitaminDSunThreshold: 0.3,
            VitaminDRestorationPerHourPerIrradiance: 4.0,
            VitaminDMaxOutdoorRestorationPerHour: 6.0,
            // ── Natural mortality ─────────────────────────────────────────────────────
            MaxLifespanYears: 110.0,
            NaturalMortalityGompertzStart: 35.0,
            NaturalMortalityGompertzScale: 0.085,
            NaturalMortalityAgeBaseline: 1.3e-7,
            NaturalMortalityAlloThreshold: 80.0,
            NaturalMortalityAlloWeight: 1.0e-6,
            NaturalMortalityAlloSpikeThreshold: 90.0,
            NaturalMortalityAlloSpikeMultiplier: 10.0,
            NaturalMortalityImmuneThreshold: 75.0,
            NaturalMortalityImmuneWeight: 4.0e-6,
            NaturalMortalityImmuneSpikeThreshold: 90.0,
            NaturalMortalityImmuneSpikeMultiplier: 8.0,
            NaturalMortalityStarvationThreshold: 95.0,
            NaturalMortalityStarvationRisk: 0.0013,
            NaturalMortalityDehydrationThreshold: 95.0,
            NaturalMortalityDehydrationRisk: 0.008,
            NaturalMortalityExhaustionEnergyMax: 5.0,
            NaturalMortalityExhaustionSleepDebtMin: 48.0,
            NaturalMortalityExhaustionRisk: 0.001,
            NaturalMortalityBoneFragilityThreshold: 0.25,
            NaturalMortalityBoneFragilityWeight: 0.0002,
            NaturalMortalitySarcopeniaWeight: 0.0001,
            NaturalMortalityMaxRiskPerHour: 0.01,
            // ── Object affordance ─────────────────────────────────────────────────────
            AffordanceHungerMaxDelta: 25.0,
            AffordanceThirstMaxDelta: 20.0
        )
        { }
    }

    /// <summary>
    /// Immutable per-tick physiological state of a character: core homeostatic levels plus
    /// the HPA-axis, circadian, aging and reproductive sub-states. Persisted in the
    /// <see cref="Characters.Core.EnginesSnapshot"/> and advanced each tick by the physiology engine.
    /// </summary>
    /// <param name="Energy">Available energy, 0..100.</param>
    /// <param name="SleepDebtHours">Accumulated sleep debt in hours (≥ 0).</param>
    /// <param name="Hunger">Hunger level, 0..100 (higher = hungrier).</param>
    /// <param name="Thirst">Thirst level, 0..100 (higher = thirstier).</param>
    /// <param name="Pain">Pain level, 0..100.</param>
    /// <param name="ImmuneLoad">Immune activation / illness burden, 0..100.</param>
    /// <param name="BodyTempDelta">Body-temperature deviation from baseline in °C.</param>
    /// <param name="Cycle">Menstrual-cycle sub-state, or <c>null</c> for characters without a cycle.</param>
    /// <param name="Pregnancy">Active pregnancy sub-state, or <c>null</c>.</param>
    /// <param name="Nutrition">Nutrition sub-state (calories, vitamins, glucose), or <c>null</c>.</param>
    /// <param name="Injury">Active injury sub-state, or <c>null</c>.</param>
    /// <param name="Postpartum">Postpartum recovery sub-state, or <c>null</c>.</param>
    public sealed record PhysiologyState(
        double Energy,          // 0..100
        double SleepDebtHours,  // >= 0
        double Hunger,          // 0..100
        double Thirst,          // 0..100
        double Pain,            // 0..100
        double ImmuneLoad,      // 0..100
        double BodyTempDelta,   // °C deviation
        MenstrualCycleState? Cycle,
        PregnancyState? Pregnancy = null,
        NutritionState? Nutrition = null,
        InjuryState? Injury = null,
        PostpartumState? Postpartum = null,
        /// <summary>
        /// Kumulativní allostatická zátěž — proxy HPA osy. Roste při chronickém neglektu
        /// potřeb (hlad, žízeň, spánkový dluh, bolest, imunitní aktivace). Klesá pouze při
        /// spánku nebo self-care. Chronicky elevovaná hodnota odráží hyperaktivaci HPA osy
        /// a predikuje zdravotní rizika (McEwen, 2000). 0..100.
        /// </summary>
        double AllostaticLoad = 0,
        /// <summary>
        /// Kortizolová hladina — explicitní výstup HPA osy. Sleduje diurnální křivku
        /// s vrcholem ~1 h po probuzení (Cortisol Awakening Response). Chronicky elevován
        /// allostatickou zátěží a imunitní aktivací. Zpětně zvyšuje stres a arousal
        /// v Psychology. 0..100; klidový normál ≈ 50.
        /// </summary>
        double CortisolLevel = 50,
        /// <summary>
        /// Celkový efektivní posun cirkadiánního rytmu od průměru (hodiny). Kombinuje
        /// stabilní chronotyp (<see cref="PhysiologyConfig.ChronotypeOffsetHours"/>) a
        /// aktuální jet-lag narušení. Kladné = ranní ptáče, záporné = noční sova.
        /// Psychology čte tuto hodnotu a posouvá Gaussovy arousal vrcholy. Rozsah −6..+6.
        /// </summary>
        double CircadianPhaseShiftHours = 0,
        /// <summary>
        /// Fyzický deficit regenerace nad rámec prostého spánkového dluhu. Roste při
        /// allostatické přetíženosti (AllostaticLoad &gt; threshold), klesá spánkem a
        /// self-care. Snižuje efektivitu obnovy energie při SleepEnded. 0..72 h.
        /// </summary>
        double RecoveryDebtHours = 0,
        /// <summary>
        /// Stav mužského testosteronového cyklu; <c>null</c> pro ženské postavy.
        /// Modeluje diurnální rytmus (vrchol ráno) a potlačení HPA-HPG cross-talkem
        /// při chronickém stresu a spánkovém dluhu.
        /// </summary>
        TestosteroneState? Testosterone = null,
        /// <summary>
        /// Zbývající hodiny sleep inertia po probuzení. Adenosin není ihned vyčistěn —
        /// prvních 1–2 h po SleepEnded je kognitivní výkon a arousal snížen (Borbély model).
        /// Klesá lineárně v Tick(); nastaveno po každém SleepEnded. 0..2.
        /// </summary>
        double SleepInertiaHours = 0,
        /// <summary>
        /// Akutní SAM aktivace — Sympatho-Adrenomedullary odpověď (adrenalin/noradrenalin).
        /// Trvá 5–15 minut (decay ~200/hod). Spikuje při fyzickém ohrožení, šoku, noční můře.
        /// Odlišné od HPA/kortizolu (minuty vs. hodiny). 0..100.
        /// </summary>
        double AcuteArousalLevel = 0,
        /// <summary>
        /// Fyzická svalová únava — odlišná od kognitivní únavy (SleepDebt) a celkové energie.
        /// Akumuluje se při fyzické práci (Work), klesá spánkem a odpočinkem.
        /// Při mírné úrovni (20–70) = stres buffer (endorfiny). Při >70 = Valence↓. 0..100.
        /// </summary>
        double PhysicalFatigueLevel = 0,
        /// <summary>
        /// Kumulativní počet dní s bolestí nad prahem (<see cref="PhysiologyConfig.ChronicPainAccumThreshold"/>).
        /// Chronická bolest (&gt;7 dní) mění psychologický profil: depresivní symptomy,
        /// trvalý Valence↓, erose MoodBaseline (Dantzer 2008; Eisenberger 2012).
        /// </summary>
        double ChronicPainDays = 0,
        /// <summary>
        /// Aktuální antikoncepční ochrana. Nastavena eventem <see cref="ContraceptionChanged"/>.
        /// Při &gt;= Moderate: potlačena ovulace a snížena závažnost PMDD.
        /// </summary>
        ContraceptionLevel CurrentContraception = ContraceptionLevel.Unspecified,
        /// <summary>
        /// Dynamický stav fyzického stárnutí (vlasy, vrásky, svalová hmota).
        /// <c>null</c> = aging systém ještě nebyl inicializován; inicializace proběhne při prvním Tick().
        /// </summary>
        PhysicalAgingState? Aging = null,
        /// <summary>
        /// Vital status of the character.
        /// Set to <see cref="StatusType.Dead"/> by <see cref="DefaultPhysiologyEngine"/>
        /// when a natural death occurs. Persisted in the snapshot so that
        /// <see cref="SimulationScene"/> can restore the dead-character set after save/load.
        /// </summary>
        StatusType Status = StatusType.Alive);

    /// <summary>
    /// The physiology engine — first stage of the per-character tick pipeline. Advances
    /// homeostatic levels, the HPA axis, circadian rhythm, reproductive cycles and aging.
    /// </summary>
    public interface IPhysiologyEngine : IEngine<PhysiologyState, PhysiologyConfig>
    {
        /// <summary>
        /// Restores serialized state and revalidates age-dependent subsystems
        /// (menstrual cycle, testosterone) against the current game time.
        /// Preferred over <see cref="IEngine{TState,TConfig}.RestoreState"/> when
        /// the character was generated in a different simulation context.
        /// </summary>
        /// <param name="state">The state to restore.</param>
        /// <param name="today">Current game time used to recompute age from birth date.</param>
        void RestoreState(PhysiologyState state, WDateOnly today);
    }

    // --- Menstruační modul ---

    /// <summary>Phase of the menstrual cycle.</summary>
    public enum CyclePhase
    {
        /// <summary>Menstruation (bleeding) phase.</summary>
        Menses,
        /// <summary>Follicular phase — between menses and ovulation.</summary>
        Follicular,
        /// <summary>Ovulation window — peak fertility.</summary>
        Ovulation,
        /// <summary>Luteal phase — between ovulation and the next menses; PMS occurs late here.</summary>
        Luteal,
        /// <summary>Cycle paused (e.g. pregnancy or contraception).</summary>
        Paused
    }

    /// <summary>
    /// Tuning constants for the menstrual-cycle module. Cycle lengths and variability are
    /// calibrated against Bull et al. 2019; stress/sleep parameters govern HPA-mediated anovulation.
    /// </summary>
    /// <param name="MensesMeanDays">Mean duration of menstruation in days.</param>
    /// <param name="PmsRisk">Probability (0..1) a character is PMS/PMDD-prone.</param>
    /// <param name="EnableOvulationWindowEvents">When <c>true</c>, ovulation-window events are emitted.</param>
    /// <param name="EnableSymptoms">When <c>true</c>, cyclic symptoms (pain, bloat, tenderness) are simulated.</param>
    /// <param name="MinCycleLengthDays">Lower bound on sampled cycle length.</param>
    /// <param name="MaxCycleLengthDays">Upper bound on sampled cycle length.</param>
    /// <param name="PainBaseMultiplier">Base multiplier on cyclic pain symptoms.</param>
    /// <param name="BloatBaseMultiplier">Base multiplier on cyclic bloat symptoms.</param>
    /// <param name="BreastTenderMultiplier">Base multiplier on cyclic breast-tenderness symptoms.</param>
    public sealed record MenstrualCycleConfig(
        /// <summary>Bull et al. 2019 (n = 3 324 cyklů): průměr 30,3 dní.</summary>
        int MeanCycleLengthDays = 30,
        /// <summary>Bull et al. 2019: SD 6,7 dní — folikulární fáze je hlavní zdroj variability.</summary>
        double VariabilityDaysStdDev = 6.7,
        int MensesMeanDays = 5,
        double PmsRisk = 0.35,
        bool EnableOvulationWindowEvents = true,
        bool EnableSymptoms = true,
        /// <summary>
        /// Střední délka luteální fáze (Bull et al. 2019: průměr 11,7 dne, SD 2,8).
        /// Engine z tohoto počítá dynamický den ovulace per-cyklus: ovulDay = length − LutealMeanDays.
        /// </summary>
        int LutealMeanDays = 12,
        int MinCycleLengthDays = 21,
        int MaxCycleLengthDays = 36,
        double PainBaseMultiplier = 1.0,
        double BloatBaseMultiplier = 1.0,
        double BreastTenderMultiplier = 1.0,
        /// <summary>
        /// Psychology stress level above which the HPA axis begins suppressing GnRH.
        /// Based on: Fenster et al. (1999); Schliep et al. (2015) — work stress and anovulation.
        /// Default 72 maps roughly to "chronic high stress" in the GET 0–100 scale.
        /// </summary>
        double AnovulatoryStressThreshold = 72.0,

        /// <summary>
        /// Sleep debt (hours) that contributes to HPA suppression load.
        /// Above this value, sleep debt adds to the suppression accumulator.
        /// Mechanism: sleep deprivation elevates cortisol, suppressing LH pulse frequency.
        /// </summary>
        double AnovulatorySleepDebtThresholdHours = 5.0,

        /// <summary>
        /// Number of consecutive game-days above suppression load threshold
        /// required before the current cycle is marked anovulatory.
        /// Prevents single-tick stress spikes from immediately suppressing ovulation.
        /// </summary>
        double AnovulatoryOnsetDays = 5.0,

        /// <summary>
        /// Number of consecutive game-days below suppression threshold
        /// required to clear anovulatory suppression.
        /// HPA axis recovery is slower than onset (asymmetric, like allostatic load).
        /// </summary>
        double AnovulatoryRecoveryDays = 3.0,
        /// <summary>Target bloat level (0..100) at the menstrual peak.</summary>
        double MensesBloatPeak = 55.0,
        /// <summary>Additional bloat target (0..100) ramping through the late luteal (PMS) phase.</summary>
        double LutealBloatPeak = 30.0,
        /// <summary>Target breast-tenderness level (0..100) during menstruation.</summary>
        double MensesBreastTenderPeak = 35.0,
        /// <summary>Target breast-tenderness level (0..100) at the late-luteal peak.</summary>
        double LutealBreastTenderPeak = 55.0,
        /// <summary>
        /// Per-day relaxation rate (0..1) at which cyclic symptoms (bloat, breast tenderness)
        /// track their phase target. Guarantees symptoms oscillate with the cycle and decay
        /// back toward zero in the follicular phase instead of accumulating monotonically.
        /// </summary>
        double SymptomTrackingRatePerDay = 0.6)
    {
        /// <summary>Parameterless constructor — all fields use their defaults.</summary>
        public MenstrualCycleConfig() : this(30, 6.7, 5, 0.35, true, true, 12, 21, 36, 1.0, 1.0, 1.0, 72, 5, 5, 3) { }
    }

    /// <summary>Runtime state of a character's menstrual cycle for the current tick.</summary>
    /// <param name="Phase">Current cycle phase.</param>
    /// <param name="DayInCycle">1-based day index within the current cycle.</param>
    /// <param name="OvulationWindow">True while the character is in the fertile ovulation window.</param>
    /// <param name="SymptomPain">Cyclic pain symptom level, 0..100.</param>
    /// <param name="SymptomBreastTender">Cyclic breast-tenderness level, 0..100.</param>
    /// <param name="SymptomBloat">Cyclic bloat level, 0..100.</param>
    /// <param name="LibidoMod">Libido multiplier for the current phase (≈ 0.5..1.5).</param>
    /// <param name="LastMensesStart">Date the most recent menstruation began.</param>
    public sealed record MenstrualCycleState(
        CyclePhase Phase,
        int DayInCycle,
        bool OvulationWindow,
        double SymptomPain,         // 0..100
        double SymptomBreastTender, // 0..100
        double SymptomBloat,        // 0..100
        double LibidoMod,           // multiplikátor 0.5..1.5
        WDateOnly LastMensesStart,
        /// <summary>
        /// Aktivní PMDD epizoda — nastává v pozdní luteální fázi u postav s PmsRisk &gt; 0.3.
        /// Způsobuje závažnější emocionální labilitu a vyšší Stress v Psychology.
        /// </summary>
        bool PmddActive = false,
        /// <summary>
        /// Skutečná délka aktuálního cyklu (vzorkovaná z normálního rozdělení při každém resetu na den 1).
        /// Použita pro výpočet dynamického dne ovulace: ovulDay = CurrentCycleLength − LutealMeanDays.
        /// Default = MeanCycleLengthDays při inicializaci.
        /// </summary>
        int CurrentCycleLength = 30,
        /// <summary>
        /// Accumulated consecutive game-days under suppressive stress/sleep-debt load.
        /// Counts up when load is above threshold, decays when load clears.
        /// When this exceeds <see cref="MenstrualCycleConfig.AnovulatoryOnsetDays"/>,
        /// <see cref="AnovulatoryCycleActive"/> is set to true.
        /// </summary>
        double StressSuppressionAccDays = 0.0,

        /// <summary>
        /// True when the current cycle will not produce an ovulation window.
        /// Set when suppression accumulator crosses the onset threshold.
        /// Cleared when the recovery accumulator crosses the recovery threshold.
        /// </summary>
        bool AnovulatoryCycleActive = false);

    /// <summary>Stav probíhajícího těhotenství postavy.</summary>
    /// <param name="OtherParent">Identity of the other parent.</param>
    /// <param name="ConceivedOn">Date of conception.</param>
    /// <param name="EstimatedDueDate">Estimated due date.</param>
    /// <param name="Discovered">Whether the pregnancy has been discovered by the character.</param>
    /// <param name="DiscoveredOn">Date the pregnancy was discovered, or <c>null</c>.</param>
    public sealed record PregnancyState(
        HumanId OtherParent,
        WDateOnly ConceivedOn,
        WDateOnly EstimatedDueDate,
        bool Discovered = false,
        WDateOnly? DiscoveredOn = null);

    /// <summary>Nutrition sub-state — dietary stores and blood glucose, each on a 0..100 scale.</summary>
    /// <param name="Calories">Energy availability from food, 0..100.</param>
    /// <param name="VitaminD">Vitamin D level from sun/diet exposure, 0..100.</param>
    /// <param name="Iron">Iron level, 0..100 (critical for post-menses recovery).</param>
    /// <param name="Protein">Protein level for muscle and tissue recovery, 0..100.</param>
    public sealed record NutritionState(
        double Calories = 80,           // 0..100; energy availability from food
        double VitaminD = 80,           // 0..100; sun/diet exposure
        double Iron = 80,               // 0..100; critical for female recovery post-menses
        double Protein = 80,            // 0..100; muscle and tissue recovery
        /// <summary>
        /// Hladina krevního cukru. Stoupá při jídle, klesá rebound dip 1–2 h po jídle.
        /// Pod 35 = hypoglykémie: iritabilita, špatná koncentrace, CogLoad↑. 0..100.
        /// </summary>
        double BloodGlucoseLevel = 80,
        /// <summary>Hodiny od posledního jídla; reset při Eat; řídí glycemický dip okno.</summary>
        double PostMealHours = 0);

    /// <summary>
    /// Per-object nutritional gains applied each hour while the character
    /// is performing an <c>Eat</c> or <c>Drink</c> action with this object.
    /// Values are in the same [0..100] scale as <see cref="NutritionState"/>.
    /// When a nutrient is not specified, its gain is zero (object does not provide it).
    /// </summary>
    public sealed record NutritionalProfile(
        /// <summary>Calories restored per hour. Default config value used when null.</summary>
        double? CalorieGain = null,
        /// <summary>Protein restored per hour.</summary>
        double? ProteinGain = null,
        /// <summary>Iron restored per hour.</summary>
        double? IronGain = null,
        /// <summary>Vitamin D restored per hour.</summary>
        double? VitaminDGain = null,
        /// <summary>Thirst reduced per hour (for drink objects).</summary>
        double? HydrationGain = null);

    /// <summary>Category of a physical injury.</summary>
    public enum InjuryType
    {
        /// <summary>Soft-tissue sprain.</summary>
        Sprain,
        /// <summary>Infection (raises immune load over time).</summary>
        Infection,
        /// <summary>Open wound.</summary>
        Wound
    }

    /// <summary>Active injury sub-state.</summary>
    /// <param name="Severity">Current injury severity, 0..100.</param>
    /// <param name="DaysSinceOnset">Days elapsed since the injury occurred.</param>
    /// <param name="Type">Injury category.</param>
    public sealed record InjuryState(
        double Severity,        // 0..100; current injury severity
        int DaysSinceOnset,     // days since injury occurred
        InjuryType Type);

    /// <summary>Stage of postpartum recovery.</summary>
    public enum PostpartumPhase
    {
        /// <summary>Immediately after birth.</summary>
        Immediate,
        /// <summary>First week postpartum.</summary>
        FirstWeek,
        /// <summary>Through the six-week recovery period.</summary>
        SixWeeks,
        /// <summary>Full recovery reached.</summary>
        FullRecovery
    }

    /// <summary>Postpartum recovery sub-state.</summary>
    /// <param name="DaysSinceBirth">Days elapsed since birth.</param>
    /// <param name="Phase">Current postpartum phase.</param>
    public sealed record PostpartumState(
        int DaysSinceBirth,
        PostpartumPhase Phase,
        /// <summary>
        /// Aktivní hormonální crash po porodu (propad estrogenu/progesteronu v 24–48 h).
        /// Způsobuje emocionální labilitu a zpomalené MoodBaseline recovery v Psychology.
        /// Automaticky deaktivován po 7 dnech.
        /// </summary>
        bool HormonalCrashActive = true,
        /// <summary>
        /// Probíhající kojení — prodlužuje prolaktin-mediovanou supresi libida.
        /// Nastaveno eventem <see cref="BreastfeedingChanged"/>.
        /// Při aktivním kojení je LibidoMod snížen o dalších ~30 %.
        /// </summary>
        bool IsBreastfeeding = false);

    /// <summary>
    /// Stav mužského testosteronového cyklu. Modeluje diurnální rytmus (vrchol v ranních
    /// hodinách) a potlačení při chronickém stresu (HPA-HPG osa cross-talk) a spánkovém
    /// dluhu. Inicializován pouze pro <see cref="SexBiology.Male"/>.
    /// </summary>
    /// <param name="Level">Testosterone level, 0..100 (60 ≈ average adult male).</param>
    public sealed record TestosteroneState(
        double Level = 60);  // 0..100; 60 = průměrný dospělý muž

    /// <summary>
    /// Dynamický stav fyzického stárnutí — ukládá runtime fyzické změny postavy.
    /// Součást <see cref="Characters.Core.EnginesSnapshot"/>; aktualizován v každém Tick().
    /// Na rozdíl od statického traitu <see cref="Characters.Traits.PhysicalAppearance"/> (genetika),
    /// tento record sleduje změny způsobené věkem, hormony, stresem a vnějšími událostmi.
    /// </summary>
    public sealed record PhysicalAgingState(
        /// <summary>Věk postavy v herních letech. Aktualizován DefaultPhysiologyEngine.Tick() z _birthDate.</summary>
        int AgeYears = 0,
        /// <summary>Aktuální délka vlasů (cm). Roste ~0,00175 cm/hod. HairCut eventem se nastavuje na novou hodnotu.</summary>
        double HairLengthCm = 5.0,
        /// <summary>Podíl šedivých vlasů (0..1). Roste s věkem (od ~30 let) a chronickým kortizolem.</summary>
        double GreyFraction = 0.0,
        /// <summary>Hustota/plnost vlasů (0..1). Klesá androgenní alopécií, stresovým telogen effluviem, postpartum.</summary>
        double HairDensity = 1.0,
        /// <summary>Skóre vrásek (0..100). Roste s věkem po 25 letech a akceleruje chronickým kortizolem.</summary>
        double WrinkleScore = 0.0,
        /// <summary>Podíl svalové hmoty (0..1). Klesá sarkopénií po 30. roce; min = SarcopeniaMuscleMin.</summary>
        double MuscleMassFraction = 1.0,
        /// <summary>
        /// Hustota kostní tkáně (0..1). Klesá postupně od 30 let; dramaticky po menopauze (bez estrogenu).
        /// Nízká hustota → amplifikuje závažnost zranění (osteoporóza → fraktury).
        /// </summary>
        double BoneDensity = 1.0);

    // Události

    /// <summary>Event — menstruation began.</summary>
    public sealed record MensesStarted(WDateTime OccurredAt, HumanId Human) : IDomainEvent;
    /// <summary>Event — menstruation ended.</summary>
    public sealed record MensesEnded(WDateTime OccurredAt, HumanId Human) : IDomainEvent;
    /// <summary>Event — the fertile ovulation window opened.</summary>
    public sealed record OvulationWindowOpened(WDateTime OccurredAt, HumanId Human) : IDomainEvent;
    /// <summary>Event — the cycle advanced to a new day/phase.</summary>
    public sealed record CycleDayAdvanced(WDateTime OccurredAt, HumanId Human, int DayInCycle, CyclePhase Phase) : IDomainEvent;

    /// <summary>
    /// Emitted when chronic stress or sleep debt suppresses ovulation for the current cycle.
    /// Subscribers (NarrativeFormatter, Psychology) can react to prolonged stress as a
    /// physiological consequence. The character does not consciously know this is happening.
    /// </summary>
    public sealed record CycleSuppressionStarted(
        WDateTime OccurredAt,
        HumanId Human,
        /// <summary>Stress level at suppression onset.</summary>
        double StressAtOnset) : IDomainEvent;

    /// <summary>
    /// Emitted when HPA suppression resolves and normal ovulation can resume next cycle.
    /// </summary>
    public sealed record CycleSuppressionLifted(WDateTime OccurredAt, HumanId Human) : IDomainEvent;
    /// <summary>Událost — postava otěhotněla po reprodukčně relevantním setkání.</summary>
    public sealed record PregnancyStarted(WDateTime OccurredAt, HumanId Human, HumanId OtherParent, WDateOnly EstimatedDueDate) : IDomainEvent;
    /// <summary>Událost — těhotenství je pro postavu zjistitelné / zjištěné.</summary>
    public sealed record PregnancyDiscovered(WDateTime OccurredAt, HumanId Human, HumanId OtherParent) : IDomainEvent;
    /// <summary>Událost — těhotenství doběhlo do porodu; nevytváří novou postavu.</summary>
    public sealed record ChildBorn(WDateTime OccurredAt, HumanId ParentA, HumanId ParentB) : IDomainEvent;
    /// <summary>Událost — postava obdržela zranění.</summary>
    public sealed record InjuryReceived(WDateTime OccurredAt, HumanId Human, double Severity, InjuryType Type) : IDomainEvent;
    /// <summary>Událost — zranění se zahojilo.</summary>
    public sealed record InjuryHealed(WDateTime OccurredAt, HumanId Human) : IDomainEvent;
    /// <summary>Událost — postava přešla do nové fáze šestinedělí.</summary>
    public sealed record PostpartumPhaseChanged(WDateTime OccurredAt, HumanId Human, PostpartumPhase Phase) : IDomainEvent;
    /// <summary>Událost — postava změnila antikoncepci.</summary>
    public sealed record ContraceptionChanged(WDateTime OccurredAt, HumanId Human, ContraceptionLevel Level) : IDomainEvent;
    /// <summary>Událost — postava si ostříhala vlasy na zadanou délku.</summary>
    public sealed record HairCut(WDateTime OccurredAt, HumanId Human, double NewLengthCm) : IDomainEvent;
    /// <summary>Událost — postava si obarvila vlasy (narrativní; resetuje šedivost pro zobrazení).</summary>
    public sealed record HairDyed(WDateTime OccurredAt, HumanId Human) : IDomainEvent;
    /// <summary>
    /// Událost — postava začala nebo ukončila kojení.
    /// Kojení prodlužuje prolaktin-mediovanou supresi libida přibližně o 30 % po dobu trvání.
    /// </summary>
    public sealed record BreastfeedingChanged(WDateTime OccurredAt, HumanId Human, bool IsBreastfeeding) : IDomainEvent;

    /// <summary>
    /// Odvozené fyziologické vitální parametry — čisté funkce stávajících stavů.
    /// Nejsou součástí simulačního loop; počítají se on-demand pro narrativu a UI.
    /// </summary>
    public sealed record PhysiologicalVitals(
        int HeartRateBpm,           // 40..200 bpm
        int SystolicBP,             // 90..200 mmHg
        int DiastolicBP,            // 60..120 mmHg
        double RespiratoryRate)     // 10..30 dechů/min
    {
        /// <summary>Vypočítá vitální parametry z existujícího fyzio+psycho stavu.</summary>
        public static PhysiologicalVitals Compute(PhysiologyState ph, PsychologyState ps)
        {
            var arousal = ps.Arousal;
            var stress = ps.Stress / 100.0;
            var cortisol = ph.CortisolLevel / 100.0;
            var acuteSAM = ph.AcuteArousalLevel / 100.0;
            var physFatigue = ph.PhysicalFatigueLevel / 100.0;

            // Srdeční tep: klidový 60 bpm + modulace arousal/SAM/fyzická zátěž/horečka
            // Kardiovaskulární stárnutí: arteriální tuhost → BP ↑ ~0,5 mmHg/rok po 30
            var ageBPBonus = ph.Aging is { AgeYears: > 30 } agingBP ? (agingBP.AgeYears - 30) * 0.5 : 0.0;

            var hr = 60 + arousal * 50 + acuteSAM * 60 + physFatigue * 30 + stress * 15;
            if (ph.BodyTempDelta > 1.0) hr += ph.BodyTempDelta * 10;

            // Krevní tlak: klidový 120/80 + stres/kortizol/SAM + věk
            var systolic = 120 + stress * 30 + cortisol * 15 + acuteSAM * 25 + ageBPBonus;
            var diastolic = 80 + stress * 15 + cortisol * 8 + acuteSAM * 12 + ageBPBonus * 0.4;

            // Dechová frekvence: klidová 14 + arousal/SAM/stres
            var rr = 14 + arousal * 8 + acuteSAM * 10 + stress * 4;

            return new PhysiologicalVitals(
                HeartRateBpm: (int)System.Math.Clamp(hr, 40, 200),
                SystolicBP: (int)System.Math.Clamp(systolic, 90, 200),
                DiastolicBP: (int)System.Math.Clamp(diastolic, 60, 120),
                RespiratoryRate: System.Math.Clamp(rr, 10, 30));
        }
    }
}
