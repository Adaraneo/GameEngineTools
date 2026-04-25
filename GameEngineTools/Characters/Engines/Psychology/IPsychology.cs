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
        double TestosteroneStressResilienceWeight = 0.008)
    {
        public PsychologyConfig() : this(0.02, 1.5, 0.5, 1.8, 0.4, 0.3, 5.0, 8.0, 0.04, true, 14.0, 3.0, 0.15, 0.5, 80.0, 0.3, 70.0, 4.0, 0.0003, 0.2, 0.4, 0.15, 0.008, 0.3, 0.008) { }
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
