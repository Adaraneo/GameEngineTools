// RelationshipsConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    /// <summary>
    /// Configuration for <see cref="IRelationshipsEngine"/>.
    /// </summary>
    public sealed record RelationshipsConfig(
        double DecayPerDay = 1.5,
        double RepairGain = 6.0,
        double RupturePenalty = 8.0,
        double MereExposureMaxBoost = 15.0,
        int MereExposureSaturation = 20,
        double FamiliarityDecayFloor = 10.0,
        double AttractionPlasticityPerInteraction = 0.25,

        // ── TransgressionResidue ────────────────────────────────────────────────
        /// <summary>
        /// Proportional power-law decay per day: residue *= (1 - rate)^days.
        /// Default 0.04 gives half-life ≈ 17 days.
        /// </summary>
        double TransgressionDecayRatePerDay = 0.04,
        /// <summary>Residue added per MicroNegative event.</summary>
        double TransgressionMicroNegativeGain = 3.0,
        /// <summary>Residue added when an intimate advance (InviteIntimacy) is rejected.</summary>
        double TransgressionRejectionGain = 6.0,

        // ── Navarro 8× gap rule ────────────────────────────────────────────────
        /// <summary>
        /// Expected inter-contact interval in days used by Navarro's 8× gap rule.
        /// If (now - LastContactTime) > 8 × this value, decay is multiplied.
        /// Default 14 days (roughly weekly contact for an acquaintance).
        /// </summary>
        double ExpectedContactIntervalDays = 14.0,
        /// <summary>
        /// Decay multiplier applied when the Navarro gap threshold is exceeded.
        /// Navarro et al. (2017): hazard of tie death spikes beyond 8× mean inter-contact interval.
        /// </summary>
        double NavarrGapMultiplier = 3.0,

        // ── CommunalStrength ───────────────────────────────────────────────────
        /// <summary>
        /// CommunalStrength increase per accepted intimate touch or sexual encounter.
        /// Clark &amp; Mills 2012: communal norms grow from intimacy, not task completion.
        /// </summary>
        double CommunalGrowthPerIntimateInteraction = 1.5,

        // ── Familiarity–Like non-monotonicity ──────────────────────────────────
        /// <summary>
        /// Like points lost per day when Familiarity is high (> 55) but
        /// no contact has occurred for &gt; 30 days.
        /// Models Norton, Frost &amp; Ariely 2007: overexposure without renewal erodes liking.
        /// </summary>
        double FamiliarityLikeDissonancePenalty = 0.04,

        // ── Attachment modulation ──────────────────────────────────────────────
        /// <summary>
        /// Maximum Closeness reduction (in points) applied to characters with full Avoidance (1.0).
        /// E.g. 40 means dismissing characters cap at Closeness 60 instead of 100.
        /// </summary>
        double ClosenessAvoidanceCap = 40.0,
        /// <summary>
        /// Additional rejection sting multiplier per unit of Attachment.Anxiety.
        /// At Anxiety = 1.0, sting is amplified by (1 + RejectionAnxietyAmplifier).
        /// </summary>
        double RejectionAnxietyAmplifier = 0.6)
    {
        /// <summary>Parameterless constructor required by DI options binding.</summary>
        public RelationshipsConfig() : this(
            DecayPerDay: 1.5,
            RepairGain: 6.0,
            RupturePenalty: 8.0,
            MereExposureMaxBoost: 15.0,
            MereExposureSaturation: 20,
            FamiliarityDecayFloor: 10.0,
            AttractionPlasticityPerInteraction: 0.25,
            TransgressionDecayRatePerDay: 0.04,
            TransgressionMicroNegativeGain: 3.0,
            TransgressionRejectionGain: 6.0,
            ExpectedContactIntervalDays: 14.0,
            NavarrGapMultiplier: 3.0,
            CommunalGrowthPerIntimateInteraction: 1.5,
            FamiliarityLikeDissonancePenalty: 0.04,
            ClosenessAvoidanceCap: 40.0,
            RejectionAnxietyAmplifier: 0.6) { }
    }
}
