// BereavementConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Bereavement
{
    /// <summary>
    /// Tuning parameters for the bereavement subsystem. Bound from <c>Characters:Bereavement</c>.
    /// </summary>
    /// <remarks>
    /// The scientific core (trajectory prevalences, DPM oscillation, widowhood effect, ritual relief)
    /// is well-supported; the attachment modifiers are deliberately small and flagged as
    /// <i>partially non-replicated</i> (2023 <i>Death Studies</i> null). PGD base rates are parameterised
    /// per loss-type (DSM-5-TR 3.3–4.7 % ↔ non-violent 9.8 % ↔ violent 49 %).
    /// <list type="bullet">
    ///   <item>Lundorff et al. (2020) — trajectory prevalences.</item>
    ///   <item>Stroebe &amp; Schut (2010); Maciejewski et al. (2007) — DPM oscillation, no stages.</item>
    ///   <item>Lundorff (2017) <i>JAD</i> 212:138; Djelantik (2020) <i>JAD</i> 265:146 — PGD base rates.</item>
    ///   <item>Moon (2011); Shor (2012); Parkes (1969) — widowhood mortality hazard.</item>
    ///   <item>Norton &amp; Gino (2014) — ritual reduces grief via regained control.</item>
    /// </list>
    /// </remarks>
    public sealed record BereavementConfig(
        // ── Trajectory prevalences (Lundorff 2020) ─────────────────────────────
        double ResilientWeight = 0.644,
        double ModerateStableWeight = 0.204,
        double RecoveryWeight = 0.084,
        double ProlongedWeight = 0.068,

        // ── Onset grief intensity ──────────────────────────────────────────────
        /// <summary>Grief intensity contributed per point of lost-bond strength. Default 0.7.</summary>
        double OnsetIntensityFromBond = 0.7,
        /// <summary>Extra onset intensity when the deceased was a partner. Default 18.</summary>
        double PartnerKinIntensityBonus = 18.0,
        /// <summary>Extra onset intensity when the deceased was a child. Default 22 (worst-case loss).</summary>
        double ChildKinIntensityBonus = 22.0,
        /// <summary>Extra onset intensity for other close kin (parent/sibling/grandparent). Default 8.</summary>
        double CloseKinIntensityBonus = 8.0,

        // ── Onset affect spike (Psychology), scaled by intensity/100 ────────────
        double OnsetValenceDrop = 0.9,
        double OnsetMoodBaselineDrop = 28.0,
        double OnsetStressSpike = 30.0,

        // ── DPM oscillation ("waves of grief") ─────────────────────────────────
        /// <summary>Period of the loss/restoration oscillation, in days. Default 3.5.</summary>
        double DpmPeriodDays = 3.5,
        /// <summary>
        /// Rate at which the loss-orientation envelope declines per day (restoration share grows).
        /// Default 0.012 → envelope reaches zero after ~80 days for a resilient mourner.
        /// </summary>
        double RestorationGrowthPerDay = 0.012,
        /// <summary>LoRo value above which a loss is actively grieving — crossing it upward fires a pang. Default 0.5.</summary>
        double LoPhaseThreshold = 0.5,

        // ── Grief pang affect deltas (per pang), scaled by intensity/100 ────────
        double GriefPangValenceDrop = 0.5,
        double GriefPangMoodDrop = 6.0,
        double GriefPangStress = 4.0,

        // ── Trajectory grief-intensity decay (points per day) ──────────────────
        double ResilientDecayPerDay = 1.2,
        double ModerateStableDecayPerDay = 0.45,
        double RecoveryDecayPerDay = 0.30,
        double ProlongedDecayPerDay = 0.06,

        // ── Prolonged-grief (PGD) base rates per loss-type ─────────────────────
        double PgdDurationGateMonths = 6.0,
        double PgdBaseRateNonViolent = 0.098,
        double PgdBaseRateViolent = 0.49,

        // ── Attachment modifiers (small; partially non-replicated, 2023 null) ──
        /// <summary>Added prolonged-trajectory weight per unit attachment Anxiety. Default 0.10.</summary>
        double AnxietyProlongedWeight = 0.10,
        /// <summary>
        /// Fraction by which attachment Avoidance suppresses <i>expressed</i> grief (pang deltas) —
        /// it does NOT lower the underlying loss. Default 0.5 = up to 50 % suppression at full avoidance.
        /// </summary>
        double AvoidanceExpressionSuppression = 0.5,

        // ── Funeral / ritual relief ────────────────────────────────────────────
        /// <summary>Flat grief-intensity reduction from attending a funeral. Default 8.</summary>
        double FuneralGriefRelief = 8.0,
        /// <summary>Additional proportional grief reduction (fraction of current intensity). Default 0.15.</summary>
        double FuneralIntensityReliefFraction = 0.15,

        /// <summary>
        /// Grief-intensity relief from visiting the deceased's grave — a small closure/consolidation
        /// step that also internalises the continuing bond (adaptive). Default 3.0.
        /// </summary>
        double GraveVisitGriefRelief = 3.0,

        // ── Widowhood mortality hazard (Physiology) ────────────────────────────
        /// <summary>Mortality multiplier in the first window after partner loss. Default 1.41.</summary>
        double WidowhoodHazardFirst = 1.41,
        /// <summary>Mortality multiplier in the tail window after partner loss. Default 1.18.</summary>
        double WidowhoodHazardTail = 1.18,
        /// <summary>Extra multiplicative factor applied to male survivors (men fare worse). Default 1.15.</summary>
        double WidowhoodMaleFactor = 1.15,
        /// <summary>Length of the acute high-hazard window in days (~6 months). Default 180.</summary>
        double WidowhoodFirstWindowDays = 180.0,
        /// <summary>End of the elevated-hazard tail in days (~2 years). Default 720.</summary>
        double WidowhoodTailWindowDays = 720.0,

        // ── Resolution ─────────────────────────────────────────────────────────
        /// <summary>Grief intensity at/below which a loss record is considered resolved and dropped. Default 1.0.</summary>
        double GriefResolvedThreshold = 1.0)
    {
        /// <summary>Parameterless constructor required by DI options binding — all fields use defaults.</summary>
        public BereavementConfig() : this(ResilientWeight: 0.644)
        {
        }
    }
}
