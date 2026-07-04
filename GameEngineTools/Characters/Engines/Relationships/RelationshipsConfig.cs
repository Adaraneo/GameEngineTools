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
        double RejectionAnxietyAmplifier = 0.6,

        // ── Per-dimension decay multipliers ───────────────────────────────────
        // Applied as: effectiveRate = DecayPerDay × days × depthFactor × navarrMultiplier × DimMultiplier
        // Calibrated for an established relationship (depthFactor ≈ 0.5) to match empirical
        // half-lives from Roberts & Dunbar 2011/2015, Saramaki 2014, Burt 2000/2002:
        /// <summary>
        /// Decay multiplier for Trust. Default 0.06 → half-life ~18 months for established bonds
        /// (Roberts &amp; Dunbar 2011; Slovic 1993: step-drop on betrayal, slow passive decay).
        /// </summary>
        double DecayMultiplierTrust = 0.06,
        /// <summary>
        /// Decay multiplier for Respect. Default 0.04 → half-life ~24 months
        /// (Fiske relational models; reputation trait: slow, asymmetric).
        /// </summary>
        double DecayMultiplierRespect = 0.04,
        /// <summary>
        /// Decay multiplier for Closeness. Default 0.35 → half-life ~9 months
        /// (Roberts &amp; Dunbar 2011: non-kin friendships; kin ties do not decay).
        /// </summary>
        double DecayMultiplierCloseness = 0.35,
        /// <summary>
        /// Decay multiplier for Like. Default 0.28 → half-life ~4 months
        /// (state-reactive: reacts faster than Trust to absence).
        /// </summary>
        double DecayMultiplierLike = 0.28,
        /// <summary>
        /// Decay multiplier for Comfort. Default 0.80 → half-life ~2–3 months.
        /// </summary>
        double DecayMultiplierComfort = 0.80,
        /// <summary>
        /// Decay multiplier for RomanticInterest. Default 1.00 → half-life ~3 months
        /// (passion decline well-replicated; Saramaki 2014).
        /// </summary>
        double DecayMultiplierRomanticInterest = 1.00,
        /// <summary>
        /// Decay multiplier for SexualInterest. Default 1.50 → half-life ~2 months
        /// (fastest-decaying dimension; Coolidge / habituation effects; boosted by novelty).
        /// Applies only when SexualInterest is at or above <see cref="TonicSexualInterestThreshold"/>
        /// (phasic passion zone). Below the threshold the tonic multiplier applies instead.
        /// </summary>
        double DecayMultiplierSexualInterest = 1.50,
        /// <summary>
        /// SexualInterest value below which tonic (baseline physical desire) logic applies.
        /// Below this threshold, the decay rate is reduced by <see cref="TonicSexualInterestDecayFactor"/>.
        /// Above it, the full phasic <see cref="DecayMultiplierSexualInterest"/> applies.
        /// Default 40.0.
        /// </summary>
        double TonicSexualInterestThreshold = 40.0,

        /// <summary>
        /// Multiplier applied to <see cref="DecayMultiplierSexualInterest"/> when
        /// SexualInterest is below <see cref="TonicSexualInterestThreshold"/>.
        /// Default 0.30 → tonic zone decays at 1.50 × 0.30 = 0.45 per day
        /// instead of 1.50, giving a half-life of roughly 5–6 months.
        /// Reference: Baumeister, Catanese &amp; Vohs (2001).
        /// </summary>
        double TonicSexualInterestDecayFactor = 0.30,

        /// <summary>
        /// Fraction of normalised PhysicalAttraction used to seed SexualInterest
        /// at <see cref="GameEngineTools.Characters.Engines.Relationships.FirstImpressionFormed"/>.
        /// Models the involuntary lust component triggered by physical attractiveness
        /// (Regan &amp; Berscheid 1999). Orientation weight is applied multiplicatively on top,
        /// so cross-orientation pairs receive a proportionally smaller seed.
        /// Default 0.50 → PhysicalAttraction 70 → SexualInterest seed ≈ 35.
        /// Clamped to [0, 45] so first impression only seeds the tonic baseline,
        /// never phasic passion (which requires repeated intimate interaction).
        /// </summary>
        double SexualInterestSeedFactor = 0.50,
        /// <summary>
        /// Decay multiplier for Familiarity. Default 0.08 → very slow decay
        /// (knowledge of the person persists long after felt familiarity fades).
        /// </summary>
        double DecayMultiplierFamiliarity = 0.08,

        // ── Dunbar finite attention budget ────────────────────────────────────
        // Saramaki et al. 2014 (PNAS): total communication effort is roughly fixed.
        // When Tier-1 relationships exceed their soft capacity, lower-tier ties
        // receive less maintenance → decay rate increases proportionally.
        /// <summary>
        /// Closeness threshold for Tier-1 (intimate support clique, ~5 people).
        /// Edges at or above this value are counted as intimate ties.
        /// </summary>
        double DunbarTier1Threshold = 70.0,
        /// <summary>
        /// Closeness threshold for Tier-2 (close friends, ~15 people).
        /// Edges between this and <see cref="DunbarTier1Threshold"/> are Tier-2.
        /// </summary>
        double DunbarTier2Threshold = 40.0,
        /// <summary>Soft capacity for Tier-1 intimate relationships (Dunbar ~5).</summary>
        int DunbarTier1Capacity = 5,
        /// <summary>Soft capacity for Tier-2 close-friend relationships (Dunbar ~15).</summary>
        int DunbarTier2Capacity = 15,
        /// <summary>
        /// Additional decay multiplier applied to lower-tier edges per excess Tier-1 relationship.
        /// E.g. 0.15 means each extra intimate tie raises Tier-2/3 decay by 15 %.
        /// </summary>
        double AttentionBudgetPressurePerExcessTier1 = 0.15,
        /// <summary>
        /// Additional decay multiplier applied to Tier-3/4 edges per excess Tier-2 relationship.
        /// </summary>
        double AttentionBudgetPressurePerExcessTier2 = 0.05,

        // ── Dominance / Prestige status perception ────────────────────────────
        // Cheng et al. 2013 (JPSP); Redhead et al. 2019 (R. Soc. Open Sci.):
        // Two independent status axes with different behavioural consequences.
        /// <summary>
        /// Decay multiplier for PerceivedDominance toward neutral (50).
        /// Status impressions fade slowly — default 0.08 → half-life ~years.
        /// </summary>
        double DecayMultiplierDominance = 0.08,
        /// <summary>
        /// Decay multiplier for PerceivedPrestige toward neutral (50).
        /// </summary>
        double DecayMultiplierPrestige = 0.08,
        /// <summary>
        /// Delta added to PerceivedPrestige when a PositiveAct is witnessed via ThirdPartyActionObserved.
        /// </summary>
        double PrestigeGainPerPositiveAct = 2.0,
        /// <summary>
        /// Delta added to PerceivedDominance when a NegativeAct/Betrayal is witnessed.
        /// </summary>
        double DominanceGainPerNegativeAct = 3.0,
        /// <summary>
        /// Delta added to PerceivedPrestige when target's accepted SelfDisclosure is witnessed.
        /// Models intellectual/emotional admiration.
        /// </summary>
        double PrestigeGainPerSelfDisclosure = 1.0,
        /// <summary>
        /// Delta added to PerceivedDominance when ContemptuousAct is received from target.
        /// Contempt signals aggression and coercive intent — strongly signals dominance.
        /// </summary>
        double DominanceGainPerContempt = 10.0,
        /// <summary>
        /// Utility bonus per point of PerceivedPrestige above 50 added to ReachOut candidate.
        /// High-prestige targets attract social approach.
        /// </summary>
        double PrestigeReachOutBonusPerPoint = 0.06,
        /// <summary>
        /// Utility penalty per point of PerceivedDominance above 70 when Closeness &lt; 30.
        /// Dominant strangers trigger avoidance; close dominant figures do not.
        /// </summary>
        double DominanceAvoidancePenaltyPerPoint = 0.08,

        // ── Investment model (Rusbult; Le & Agnew 2003; Tran et al. 2019) ──────
        /// <summary>
        /// Comparison Level baseline subtracted from blended outcomes to yield satisfaction.
        /// Satisfaction = mean(Like, Closeness, Comfort) − ComparisonLevelBaseline (Thibaut &amp; Kelley 1959).
        /// </summary>
        double ComparisonLevelBaseline = 45.0,
        /// <summary>Weight of accumulated InvestmentSize in the commitment integrator.</summary>
        double CommitmentInvestmentWeight = 0.6,
        /// <summary>Weight of AlternativeQuality (CL_alt) subtracted in the commitment integrator.</summary>
        double CommitmentAlternativeWeight = 0.5,
        /// <summary>Per-day rate at which current Commitment drifts toward its computed target.</summary>
        double CommitmentDriftPerDay = 0.08,
        /// <summary>
        /// Daily InvestmentSize growth per point of Closeness above
        /// <see cref="DunbarTier2Threshold"/>. Never decays once accumulated.
        /// </summary>
        double InvestmentGrowthPerDay = 0.02,
        /// <summary>
        /// IntimateAffinity threshold above which an edge is treated as romantic for CL_alt
        /// computation. Below this, AlternativeQuality stays 0 (platonic bonds have no romantic alternative).
        /// </summary>
        double RomanticEdgeIntimacyThreshold = 30.0,
        /// <summary>
        /// Max fractional reduction of Closeness/IntimateAffinity decay at Commitment = 100.
        /// E.g. 0.6 → fully committed bonds decay at 40 % of base rate (stickiness).
        /// </summary>
        double CommitmentDecayResistance = 0.6,
        /// <summary>
        /// Commitment threshold below which a partner edge emits
        /// <see cref="RelationshipDissolutionConsidered"/> (one-shot on the downward crossing).
        /// </summary>
        double DissolutionCommitmentThreshold = 15.0,

        /// <summary>
        /// Interval (game days) between periodic edge snapshots (EventId 2005) emitted
        /// from the decay pass. Mutations log 2005 immediately; this keeps edge state
        /// observable in logs even through long interaction-free stretches, so log
        /// files can be rotated/deleted without losing relationship visibility.
        /// </summary>
        double EdgeSnapshotIntervalDays = 1.0,

        // ── Community reputation (indirect reciprocity; Nowak & Sigmund 2005) ───
        /// <summary>
        /// Weight converting a newcomer's community trust prior (from
        /// <see cref="GameEngineTools.Characters.Engines.Reputation.CommunityReputationLedger"/>)
        /// into a Trust offset at <see cref="FirstImpressionFormed"/>. The prior is centred on
        /// <see cref="GameEngineTools.Characters.Engines.Reputation.ReputationMath.DefaultTrustPrior"/>
        /// (0.4); the seeded Trust is shifted by <c>(prior − 0.4) × weight</c>. Default 80 maps the
        /// prior range [0.15..0.7] to a Trust offset of roughly [−20..+24] — a good local reputation
        /// makes a stranger trusted on arrival, a bad one makes them suspect.
        /// Applied only when the impression carries a prior (null = no community signal known).
        /// </summary>
        double ReputationTrustPriorWeight = 80.0,

        // ── Authority Ranking relational model (Fiske AR; Zakharin & Bates 2023) ──
        /// <summary>
        /// Extra Respect (deference) gained per accepted interaction with a perceived <i>superior</i>
        /// when the interaction surface's <see cref="GameEngineTools.Characters.Engines.Interactions.RelationalModel"/>
        /// is <see cref="GameEngineTools.Characters.Engines.Interactions.RelationalModel.AuthorityRanking"/>.
        /// Scaled by how far above neutral (50) the superior is perceived. Default 1.5.
        /// AR is a <b>relational type</b> (how the bond updates) — orthogonal to the individual
        /// Dominance/Prestige routes to status (Cheng 2013). Source: Zakharin &amp; Bates 2023; Fiske 1992.
        /// </summary>
        double AuthorityRankingDeferenceRespect = 1.5,

        /// <summary>
        /// Extra Trust (loyalty) gained per accepted interaction with a perceived superior in an
        /// Authority-Ranking context, scaled by perceived superiority. Default 1.0.
        /// </summary>
        double AuthorityRankingLoyaltyTrust = 1.0,

        // ── Four Horsemen (Gottman 1994, descriptive only — see ContemptuousActPerformed) ──
        /// <summary>Comfort penalty per DefensiveActPerformed (Gottman 1994, descriptive category only).</summary>
        double DefensivenessComfortPenalty = 3.0,
        /// <summary>Trust penalty per DefensiveActPerformed.</summary>
        double DefensivenessTrustPenalty = 1.5,
        /// <summary>TransgressionResidue gain per DefensiveActPerformed.</summary>
        double TransgressionDefensivenessGain = 4.0,
        /// <summary>Closeness penalty per StonewallingActPerformed.</summary>
        double StonewallingClosenessPenalty = 4.0,
        /// <summary>TransgressionResidue gain per StonewallingActPerformed.</summary>
        double TransgressionStonewallingGain = 5.0,

        // ── Demand/withdraw conflict trajectory (Schrodt, Witt & Shimkowski 2014) ──────
        /// <summary>
        /// DemandWithdrawScore gained per negative-behavior event (MicroNegative,
        /// DefensiveActPerformed, StonewallingActPerformed) without an intervening repair.
        /// </summary>
        double DemandWithdrawGainPerNegativeEvent = 5.0,
        /// <summary>DemandWithdrawScore reduction per accepted RepairAttempt.</summary>
        double DemandWithdrawRepairReduction = 12.0,
        /// <summary>
        /// Weight of DemandWithdrawScore subtracted in the commitment integrator. Kept small
        /// per Kanter, Lavner, Lannin, Hilgard &amp; Monk (2022, <i>JMF</i>, 84(2), 533–551) —
        /// negativity → dissolution d=−0.41, explicitly a small effect — see
        /// <see cref="DefaultRelationshipsEngine.ComputeCommitmentTarget"/> remarks.
        /// </summary>
        double CommitmentConflictTrajectoryWeight = 0.15,

        // ── Jealousy gap-fill (Buss et al. 1992; Dijkstra & Buunk 1998; Pollet & Saxton 2020) ──
        /// <summary>
        /// Scale applied to rival-directed hostility (Like/Respect penalty) relative to computed
        /// jealousy intensity. No literature-specified value exists for this propagation weight
        /// (architectural design decision) — keep conservative (default 0.5×) relative to the
        /// partner-directed TransgressionResidue effect until playtesting data suggests otherwise.
        /// </summary>
        double RivalHostilityScale = 0.5,
        /// <summary>Rival-attractiveness modulator weight for female observers (Pollet &amp; Saxton 2020).</summary>
        double RivalAttractivenessWeightFemale = 0.4,
        /// <summary>Rival-attractiveness modulator weight for male observers (smaller per replication).</summary>
        double RivalAttractivenessWeightMale = 0.2,
        /// <summary>
        /// IntimateAffinity threshold above which an observed SelfDisclosure exchange counts
        /// as emotional intimacy for EmotionalIntimacyAct purposes. Architectural default, not sourced.
        /// </summary>
        double EmotionalIntimacyAffinityThreshold = 50.0,
        /// <summary>
        /// SexualInterest ceiling below which the exchange is classified emotional rather than
        /// sexual — keeps EmotionalIntimacyAct and IntimateAct mutually distinguishing. Not sourced.
        /// </summary>
        double EmotionalIntimacySexualCeiling = 25.0,

        // ── Transference (Andersen & Chen 2002 — single-lab-origin caution, see SignificantOtherImprint) ──
        /// <summary>
        /// Commitment threshold above which a relationship becomes eligible for
        /// <see cref="SemanticMemory.SignificantOtherImprint"/> capture. No literature-specified value
        /// exists for this threshold (architectural decision, not a sourced constant) — set high enough
        /// that only genuinely significant bonds qualify (partners, and by extension deep long-term
        /// friendships), consistent with Andersen &amp; Chen's "significant other" framing, not casual
        /// acquaintances.
        /// </summary>
        double SignificantOtherCommitmentThreshold = 70.0,
        /// <summary>
        /// Combined-resemblance threshold above which transference activates. Architectural default —
        /// no literature-specified activation threshold exists (flagged per research doc).
        /// </summary>
        double TransferenceActivationThreshold = 0.70,
        /// <summary>
        /// Whether facial resemblance is sex-weighted per Günaydın et al. (2012) — see the extrapolation
        /// caveat on <c>TransferenceMath.SexWeightedFacialResemblance</c>. Default true per explicit
        /// inclusion instruction; set false to fall back to sex-neutral resemblance.
        /// </summary>
        bool ApplySexDifferentiatedFacialResemblance = true,
        /// <summary>
        /// Facial-resemblance multiplier for female observers. Deliberately damped relative to the
        /// source's own d=0.87 (measured for liking judgments, not belief-transference) — default 1.15,
        /// not the much larger ratio the raw effect sizes would imply.
        /// </summary>
        double FacialResemblanceWeightFemale = 1.15,
        /// <summary>
        /// Facial-resemblance multiplier for male observers. Damped toward neutral (default 0.90, not
        /// the near-zero d=0.12 the raw effect size would imply) for the same extrapolation reason.
        /// </summary>
        double FacialResemblanceWeightMale = 0.90)
    {
        /// <summary>
        /// Parameterless constructor required by DI options binding.
        /// <b>Must</b> mirror the positional defaults via named arguments — when adding a new
        /// parameter to the record, add a matching named argument here or the call fails to compile.
        /// </summary>
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
            RejectionAnxietyAmplifier: 0.6,
            DecayMultiplierTrust: 0.06,
            DecayMultiplierRespect: 0.04,
            DecayMultiplierCloseness: 0.35,
            DecayMultiplierLike: 0.28,
            DecayMultiplierComfort: 0.80,
            DecayMultiplierRomanticInterest: 1.00,
            DecayMultiplierSexualInterest: 1.50,
            DecayMultiplierFamiliarity: 0.08,
            DunbarTier1Threshold: 70.0,
            DunbarTier2Threshold: 40.0,
            DunbarTier1Capacity: 5,
            DunbarTier2Capacity: 15,
            AttentionBudgetPressurePerExcessTier1: 0.15,
            AttentionBudgetPressurePerExcessTier2: 0.05,
            DecayMultiplierDominance: 0.08,
            DecayMultiplierPrestige: 0.08,
            PrestigeGainPerPositiveAct: 2.0,
            DominanceGainPerNegativeAct: 3.0,
            PrestigeGainPerSelfDisclosure: 1.0,
            DominanceGainPerContempt: 10.0,
            PrestigeReachOutBonusPerPoint: 0.06,
            DominanceAvoidancePenaltyPerPoint: 0.08,
            ComparisonLevelBaseline: 45.0,
            CommitmentInvestmentWeight: 0.6,
            CommitmentAlternativeWeight: 0.5,
            CommitmentDriftPerDay: 0.08,
            InvestmentGrowthPerDay: 0.02,
            RomanticEdgeIntimacyThreshold: 30.0,
            CommitmentDecayResistance: 0.6,
            DissolutionCommitmentThreshold: 15.0)
        { }
    }
}
