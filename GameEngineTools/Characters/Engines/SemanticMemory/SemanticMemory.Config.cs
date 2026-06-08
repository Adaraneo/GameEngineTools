// SemanticMemory.Config.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    /// <summary>
    /// Konfigurace <see cref="DefaultSemanticMemoryEngine"/>.
    /// </summary>
    public sealed record SemanticMemoryConfig(
        /// <summary>Learning rate for new beliefs (0–1 per piece of evidence).</summary>
        double LearningRate = 0.18,
        /// <summary>How quickly contradictory beliefs weaken (per event).</summary>
        double ContradictionRate = 0.08,
        /// <summary>Natural strength decay per day without contact.</summary>
        double DecayPerDay = 0.01,
        /// <summary>Stability increase per piece of evidence.</summary>
        double StabilityGainPerEvidence = 0.08,
        /// <summary>Number of recent episodes in the pattern window.</summary>
        int PatternWindowSize = 6,
        /// <summary>Minimum number of occurrences for full pattern weight (otherwise 0.45×).</summary>
        int MinimumPatternSupport = 2,
        /// <summary>Stability penalty on contradiction.</summary>
        double ContradictionStabilityHit = 0.05,
        // ── Attachment style modulation (Bartholomew-Horowitz 2D model) ──────────────
        /// <summary>Anxious attachment: hyperactivation → 1.30× faster learning.</summary>
        double AttachmentLearningBoostAnxious = 1.30,
        /// <summary>Avoidant attachment: deactivation → 0.75× slower learning.</summary>
        double AttachmentLearningDiscountAvoidant = 0.75,
        /// <summary>Avoidant attachment: suppression of the EmotionallySafe belief (0.45× weight).</summary>
        double AttachmentSafeDiscountAvoidant = 0.45,
        /// <summary>Disorganized attachment: unstable → 1.15× learning.</summary>
        double AttachmentLearningBoostDisorganized = 1.15,
        /// <summary>Anxious attachment: higher contradiction sensitivity → 1.20×.</summary>
        double AttachmentContradictionBoostAnxious = 1.20,
        /// <summary>Disorganized attachment: highest contradiction sensitivity → 1.40×.</summary>
        double AttachmentContradictionBoostDisorganized = 1.40,
        // ── Navarro 8× gap rule (Navarro et al. 2017) ────────────────────────────────
        /// <summary>
        /// If more than N × the average inter-interaction interval has elapsed,
        /// decay is multiplied by <see cref="NavarroDecayAccelerator"/>.
        /// </summary>
        int NavarroCriticalMultiple = 8,
        /// <summary>Decay multiplier when the Navarro threshold is exceeded (default 3×).</summary>
        double NavarroDecayAccelerator = 3.0)
    {
        /// <summary>Parameterless constructor required by the Options pattern.</summary>
        public SemanticMemoryConfig() : this(
            0.18, 0.08, 0.01, 0.08, 6, 2, 0.05,
            1.30, 0.75, 0.45, 1.15, 1.20, 1.40, 8, 3.0)
        { }
    }
}
