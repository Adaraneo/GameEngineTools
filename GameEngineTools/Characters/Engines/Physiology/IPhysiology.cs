// IPhysiology.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.World.Utils.Time;

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
        double SarcopeniaMuscleMin = 0.3)
    {
        public PhysiologyConfig() : this(1600, 12, true, 12, 10, 0.3, 0.5, 0.03, 4.0, 21, 280, true, 1.0, 40.0, 20.0, 0.5, 2.0, 0.5, 5.0, 70, 70, 5, 50, 60, 0.5, 0.1, 8.0, 30.0, 0.25, 0.15, 0.0, 22.0, 0.08, 60.0, 0.2, 0.15, 0.05, true, 8.0, 0.20, 0.8, 1.5, 8.0, 200.0, 40.0, 25.0, 0.3, 5.0, 25.0, 5.0, 8.0, 50.0, 3.0, 8.0, 1.0, 2.0, 75.0, 0.1, 6.0, 50.0, 80.0, 0.8, 30.0, 0.5, 0.3, 17.0, 50, 40, 0.005, 60, 0.2, 25, 0.8, 2000.0, 4000.0, 0.3, 2.0, 0.00175, 30.0, 0.02, 0.0001, 25.0, 0.005, 70.0, 0.0005, 0.15, 0.00002, 25.0, 0.5, 0.001, 30.0, 0.005, 0.3) { }
    }

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
        PhysicalAgingState? Aging = null);

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
        bool EnableSymptoms = true,
        int OvulationDayOfCycle = 14,
        int MinCycleLengthDays = 21,
        int MaxCycleLengthDays = 35,
        double PainBaseMultiplier = 1.0,
        double BloatBaseMultiplier = 1.0,
        double BreastTenderMultiplier = 1.0)
    {
        public MenstrualCycleConfig() : this(28, 2.0, 5, 0.35, true, true, 14, 21, 35, 1.0, 1.0, 1.0) { }
    }

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
        bool PmddActive = false);

    /// <summary>Stav probíhajícího těhotenství postavy.</summary>
    public sealed record PregnancyState(
        HumanId OtherParent,
        WDateOnly ConceivedOn,
        WDateOnly EstimatedDueDate,
        bool Discovered = false,
        WDateOnly? DiscoveredOn = null);

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

    public enum InjuryType { Sprain, Infection, Wound }

    public sealed record InjuryState(
        double Severity,        // 0..100; current injury severity
        int DaysSinceOnset,     // days since injury occurred
        InjuryType Type);

    public enum PostpartumPhase { Immediate, FirstWeek, SixWeeks, FullRecovery }

    public sealed record PostpartumState(
        int DaysSinceBirth,
        PostpartumPhase Phase,
        /// <summary>
        /// Aktivní hormonální crash po porodu (propad estrogenu/progesteronu v 24–48 h).
        /// Způsobuje emocionální labilitu a zpomalené MoodBaseline recovery v Psychology.
        /// Automaticky deaktivován po 7 dnech.
        /// </summary>
        bool HormonalCrashActive = true);

    /// <summary>
    /// Stav mužského testosteronového cyklu. Modeluje diurnální rytmus (vrchol v ranních
    /// hodinách) a potlačení při chronickém stresu (HPA-HPG osa cross-talk) a spánkovém
    /// dluhu. Inicializován pouze pro <see cref="SexBiology.Male"/>.
    /// </summary>
    public sealed record TestosteroneState(
        double Level = 60);  // 0..100; 60 = průměrný dospělý muž

    /// <summary>
    /// Dynamický stav fyzického stárnutí — ukládá runtime fyzické změny postavy.
    /// Součást <see cref="Characters.Core.EnginesSnapshot"/>; aktualizován v každém Tick().
    /// Na rozdíl od statického traitu <see cref="Characters.Traits.PhysicalAppearance"/> (genetika),
    /// tento record sleduje změny způsobené věkem, hormony, stresem a vnějšími událostmi.
    /// </summary>
    public sealed record PhysicalAgingState(
        /// <summary>Aktuální délka vlasů (cm). Roste ~0,00175 cm/hod. HairCut eventem se nastavuje na novou hodnotu.</summary>
        double HairLengthCm = 5.0,
        /// <summary>Podíl šedivých vlasů (0..1). Roste s věkem (od ~30 let) a chronickým kortizolem.</summary>
        double GreyFraction = 0.0,
        /// <summary>Hustota/plnost vlasů (0..1). Klesá androgenní alopécií, stresovým telogen effluviem, postpartum.</summary>
        double HairDensity = 1.0,
        /// <summary>Skóre vrásek (0..100). Roste s věkem po 25 letech a akceleruje chronickým kortizolem.</summary>
        double WrinkleScore = 0.0,
        /// <summary>Podíl svalové hmoty (0..1). Klesá sarkopénií po 30. roce; min = SarcopeniaMuscleMin.</summary>
        double MuscleMassFraction = 1.0);

    // Události
    public sealed record MensesStarted(WDateTime OccurredAt, HumanId Human) : IDomainEvent;
    public sealed record MensesEnded(WDateTime OccurredAt, HumanId Human) : IDomainEvent;
    public sealed record OvulationWindowOpened(WDateTime OccurredAt, HumanId Human) : IDomainEvent;
    public sealed record CycleDayAdvanced(WDateTime OccurredAt, HumanId Human, int DayInCycle, CyclePhase Phase) : IDomainEvent;
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
            var arousal     = ps.Arousal;
            var stress      = ps.Stress / 100.0;
            var cortisol    = ph.CortisolLevel / 100.0;
            var acuteSAM    = ph.AcuteArousalLevel / 100.0;
            var physFatigue = ph.PhysicalFatigueLevel / 100.0;

            // Srdeční tep: klidový 60 bpm + modulace arousal/SAM/fyzická zátěž/horečka
            var hr = 60 + arousal * 50 + acuteSAM * 60 + physFatigue * 30 + stress * 15;
            if (ph.BodyTempDelta > 1.0) hr += ph.BodyTempDelta * 10;

            // Krevní tlak: klidový 120/80 + stres/kortizol/SAM
            var systolic  = 120 + stress * 30 + cortisol * 15 + acuteSAM * 25;
            var diastolic = 80  + stress * 15 + cortisol * 8  + acuteSAM * 12;

            // Dechová frekvence: klidová 14 + arousal/SAM/stres
            var rr = 14 + arousal * 8 + acuteSAM * 10 + stress * 4;

            return new PhysiologicalVitals(
                HeartRateBpm:    (int)System.Math.Clamp(hr,       40,  200),
                SystolicBP:      (int)System.Math.Clamp(systolic,  90, 200),
                DiastolicBP:     (int)System.Math.Clamp(diastolic, 60, 120),
                RespiratoryRate: System.Math.Clamp(rr, 10, 30));
        }
    }
}
