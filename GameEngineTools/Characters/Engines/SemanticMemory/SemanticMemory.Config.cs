// SemanticMemory.Config.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    public sealed record SemanticMemoryConfig(
        double LearningRate = 0.18,
        double ContradictionRate = 0.08,
        double DecayPerDay = 0.01,
        double StabilityGainPerEvidence = 0.08,
        int PatternWindowSize = 6,
        int MinimumPatternSupport = 2,
        double ContradictionStabilityHit = 0.05)
    {
        public SemanticMemoryConfig() : this(0.18, 0.08, 0.01, 0.08, 6, 2, 0.05) { }
    }
}
