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
        double FeverArousalSuppressPerDegree = 0.04)
    {
        public PsychologyConfig() : this(0.02, 1.5, 0.5, 1.8, 0.4, 0.3, 5.0, 8.0, 0.04) { }
    }

    public sealed record PsychologyState(
        double Valence,    // -1..+1
        double Arousal,    //  0..1
        double Dominance,  //  0..1
        double Stress,     //  0..100
        double CognitiveLoad, // 0..100
        DiscreteEmotion DominantEmotion);

    public enum DiscreteEmotion
    { Neutral, Joy, Sadness, Anger, Fear, Disgust, Surprise, Tenderness, Pride, Shame }

    public interface IPsychologyEngine : IEngine<PsychologyState, PsychologyConfig>
    { }

    // Události
    public sealed record EmotionShifted(WDateTime OccurredAt, HumanId Human, DiscreteEmotion To, double Valence, double Arousal, double Dominance) : IDomainEvent;
    public sealed record StressSpiked(WDateTime OccurredAt, HumanId Human, double NewStress) : IDomainEvent;
}
