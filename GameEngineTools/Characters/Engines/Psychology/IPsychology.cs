// IPsychology.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

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
        // Kortizol → psychika
        double CortisolStressWeight = 0.15,
        double CortisolArousalWeight = 0.008,
        // Testosteron → psychika
        double TestosteroneIntimacyWeight = 0.3,
        double TestosteroneStressResilienceWeight = 0.008,
        // Sleep Inertia (musí souhlasit s PhysiologyConfig.SleepInertiaMaxHours pro správné normování)
        double SleepInertiaMaxHours = 1.5,
        // Hangry neutrální bias (MacCormack 2019)
        double HangryNeutralBiasThreshold = 70.0,
        double HangryNeutralBiasStrength = 0.015,
        double HangryNeutralContextWindow = 0.25,
        // Sickness behavior — anhedonie (Dantzer 2007)
        double SicknessAnhedoniaImmuneThreshold = 50.0,
        double SicknessAnhedoniaRewardBlunting = 0.5,
        double SicknessLethargyArousalPenalty = 0.008,
        double SicknessBrainFogCogLoadBonus = 3.0,
        // SAM systém → PAD
        double AcuteArousalPsychWeight = 0.6,
        // Fyzická únava → PAD
        double PhysicalFatigueHighThreshold = 70.0,
        double PhysicalFatigueMildThreshold = 20.0,
        double PhysicalFatigueValencePenalty = 0.0008,
        double PhysicalFatigueArousalPenalty = 0.005,
        double PhysicalFatigueStressReliefWeight = 0.5,
        // Glykemický stav — hypoglykémie
        double HypoglycemiaThreshold = 35.0,
        double HypoglycemiaValencePenalty = 0.003,
        double HypoglycemiaCogLoadBonus = 4.0,
        // Yerkes-Dodson kortizol optimum (Lupien 2007)
        double CortisolOptimalLow = 55.0,
        double CortisolOptimalHigh = 75.0,
        double CortisolOptimalCogBonus = 1.0,
        // PMDD (PmsRisk amplifikátor)
        double PmddValencePenaltyPerHour = 0.002,
        double PmddStressBonus = 0.5,
        // Postpartum hormonal crash
        double PostpartumCrashValenceLability = 0.05,
        double PostpartumCrashMoodBaselinePenalty = 0.3,
        // Ambientní teplota → PAD (Anderson 2002)
        double AmbientTempHeatThreshold = 27.0,
        double AmbientTempColdThreshold = 15.0,
        double AmbientTempHeatValencePenalty = 0.008,
        double AmbientTempHeatArousalBonus = 0.005,
        double AmbientTempColdSocialBonus = 1.0,
        // Dehydratace → kognitivní deficit (Masento 2014)
        double DehydrationCogLoadThreshold = 50.0,
        double DehydrationCogLoadBonus = 3.0,
        // Hyperalgezie při nemoci (Dantzer 2007)
        double HyperalgesiaImmuneThreshold = 40.0,
        double HyperalgesiaMaxMultiplier = 0.5,
        // Chronická bolest → depresivní profil (Dantzer 2008)
        double ChronicPainOnsetDays = 7.0,
        double ChronicPainValencePenaltyPerDay = 0.002,
        double ChronicPainMoodBaselinePenaltyPerDay = 0.05,
        // Stresová vulnerabilita v noci (McEwen 1998) — kortizol moduluje stress recovery
        double CircadianVulnerabilityMin = 0.3,
        double CircadianVulnerabilityScale = 50.0,
        // Serotonin IDO pathway (Dantzer 2007) — chronická imunita tlumí MoodBaseline recovery
        double SerotoninSuppressionImmuneThreshold = 60.0,
        double SerotoninMoodRecoveryDampening = 0.3,
        // Wanting vs. Liking — stres amplifikuje wanting/craving (Berridge 2025)
        double WantingStressThreshold = 60.0,
        double WantingNeedIntimacyBoostPerHour = 0.4,
        double WantingNeedSocialBoostPerHour = 0.2,
        // Altitude — kognitivní deficit při hypoxii
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
        /// Stress per unit of isolation excess per hour, applied only when Extraversion &gt; 0.6
        /// and actual privacy exceeds desired. Models Altman (1975) non-monotonic privacy optimum.
        /// </summary>
        double IsolationStressWeight = 3.0,
        /// <summary>
        /// Stress recovery bonus per hour when in a quiet private space (HasPrivacy + Noise &lt; 0.3).
        /// Reduces accumulated stress — models restorative environment effect (Kaplan 1995).
        /// </summary>
        double PrivacyRecoveryBonusPerHour = 0.8,
        // Kognitivní stárnutí + percepce (Salthouse 2009; Gates & Cooper 1991)
        double CognitivAgingThreshold = 60.0,
        double CognitiveAgingCogLoadPerYear = 0.3,
        double PerceptualAgingThreshold = 50.0,
        double PerceptualAgingCogLoadPerHour = 0.005,
        // Post-menopauza — estrogen deficience → nálada
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

        /// <summary>Valence decay multiplier when dominant emotion is Sadness. Default 0.06 ≈ ~120 h duration.</summary>
        double EmotionDecaySadness = 0.06,

        /// <summary>
        /// When stress exceeds this threshold and DominantEmotion is Sadness/Shame/Anger,
        /// rumination blocks emotional decay. Factor: 1 - (stress/100 × RuminationDecayBlock).
        /// </summary>
        double RuminationStressThreshold = 60.0,

        /// <summary>Strength of rumination's blocking effect on valence decay. Default 0.7.</summary>
        double RuminationDecayBlock = 0.7)
    {
        public PsychologyConfig() : this(0.02, 1.5, 0.5, 1.8, 0.4, 0.3, 5.0, 8.0, 0.04, true, 14.0, 3.0, 0.15, 0.5, 80.0, 0.3, 70.0, 4.0, 0.0003, 0.2, 0.4, 0.15, 0.008, 0.3, 0.008, 1.5, 70.0, 0.015, 0.25, 50.0, 0.5, 0.008, 3.0, 0.6, 70.0, 20.0, 0.0008, 0.005, 0.5, 35.0, 0.003, 4.0, 55.0, 75.0, 1.0, 0.002, 0.5, 0.05, 0.3, 27.0, 15.0, 0.008, 0.005, 1.0, 50.0, 3.0, 40.0, 0.5, 7.0, 0.002, 0.05, 0.3, 50.0, 60.0, 0.3, 60.0, 0.4, 0.2, 2500.0, 2.0, 0.5, 0.8, 1.0, 4.0, 1.5, 6.0, 60.0, 0.3, 50.0, 0.005, 0.002, 3.0, 3.0, 2.5, 1.0, 0.8, 0.7, 0.6, 0.4, 0.06, 60.0, 0.7) { }
    }

    public sealed record PsychologyState(
        double Valence,    // -1..+1
        double Arousal,    //  0..1
        double Dominance,  //  0..1
        double Stress,     //  0..100
        double CognitiveLoad, // 0..100
        DiscreteEmotion DominantEmotion,
        double MoodBaseline = 50.0,        // 0..100; persistent underlying mood, neutral=50
        MotivationState? Motivations = null); // runtime need levels

    public enum DiscreteEmotion
    { Neutral, Joy, Sadness, Anger, Fear, Disgust, Surprise, Tenderness, Pride, Shame }

    public sealed record MotivationState(
        double NeedSocial = 50,        // 0..100; loneliness/connection driver
        double NeedIntimacy = 50,      // 0..100; sexual/romantic driver
        double NeedAchievement = 50,   // 0..100; accomplishment driver
        double NeedCare = 50,          // 0..100; nurturing driver (peaks postpartum)
        double NeedSafety = 50,        // 0..100; security/predictability driver
        bool SicknessWithdraw = false); // sickness behavior: immune-driven social withdrawal

    public interface IPsychologyEngine : IEngine<PsychologyState, PsychologyConfig>
    { }

    // Události
    public sealed record EmotionShifted(WDateTime OccurredAt, HumanId Human, DiscreteEmotion To, double Valence, double Arousal, double Dominance) : IDomainEvent;
    public sealed record StressSpiked(WDateTime OccurredAt, HumanId Human, double NewStress) : IDomainEvent;
    public sealed record MotivationChanged(WDateTime OccurredAt, HumanId Human, MotivationState Previous, MotivationState Next) : IDomainEvent;
    public sealed record StressManifested(WDateTime OccurredAt, HumanId Human, string Manifestation) : IDomainEvent;
}
