// SocialComparisonMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Social
{
    using System;

    /// <summary>
    /// The deltas produced by a single social comparison, before any engine applies them.
    /// </summary>
    public readonly record struct SocialComparisonResult(
        ComparisonDirection Direction,
        ComparisonReaction Reaction,
        ComparisonEnvy Envy,
        double SelfEsteemDelta,
        double MoodValenceDelta,
        double MoodBaselineDelta,
        double AchievementMotivationDelta,
        double TargetHostilityDelta)
    {
        /// <summary>An inert result — no salient comparison occurred.</summary>
        public static SocialComparisonResult None { get; } = new(
            ComparisonDirection.None, ComparisonReaction.Contrast, ComparisonEnvy.None,
            0.0, 0.0, 0.0, 0.0, 0.0);

        /// <summary>True when every delta is negligible (nothing worth emitting).</summary>
        public bool IsNegligible
            => Direction == ComparisonDirection.None
               || (Math.Abs(SelfEsteemDelta) < 1e-4
                   && Math.Abs(MoodValenceDelta) < 1e-4
                   && Math.Abs(MoodBaselineDelta) < 1e-4
                   && Math.Abs(AchievementMotivationDelta) < 1e-4
                   && Math.Abs(TargetHostilityDelta) < 1e-4);
    }

    /// <summary>
    /// Pure, stateless social comparison math: comparison orientation (INCOM), and the
    /// contrast/assimilation reaction with its envy bifurcation and downward mood-repair.
    /// </summary>
    /// <remarks>
    /// Default reaction is <b>contrast</b> (Gerber, Wheeler &amp; Suls 2018); assimilation requires
    /// both attainability (small gap) and identification (high closeness). Upward contrast can spawn
    /// malicious envy under low agreeableness; attainable upward comparison spawns benign envy →
    /// achievement motivation. Downward comparison repairs mood, more so for low-self-esteem comparers
    /// and similar targets (Wills 1981).
    /// </remarks>
    public static class SocialComparisonMath
    {
        /// <summary>
        /// Dispositional tendency to compare [0..1] (INCOM proxy). Rises with Neuroticism and with
        /// lower self-esteem (Gibbons &amp; Buunk 1999). Scales the magnitude of every reaction, so a
        /// low-orientation character barely registers comparisons.
        /// </summary>
        public static double ComparisonOrientation(double neuroticism, double selfEsteem, SocialComparisonConfig c)
            => Math.Clamp(
                c.OrientationBase
                + (neuroticism - 0.5) * c.OrientationNeuroticismWeight
                + (0.5 - selfEsteem) * c.OrientationLowEsteemWeight,
                0.0, 1.0);

        /// <summary>
        /// Evaluates one comparison of the self (<paramref name="selfStanding"/>) against a peer
        /// (<paramref name="targetStanding"/>), both on a [0..100] competence/status scale.
        /// </summary>
        /// <param name="selfStanding">The comparer's own perceived standing [0..100].</param>
        /// <param name="targetStanding">The target's perceived standing [0..100].</param>
        /// <param name="closeness">Edge closeness to the target [0..100] — identification + similarity proxy.</param>
        /// <param name="neuroticism">Comparer Neuroticism [0..1].</param>
        /// <param name="agreeableness">Comparer Agreeableness [0..1].</param>
        /// <param name="selfEsteem">Comparer global self-esteem [0..1].</param>
        /// <param name="c">Tuning config.</param>
        public static SocialComparisonResult Evaluate(
            double selfStanding,
            double targetStanding,
            double closeness,
            double neuroticism,
            double agreeableness,
            double selfEsteem,
            SocialComparisonConfig c)
        {
            var gap = targetStanding - selfStanding;
            var absGap = Math.Abs(gap);
            if (absGap < c.MinSalientGap)
                return SocialComparisonResult.None;

            var intensity = ComparisonOrientation(neuroticism, selfEsteem, c);
            var gapNorm = Math.Clamp(absGap / Math.Max(1e-6, c.GapNormDivisor), 0.0, 1.0);
            var attainable = absGap <= c.AttainabilityGap;
            var identified = closeness >= c.IdentificationCloseness;
            var assimilate = attainable && identified;

            if (gap > 0)
            {
                // ── Upward comparison ──────────────────────────────────────────
                if (assimilate)
                {
                    // Benign envy / inspiration: an attainable, identified-with model lifts aspiration.
                    return new SocialComparisonResult(
                        ComparisonDirection.Upward, ComparisonReaction.Assimilation, ComparisonEnvy.Benign,
                        SelfEsteemDelta: c.AssimilationEsteemLift * intensity,
                        MoodValenceDelta: c.AssimilationMoodLift * intensity,
                        MoodBaselineDelta: 0.0,
                        AchievementMotivationDelta: c.BenignEnvyAchievementWeight * intensity,
                        TargetHostilityDelta: 0.0);
                }

                // Contrast (the default): self-evaluation drops away from the superior standard.
                var maliciousScore = c.MaliciousEnvyDispositionWeight * (1.0 - agreeableness) * intensity * gapNorm;
                var malicious = maliciousScore >= c.MaliciousEnvyThreshold;
                return new SocialComparisonResult(
                    ComparisonDirection.Upward, ComparisonReaction.Contrast,
                    malicious ? ComparisonEnvy.Malicious : ComparisonEnvy.None,
                    SelfEsteemDelta: -c.ContrastSelfEvalWeight * intensity * gapNorm,
                    MoodValenceDelta: -c.ContrastMoodDrop * intensity * gapNorm,
                    MoodBaselineDelta: -c.ContrastMoodBaselineDrop * intensity * gapNorm,
                    AchievementMotivationDelta: 0.0,
                    TargetHostilityDelta: malicious ? c.MaliciousEnvyHostilityWeight * maliciousScore : 0.0);
            }

            // ── Downward comparison ────────────────────────────────────────────
            if (assimilate)
            {
                // Identified-with inferior → fear of decline (mild self-threat), not repair.
                return new SocialComparisonResult(
                    ComparisonDirection.Downward, ComparisonReaction.Assimilation, ComparisonEnvy.None,
                    SelfEsteemDelta: -c.DownwardAssimilationEsteemDrop * intensity,
                    MoodValenceDelta: -c.DownwardAssimilationMoodDrop * intensity,
                    MoodBaselineDelta: 0.0,
                    AchievementMotivationDelta: 0.0,
                    TargetHostilityDelta: 0.0);
            }

            // Self-enhancement / mood repair — stronger for low self-esteem and similar targets.
            var lowEsteemBoost = 1.0 + (0.5 - selfEsteem) * c.DownwardLowEsteemAmplifier;
            var similarity = Math.Clamp(closeness / 100.0, 0.0, 1.0);
            var similarityMult = c.DownwardSimilarityFloor + (1.0 - c.DownwardSimilarityFloor) * similarity;
            var repair = intensity * gapNorm * Math.Max(0.0, lowEsteemBoost) * similarityMult;
            return new SocialComparisonResult(
                ComparisonDirection.Downward, ComparisonReaction.Contrast, ComparisonEnvy.None,
                SelfEsteemDelta: c.DownwardSelfEvalWeight * repair,
                MoodValenceDelta: c.DownwardMoodLift * repair,
                MoodBaselineDelta: 0.0,
                AchievementMotivationDelta: 0.0,
                TargetHostilityDelta: 0.0);
        }
    }
}
