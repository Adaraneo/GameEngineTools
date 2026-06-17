// SocialComparisonConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Social
{
    /// <summary>
    /// Tuning parameters for the social comparison engine. All values bind from
    /// <c>Characters:SocialComparison</c> in appsettings.
    /// </summary>
    /// <remarks>
    /// Calibrated against the social comparison literature:
    /// <list type="bullet">
    ///   <item>Gerber, Wheeler &amp; Suls (2018) <i>Psych. Bulletin</i> meta-analysis: <b>contrast is the
    ///         default</b> response (ability g ≈ −0.65…−0.75, affect g ≈ −0.65…−0.83); assimilation only
    ///         under attainability/identification/priming; upward targets are preferred even under threat.</item>
    ///   <item>Gibbons &amp; Buunk (1999) INCOM: comparison orientation rises with Neuroticism
    ///         (r ≈ .33–.37) and lower self-esteem.</item>
    ///   <item>Wills (1981) downward comparison: mood repair is stronger for low-self-esteem comparers
    ///         and mediated by perceived similarity to the target.</item>
    ///   <item>van de Ven et al.; Meier &amp; Schäfer (2018): benign envy → self-improvement, malicious
    ///         envy → hostility, gated by attainability and disposition.</item>
    /// </list>
    /// </remarks>
    public sealed record SocialComparisonConfig(
        // ── Cadence ────────────────────────────────────────────────────────────
        /// <summary>Minimum game days between comparisons (reflective cadence, not per-tick). Default 0.5.</summary>
        double ComparisonCooldownDays = 0.5,

        /// <summary>Minimum edge Familiarity for a peer to be eligible as a comparison standard. Default 15.</summary>
        double MinFamiliarity = 15.0,

        // ── INCOM comparison orientation (Gibbons & Buunk 1999) ────────────────
        /// <summary>Baseline comparison orientation [0..1]. Default 0.40.</summary>
        double OrientationBase = 0.40,
        /// <summary>Orientation gain per unit Neuroticism above 0.5 (r≈.33–.37). Default 0.70.</summary>
        double OrientationNeuroticismWeight = 0.70,
        /// <summary>Orientation gain per unit self-esteem below 0.5. Default 0.50.</summary>
        double OrientationLowEsteemWeight = 0.50,

        // ── Standing gap → direction / attainability ───────────────────────────
        /// <summary>Standing points (0..100) below which a comparison is not salient. Default 6.</summary>
        double MinSalientGap = 6.0,
        /// <summary>Divisor normalising the standing gap to [0..1] for magnitude scaling. Default 50.</summary>
        double GapNormDivisor = 50.0,
        /// <summary>Gap (0..100) at or below which an upward target feels attainable. Default 25.</summary>
        double AttainabilityGap = 25.0,
        /// <summary>Edge Closeness (0..100) at or above which the comparer identifies with the target. Default 50.</summary>
        double IdentificationCloseness = 50.0,

        // ── Upward contrast (the default upward reaction; Gerber 2018) ─────────
        /// <summary>Max self-esteem drop from upward contrast (SelfEsteem is [0..1]). Default 0.08.</summary>
        double ContrastSelfEvalWeight = 0.08,
        /// <summary>Max PAD valence drop from upward contrast. Default 0.10.</summary>
        double ContrastMoodDrop = 0.10,
        /// <summary>Max mood-baseline drop (0..100) from upward contrast. Default 3.0.</summary>
        double ContrastMoodBaselineDrop = 3.0,

        // ── Benign envy / assimilation (upward, attainable + identified) ───────
        /// <summary>Max NeedAchievement gain (0..100) from benign envy / inspiration. Default 8.0.</summary>
        double BenignEnvyAchievementWeight = 8.0,
        /// <summary>Max PAD valence lift from assimilation (hope/inspiration). Default 0.05.</summary>
        double AssimilationMoodLift = 0.05,
        /// <summary>Max self-esteem lift from assimilation toward an attainable model. Default 0.02.</summary>
        double AssimilationEsteemLift = 0.02,

        // ── Malicious envy (upward contrast, unattainable, low agreeableness) ──
        /// <summary>Weight mapping (1−Agreeableness) into a malicious-envy score. Default 0.9.</summary>
        double MaliciousEnvyDispositionWeight = 0.9,
        /// <summary>Malicious-envy score threshold above which hostility is emitted. Default 0.30.</summary>
        double MaliciousEnvyThreshold = 0.30,
        /// <summary>Hostility magnitude per unit malicious-envy score (consumed by Relationships). Default 6.0.</summary>
        double MaliciousEnvyHostilityWeight = 6.0,

        // ── Downward comparison (self-enhancement / mood repair; Wills 1981) ───
        /// <summary>Max self-esteem lift from downward contrast. Default 0.05.</summary>
        double DownwardSelfEvalWeight = 0.05,
        /// <summary>Max PAD valence lift from downward contrast (mood repair). Default 0.07.</summary>
        double DownwardMoodLift = 0.07,
        /// <summary>Amplifier on downward mood repair per unit self-esteem below 0.5 (low-SE benefit more). Default 1.0.</summary>
        double DownwardLowEsteemAmplifier = 1.0,
        /// <summary>Floor on the similarity multiplier so dissimilar downward targets still repair a little. Default 0.4.</summary>
        double DownwardSimilarityFloor = 0.4,
        /// <summary>Self-esteem drop when a below-self target is identified-with (fear of decline). Default 0.02.</summary>
        double DownwardAssimilationEsteemDrop = 0.02,
        /// <summary>PAD valence drop when a below-self target is identified-with. Default 0.03.</summary>
        double DownwardAssimilationMoodDrop = 0.03,

        // ── Target standing blend ──────────────────────────────────────────────
        /// <summary>Weight of edge Respect in the target's perceived standing (rest from PerceivedPrestige). Default 0.6.</summary>
        double StandingRespectWeight = 0.6,

        // ── Dark-core amplification of malicious envy ──────────────────────────
        /// <summary>
        /// Multiplier applied to the malicious-envy <see cref="SocialComparisonResult.TargetHostilityDelta"/>
        /// per unit DarkCore axis [0..1]: <c>hostility *= (1 + DarkCore × this)</c>.
        /// Default 0.5 — a fully dark-core character generates 1.5× the baseline malicious hostility.
        /// Sources: van de Ven (2009); Lange &amp; Crusius (2015) — dark-core amplifies the antagonistic
        /// (malicious) envy branch. Default 0.0 ⇒ no change ⇒ existing tests stay green.
        /// </summary>
        double DarkCoreMaliciousAmplification = 0.5)
    {
        /// <summary>Parameterless constructor required by DI options binding — all fields use defaults.</summary>
        public SocialComparisonConfig() : this(ComparisonCooldownDays: 0.5)
        {
        }
    }
}
