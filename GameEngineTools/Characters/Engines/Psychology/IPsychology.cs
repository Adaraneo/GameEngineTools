// IPsychology.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Tuning constants for <see cref="IPsychologyEngine"/>. Governs PAD affect dynamics,
    /// the stress/HPA model, cognitive load, circadian arousal, sickness behaviour, the
    /// dual-control sexual model and per-emotion decay rates. Bound from <c>Characters:Psychology</c>.
    /// </summary>
    public sealed record PsychologyConfig(
        double BaselineAffectVariance = 0.02,
        double StressRecoveryRatePerHour = 1.5,
        double SleepQualityAffectWeight = 0.5,
        double CognitiveLoadSleepDebtWeight = 1.8,
        double CognitiveLoadPainWeight = 0.4,
        double CognitiveLoadStressWeight = 0.3,
        double CognitiveLoadRecoveryPerHour = 5.0,
        double FeverCognitiveLoadPerDegree = 8.0,
        double FeverArousalSuppressPerDegree = 0.04,
        bool EnableCircadianRhythm = true,
        double CircadianArousalPeakHour = 14.0,
        double CircadianArousalTroughHour = 3.0,
        double CircadianInfluence = 0.15,
        double MoodBaselineRecoveryPerHour = 0.5,
        double MoodBaselineHighStressThreshold = 80.0,
        double MoodBaselineAgreeablenessBonus = 0.3,
        double StressManifestationThreshold = 70.0,
        double StressManifestationHours = 4.0,
        double LowIronValencePenaltyPerUnit = 0.0003,
        double LowVitaminDMoodPenaltyPerHour = 0.2,
        double AllostaticLoadCognitiveWeight = 0.4,
        // Cortisol → psychology
        double CortisolStressWeight = 0.15,
        double CortisolArousalWeight = 0.008,
        // Testosterone → psychology
        double TestosteroneIntimacyWeight = 0.3,
        double TestosteroneStressResilienceWeight = 0.008,
        // Sleep inertia (must match PhysiologyConfig.SleepInertiaMaxHours for correct normalization)
        double SleepInertiaMaxHours = 1.5,
        // Hangry neutral bias (MacCormack 2019)
        double HangryNeutralBiasThreshold = 70.0,
        double HangryNeutralBiasStrength = 0.015,
        double HangryNeutralContextWindow = 0.25,
        // Sickness behavior — anhedonie (Dantzer 2007)
        double SicknessAnhedoniaImmuneThreshold = 50.0,
        double SicknessAnhedoniaRewardBlunting = 0.5,
        double SicknessLethargyArousalPenalty = 0.008,
        double SicknessBrainFogCogLoadBonus = 3.0,
        // SAM system → PAD
        double AcuteArousalPsychWeight = 0.6,
        // Physical fatigue → PAD
        double PhysicalFatigueHighThreshold = 70.0,
        double PhysicalFatigueMildThreshold = 20.0,
        double PhysicalFatigueValencePenalty = 0.0008,
        double PhysicalFatigueArousalPenalty = 0.005,
        double PhysicalFatigueStressReliefWeight = 0.5,
        // Glycemic state — hypoglycemia
        double HypoglycemiaThreshold = 35.0,
        double HypoglycemiaValencePenalty = 0.003,
        double HypoglycemiaCogLoadBonus = 4.0,
        // Yerkes-Dodson kortizol optimum (Lupien 2007)
        double CortisolOptimalLow = 55.0,
        double CortisolOptimalHigh = 75.0,
        double CortisolOptimalCogBonus = 1.0,
        // PMDD (PmsRisk amplifier)
        double PmddValencePenaltyPerHour = 0.002,
        double PmddStressBonus = 0.5,
        #region PMDD hormone-withdrawal weighting (Hantsoo & Epperson 2020; Schmidt et al. 2017)

        /// <summary>
        /// Estradiol level (GET 0..100 proxy scale) at/above which no extra estradiol-withdrawal
        /// penalty applies. Existing tuned placeholder, unchanged from the original implementation.
        /// </summary>
        double PmddEstradiolWithdrawalRef = 70.0,

        /// <summary>
        /// Progesterone level (GET 0..100 proxy scale) at/above which no extra progesterone/
        /// allopregnanolone-withdrawal penalty applies. Approximate proxy for the late-luteal
        /// progesterone peak the engine's CycleHormones model produces (~90-100 at ovulDay+7).
        /// </summary>
        double PmddProgesteroneWithdrawalRef = 60.0,

        /// <summary>
        /// Relative weight (0..1) given to progesterone/allopregnanolone withdrawal vs. estradiol
        /// withdrawal in the combined PMDD severity factor. Default 0.6 reflects the neurosteroid
        /// literature's emphasis on progesterone/allopregnanolone as the leading proximate driver
        /// (Hantsoo &amp; Epperson 2020), while keeping a real (not zero) estradiol contribution
        /// because the evidence does not cleanly isolate the two hormones (Schmidt et al. 1998
        /// showed estradiol add-back alone can also provoke symptoms).
        /// </summary>
        double PmddProgesteroneWithdrawalWeight = 0.6,

        #endregion PMDD hormone-withdrawal weighting
        // Postpartum hormonal crash
        double PostpartumCrashValenceLability = 0.05,
        double PostpartumCrashMoodBaselinePenalty = 0.3,
        // Ambient temperature → PAD (Anderson 2002)
        double AmbientTempHeatThreshold = 27.0,
        double AmbientTempColdThreshold = 15.0,
        double AmbientTempHeatValencePenalty = 0.008,
        double AmbientTempHeatArousalBonus = 0.005,
        double AmbientTempColdSocialBonus = 1.0,
        // Dehydration → cognitive deficit (Masento 2014)
        double DehydrationCogLoadThreshold = 50.0,
        double DehydrationCogLoadBonus = 3.0,
        // Hyperalgesia during illness (Dantzer 2007)
        double HyperalgesiaImmuneThreshold = 40.0,
        double HyperalgesiaMaxMultiplier = 0.5,
        // Chronic pain → depressive profile (Dantzer 2008)
        double ChronicPainOnsetDays = 7.0,
        double ChronicPainValencePenaltyPerDay = 0.002,
        double ChronicPainMoodBaselinePenaltyPerDay = 0.05,
        // Stress vulnerability at night (McEwen 1998) — cortisol modulates stress recovery
        double CircadianVulnerabilityMin = 0.3,
        double CircadianVulnerabilityScale = 50.0,
        // Serotonin IDO pathway (Dantzer 2007) — chronic immune activation dampens MoodBaseline recovery
        double SerotoninSuppressionImmuneThreshold = 60.0,
        double SerotoninMoodRecoveryDampening = 0.3,
        // Wanting vs. Liking — stress amplifies wanting/craving (Berridge 2025)
        double WantingStressThreshold = 60.0,
        double WantingNeedIntimacyBoostPerHour = 0.4,
        double WantingNeedSocialBoostPerHour = 0.2,
        // Altitude — cognitive deficit under hypoxia
        double AltitudeCogLoadThreshold = 2500.0,
        double AltitudeCogLoadBonusPerKm = 2.0,
        // ── Dual Control Model (Bancroft & Janssen 2000) ───────────────────────
        /// <summary>NeedIntimacy boost per hour per unit of SES above 0.5 baseline.</summary>
        double SESNeedIntimacyBoostPerHour = 0.5,
        /// <summary>NeedIntimacy inhibition per unit SIS1 per unit stress/100 per hour.</summary>
        double SIS1StressInhibitionWeight = 0.8,
        /// <summary>NeedIntimacy inhibition per unit SIS2 per unit crowding per hour.</summary>
        double SIS2CrowdingInhibitionWeight = 1.0,
        // ── Proxemics zone violation (Altman 1975) ─────────────────────────────
        /// <summary>
        /// Stress added per hour when in Intimate zone (&lt;0.45 m) without privacy.
        /// Applied after stressGrowthMult (Neuroticism modulation, E4).
        /// </summary>
        double ProxemicsIntimateZoneStressPerHour = 4.0,
        /// <summary>Stress added per hour when in Personal zone in a public/work context.</summary>
        double ProxemicsPersonalZoneStressPerHour = 1.5,
        // ── Privacy non-monotonicity (Altman 1975) ─────────────────────────────
        /// <summary>
        /// Stress per unit of crowding mismatch per hour (actual privacy &lt; desired).
        /// Introverts in public spaces accumulate stress proportional to the deficit.
        /// </summary>
        double PrivacyMismatchStressWeight = 6.0,
        /// <summary>
        /// Stress per unit of isolation excess per hour, applied to ALL characters whenever actual
        /// privacy exceeds desired (the personality threshold lives in desiredPrivacy, not here).
        /// Models Social Baseline Theory (Coan &amp; Sbarra 2015): solitude is intrinsically effortful;
        /// introverts have a higher solitude tolerance, not an exemption.
        /// </summary>
        double IsolationStressWeight = 3.0,
        /// <summary>
        /// Stress recovery bonus per hour when in a quiet private space (HasPrivacy + Noise &lt; 0.3).
        /// Reduces accumulated stress — models restorative environment effect (Kaplan 1995).
        /// </summary>
        double PrivacyRecoveryBonusPerHour = 0.8,
        /// <summary>
        /// Noise level above which ambient sound begins contributing to stress accumulation.
        /// Mapped from WHO threshold ~55 dB. Below this value effect is negligible.
        /// </summary>
        double NoiseStressThreshold = 0.55,
        /// <summary>
        /// Stress added per unit of noise above <see cref="NoiseStressThreshold"/> per hour.
        /// Neuroticism (via stressGrowthMult) further amplifies sensitivity.
        /// Reference: Glass &amp; Singer 1972; WHO community noise guidelines.
        /// </summary>
        double NoiseStressWeightPerHour = 0.08,
        /// <summary>
        /// Multiplier applied to noise-induced stress when the character is in their home territory
        /// (Identity.HomeLocationId == InteractionSurface.Location). Controllable noise has
        /// dramatically lower cortisol response than uncontrollable noise of equal intensity
        /// (Glass &amp; Singer 1972). Default 0.4 → 60 % reduction at home.
        /// </summary>
        double HomeNoiseStressMultiplier = 0.4,
        // Cognitive aging + perception (Salthouse 2009; Gates & Cooper 1991)
        double CognitivAgingThreshold = 60.0,
        double CognitiveAgingCogLoadPerYear = 0.3,
        double PerceptualAgingThreshold = 50.0,
        double PerceptualAgingCogLoadPerHour = 0.005,
        // Post-menopause — estrogen deficiency → mood
        double PostMenopauseMoodBaselinePenaltyPerHour = 0.002,
        // --- Per-emotion PAD valence decay multipliers (Verduyn & Lavrijsen 2015) ---
        // Multiplier < 1 = emotion lingers longer; multiplier > 1 = fades quickly.
        // Calibrated for ValenceDecayRate = 0.15/h:
        //   Fear/Surprise ~30 min  → mult 3.0  (very fast fade)
        //   Joy           ~2–4 h   → mult 1.0  (default)
        //   Anger         ~4–8 h   → mult 0.6
        //   Shame         ~3–6 h   → mult 0.4
        //   Sadness       ~120 h   → mult 0.06 (very slow fade)

        /// <summary>Valence decay multiplier when dominant emotion is Fear (Verduyn &amp; Lavrijsen 2015). Default 3.0 ≈ ~30 min duration.</summary>
        double EmotionDecayFear = 3.0,

        /// <summary>Valence decay multiplier when dominant emotion is Surprise. Default 3.0.</summary>
        double EmotionDecaySurprise = 3.0,

        /// <summary>Valence decay multiplier when dominant emotion is Disgust. Default 2.5.</summary>
        double EmotionDecayDisgust = 2.5,

        /// <summary>Valence decay multiplier when dominant emotion is Joy. Default 1.0.</summary>
        double EmotionDecayJoy = 1.0,

        /// <summary>Valence decay multiplier when dominant emotion is Pride. Default 0.8.</summary>
        double EmotionDecayPride = 0.8,

        /// <summary>Valence decay multiplier when dominant emotion is Tenderness. Default 0.7.</summary>
        double EmotionDecayTenderness = 0.7,

        /// <summary>Valence decay multiplier when dominant emotion is Anger. Default 0.6 ≈ ~4–8 h duration.</summary>
        double EmotionDecayAnger = 0.6,

        /// <summary>Valence decay multiplier when dominant emotion is Shame. Default 0.4.</summary>
        double EmotionDecayShame = 0.4,

        /// <summary>
        /// Valence decay multiplier when dominant emotion is Guilt. Default 0.5.
        /// Faster than Shame (0.4) because guilt resolves via reparative action.
        /// Guilt does NOT ruminate — unlike Shame, it does not predispose to depression.
        /// Source: Orth, Berking &amp; Burkhardt (2006, <i>PSPB</i> 32:1608–1619).
        /// </summary>
        double EmotionDecayGuilt = 0.5,

        /// <summary>Valence decay multiplier when dominant emotion is Sadness. Default 0.06 ≈ ~120 h duration.</summary>
        double EmotionDecaySadness = 0.06,

        /// <summary>
        /// When stress exceeds this threshold and DominantEmotion is Sadness/Shame/Anger,
        /// rumination blocks emotional decay. Factor: 1 - (stress/100 × RuminationDecayBlock).
        /// </summary>
        double RuminationStressThreshold = 60.0,

        /// <summary>Strength of rumination's blocking effect on valence decay. Default 0.7.</summary>
        double RuminationDecayBlock = 0.7,

        /// <summary>
        /// Base shame spike Valence delta per unit of NormViolationScore.
        /// Applied by DefaultPsychologyEngine when handling <see cref="GameEngineTools.Characters.Engines.Interactions.NormViolationOccurred"/>.
        /// Default −0.55; see NormViolationMath.ComputeShameSpike for personality scaling.
        /// Source: Singh &amp; Bhushan (2025, Frontiers in Psychology 16:1678930).
        /// </summary>
        double NormShameBaseValenceDelta = -0.55,

        /// <summary>
        /// Base shame spike Dominance delta per unit of NormViolationScore.
        /// Dominance drop is the defining signature of norm-violation shame vs. VAD-emergent shame.
        /// Default −0.65. Source: Sznycer (2016) — devaluation maps to loss of social standing.
        /// </summary>
        double NormShameBaseDominanceDelta = -0.65,

        /// <summary>
        /// Minimum norm violation score required to trigger a shame spike in PsychologyEngine.
        /// Below this threshold the event is considered too minor to register emotionally.
        /// Default 0.25.
        /// </summary>
        double NormShameMinViolationScore = 0.25,
        // ── Object affordance application ─────────────────────────────────────────
        /// <summary>
        /// Maximum Valence boost applied by a single MoodBoost affordance at full
        /// satisfaction (1.0). Scales linearly — candles (0.25) → +0.025 Valence.
        /// Range [0..2] on PAD scale maps to actual delta on [-1..+1].
        /// </summary>
        double AffordanceMoodBoostMaxValence = 0.10,

        /// <summary>
        /// Maximum MoodBaseline boost applied by a single MoodBoost affordance at full
        /// satisfaction (1.0). Persistent effect; slower to recover than Valence.
        /// </summary>
        double AffordanceMoodBoostMaxMoodBaseline = 2.0,

        /// <summary>
        /// Maximum Stress reduction applied by a single Warmth affordance at full
        /// satisfaction (1.0). Models cold-stress relief (Nakamura 2011).
        /// </summary>
        double AffordanceWarmthMaxStressRelief = 4.0,

        /// <summary>
        /// Maximum Valence boost from a Social affordance (communal spaces, campfire).
        /// Scales by satisfaction × (NeedBelonging / 100) so lonely characters benefit more.
        /// </summary>
        double AffordanceSocialMaxValence = 0.08,

        /// <summary>
        /// Maximum Stress added by a single StressRaise affordance at full satisfaction (1.0).
        /// Models threat/hazard presence — fire, weapons, intimidating environment.
        /// </summary>
        double AffordanceStressRaiseMaxStress = 12.0,
        // ── Appraisal-based emotion generation (Scherer CPM) ──────────────────────
        // Per-dimension weights used by AppraisalEmotionMap to translate appraisal checks into a
        // PAD delta. Values are the meta-analytic appraisal→emotion link strengths (relative weights,
        // not probabilities). Source: Yeo & Ong 2024, Psychological Bulletin 150(12).
        /// <summary>Weight of intrinsic pleasantness on the appraisal Valence delta (pleasantness→affection r≈.57). Source: Yeo &amp; Ong 2024.</summary>
        double AppraisalPleasantnessValenceWeight = 0.57,
        /// <summary>Weight of goal-conduciveness on the appraisal Valence delta (goal-conduciveness→joy r≈.56). Source: Yeo &amp; Ong 2024.</summary>
        double AppraisalGoalConducivenessValenceWeight = 0.56,
        /// <summary>Weight of realised loss (negative conduciveness) on the appraisal Valence delta (loss→sadness r≈.42). Source: Yeo &amp; Ong 2024.</summary>
        double AppraisalLossValenceWeight = 0.42,
        /// <summary>Weight of threat on the appraisal Arousal delta (threat→fear r≈.47). Source: Yeo &amp; Ong 2024.</summary>
        double AppraisalThreatArousalWeight = 0.47,
        /// <summary>Weight of novelty on the appraisal Arousal delta. Source: Yeo &amp; Ong 2024 (novelty/suddenness check).</summary>
        double AppraisalNoveltyArousalWeight = 0.25,
        /// <summary>Weight of agency/coping on the appraisal Dominance delta (self-accountability→pride/control). Source: Yeo &amp; Ong 2024; Roseman 1996.</summary>
        double AppraisalAgencyDominanceWeight = 0.30,
        /// <summary>Overall scaling of the appraisal-driven PAD nudge so a single event does not saturate PAD. Default 0.5.</summary>
        double AppraisalPadDeltaScale = 0.5,
        /// <summary>
        /// Maps the Physiology Van Dongen cognitive deficit [0..~1] onto Psychology CognitiveLoad
        /// points. Implements the sleep-restriction dose-response on cognition: 6 h restriction
        /// approaches one night of total deprivation. Source: Van Dongen et al. 2003, <i>Sleep</i> 26(2).
        /// </summary>
        double CognitiveDeficitCogLoadWeight = 25.0,
        /// <summary>
        /// MoodBaseline recovery applied when the character reengages on an alternative goal after
        /// disengaging from a blocked one — the well-being benefit of adaptive goal adjustment.
        /// Source: Wrosch et al. 2003, <i>PSPB</i> 29(12). Default 4.0.
        /// </summary>
        double GoalReengagementMoodRecovery = 4.0,
        /// <summary>
        /// Iron level (0..100 scale) below which low-iron psychological effects trigger
        /// (Valence suppression). Mapped conceptually to the WHO ferritin deficiency cut-off.
        /// </summary>
        /// <remarks>
        /// Source: WHO guideline on use of ferritin concentrations to assess iron status in
        /// individuals and populations, Geneva: WHO; 2020 (official guideline; ferritin &lt;15 ug/L
        /// adult deficiency threshold, scaled onto the engine's 0..100 nutrition axis).
        /// </remarks>
        double IronDeficiencyThreshold = 30,
        /// <summary>
        /// Vitamin D level (0..100 scale) below which low-VitaminD psychological effects trigger
        /// (MoodBaseline suppression). Mapped conceptually to the IOM sufficiency cut-off.
        /// </summary>
        /// <remarks>
        /// Source: Institute of Medicine, Dietary Reference Intakes for Calcium and Vitamin D,
        /// National Academies Press, 2011 (official guideline; serum 25(OH)D &lt;50 nmol/L / 20 ng/mL
        /// insufficiency threshold, scaled onto the engine's 0..100 nutrition axis).
        /// Note: the Endocrine Society 2011 guideline (Holick MF et al., JCEM 96(7):1911-1930,
        /// DOI 10.1210/jc.2011-0385) uses a stricter 30 ng/mL cut-off — this is a genuine unresolved
        /// guideline conflict in the source literature, not an engine error. IOM's lower/looser
        /// threshold was chosen as the default to avoid over-triggering deficiency effects.
        /// </remarks>
        double VitaminDDeficiencyThreshold = 20)
    {
        /// <summary>Parameterless constructor — all fields use their defaults.</summary>
        public PsychologyConfig() : this(0.02, 1.5, 0.5, 1.8, 0.4, 0.3, 5.0, 8.0, 0.04, true, 14.0, 3.0, 0.15, 0.5, 80.0, 0.3, 70.0, 4.0, 0.0003, 0.2, 0.4, 0.15, 0.008, 0.3, 0.008, 1.5, 70.0, 0.015, 0.25, 50.0, 0.5, 0.008, 3.0, 0.6, 70.0, 20.0, 0.0008, 0.005, 0.5, 35.0, 0.003, 4.0, 55.0, 75.0, 1.0, 0.002, 0.5, 0.05, 0.3, 27.0, 15.0, 0.008, 0.005, 1.0, 50.0, 3.0, 40.0, 0.5, 7.0, 0.002, 0.05, 0.3, 50.0, 60.0, 0.3, 60.0, 0.4, 0.2, 2500.0, 2.0, 0.5, 0.8, 1.0, 4.0, 1.5, 6.0, 60.0, 0.3, 50.0, 0.005, 0.002, 3.0, 3.0, 2.5, 1.0, 0.8, 0.7, 0.6, 0.4, 0.06, 60.0, 0.7, -0.55, -0.65, 0.25, 0.10, 2.0, 4.0, 0.08, 12.0, 0.57, 0.56, 0.42, 0.47, 0.25, 0.30, 0.5, 25.0, 4.0, 30, 20) { }
    }

    /// <summary>
    /// Continuous PAD emotional state plus derived scalars (stress, cognitive load, mood
    /// baseline) and runtime motivations. Committed each tick by <see cref="IPsychologyEngine"/>.
    /// </summary>
    /// <param name="Valence">Pleasure dimension, −1..+1.</param>
    /// <param name="Arousal">Activation dimension, 0..1.</param>
    /// <param name="Dominance">Control dimension, 0..1.</param>
    /// <param name="Stress">HPA-axis stress level, 0..100.</param>
    /// <param name="CognitiveLoad">Cognitive load, 0..100.</param>
    /// <param name="DominantEmotion">Discrete emotion inferred from the PAD state.</param>
    /// <param name="MoodBaseline">Persistent underlying mood, 0..100 (neutral = 50).</param>
    /// <param name="Motivations">Runtime motivation/need levels, or <c>null</c>.</param>
    public sealed record PsychologyState(
        double Valence,    // -1..+1
        double Arousal,    //  0..1
        double Dominance,  //  0..1
        double Stress,     //  0..100
        double CognitiveLoad, // 0..100
        DiscreteEmotion DominantEmotion,
        double MoodBaseline = 50.0,        // 0..100; persistent underlying mood, neutral=50
        MotivationState? Motivations = null); // runtime need levels

    /// <summary>Discrete emotion inferred from the continuous PAD state.</summary>
    public enum DiscreteEmotion
    {
        /// <summary>No salient emotion.</summary>
        Neutral,

        /// <summary>Joy / happiness.</summary>
        Joy,

        /// <summary>Sadness.</summary>
        Sadness,

        /// <summary>Anger (approach-motivated).</summary>
        Anger,

        /// <summary>Fear.</summary>
        Fear,

        /// <summary>Disgust.</summary>
        Disgust,

        /// <summary>Surprise.</summary>
        Surprise,

        /// <summary>Tenderness / affection.</summary>
        Tenderness,

        /// <summary>Pride.</summary>
        Pride,

        /// <summary>Shame — self-focused, withdrawal-motivated.</summary>
        Shame,

        /// <summary>
        /// Guilt — behavior-focused, approach-motivated, reparative.
        /// Triggered when an action violates the character's Benevolence or Universalism values.
        /// VAD: V≈−0.55, A≈+0.45, D≈−0.20 (higher Dominance than Shame = approach vs. withdrawal).
        /// Decay: 0.5 (faster than Shame's 0.4; resolves on reparative action).
        /// Source: Tangney &amp; Dearing (2002); Singh &amp; Bhushan (2025, PMC12647085).
        /// </summary>
        Guilt
    }

    /// <summary>
    /// Runtime motivational drive levels (each 0..100) plus the sickness-withdrawal flag.
    /// Computed by Psychology and read by the Behavior engine's need engines.
    /// </summary>
    /// <param name="NeedSocial">Loneliness/connection driver, 0..100.</param>
    /// <param name="NeedIntimacy">Sexual/romantic driver, 0..100.</param>
    /// <param name="NeedAchievement">Accomplishment driver, 0..100.</param>
    /// <param name="NeedCare">Nurturing driver (peaks postpartum), 0..100.</param>
    /// <param name="NeedSafety">Security/predictability driver, 0..100.</param>
    /// <param name="SicknessWithdraw">Immune-driven social-withdrawal flag.</param>
    public sealed record MotivationState(
        double NeedSocial = 50,        // 0..100; loneliness/connection driver
        double NeedIntimacy = 50,      // 0..100; sexual/romantic driver
        double NeedAchievement = 50,   // 0..100; accomplishment driver
        double NeedCare = 50,          // 0..100; nurturing driver (peaks postpartum)
        double NeedSafety = 50,        // 0..100; security/predictability driver
        bool SicknessWithdraw = false); // sickness behavior: immune-driven social withdrawal

    /// <summary>
    /// The psychology engine — second stage of the tick pipeline. Computes PAD affect,
    /// stress, cognitive load and motivations from the physiological state and events.
    /// </summary>
    public interface IPsychologyEngine : IEngine<PsychologyState, PsychologyConfig>
    { }

    // Events

    /// <summary>Event — the dominant emotion / PAD state shifted.</summary>
    public sealed record EmotionShifted(WDateTime OccurredAt, HumanId Human, DiscreteEmotion To, double Valence, double Arousal, double Dominance) : IDomainEvent;
    /// <summary>
    /// Event — an emotion was generated via Scherer-CPM appraisal of an incoming event
    /// (as opposed to PAD-only inference). Emitted for debugging/observability of the appraisal path.
    /// </summary>
    /// <param name="OccurredAt">Game time at which the appraisal occurred.</param>
    /// <param name="Human">The appraising character.</param>
    /// <param name="Emotion">The emotion selected by <c>AppraisalEmotionMap</c>.</param>
    /// <param name="GoalConduciveness">Goal-conduciveness check value [−1..+1] that drove the appraisal.</param>
    /// <param name="DeltaValence">Valence delta applied to PAD.</param>
    /// <param name="DeltaArousal">Arousal delta applied to PAD.</param>
    /// <param name="DeltaDominance">Dominance delta applied to PAD.</param>
    public sealed record EmotionAppraised(WDateTime OccurredAt, HumanId Human, DiscreteEmotion Emotion, double GoalConduciveness, double DeltaValence, double DeltaArousal, double DeltaDominance) : IDomainEvent;
    /// <summary>Event — stress rose sharply.</summary>
    public sealed record StressSpiked(WDateTime OccurredAt, HumanId Human, double NewStress) : IDomainEvent;
    /// <summary>Event — motivational drive levels changed.</summary>
    public sealed record MotivationChanged(WDateTime OccurredAt, HumanId Human, MotivationState Previous, MotivationState Next) : IDomainEvent;
    /// <summary>Event — sustained high stress manifested as an observable behaviour.</summary>
    public sealed record StressManifested(WDateTime OccurredAt, HumanId Human, string Manifestation) : IDomainEvent;
}
