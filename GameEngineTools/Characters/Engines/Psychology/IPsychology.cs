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
        double WantingNeedSocialBoostPerHour = 0.2)
    {
        public PsychologyConfig() : this(0.02, 1.5, 0.5, 1.8, 0.4, 0.3, 5.0, 8.0, 0.04, true, 14.0, 3.0, 0.15, 0.5, 80.0, 0.3, 70.0, 4.0, 0.0003, 0.2, 0.4, 0.15, 0.008, 0.3, 0.008, 1.5, 70.0, 0.015, 0.25, 50.0, 0.5, 0.008, 3.0, 0.6, 70.0, 20.0, 0.0008, 0.005, 0.5, 35.0, 0.003, 4.0, 55.0, 75.0, 1.0, 0.002, 0.5, 0.05, 0.3, 27.0, 15.0, 0.008, 0.005, 1.0, 50.0, 3.0, 40.0, 0.5, 7.0, 0.002, 0.05, 0.3, 50.0, 60.0, 0.3, 60.0, 0.4, 0.2) { }
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
