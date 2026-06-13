// AppraisalEmotionMap.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology.Appraisal
{
    using System;

    /// <summary>
    /// Converts an <see cref="AppraisalOutcome"/> (Scherer CPM checks) into a labelled
    /// <see cref="DiscreteEmotion"/> plus a coherent PAD delta. This is the emotion <i>generator</i>:
    /// the discrete label is selected from the appraisal structure (notably <see cref="AppraisalAgency"/>),
    /// not from PAD alone — so two events with identical physiological PAD but different appraisal
    /// produce different emotions (e.g. goal-blockage→Anger vs. threat→Fear).
    /// </summary>
    /// <remarks>
    /// Empirically-weighted appraisal→emotion links are used as <i>relative weights</i> (not
    /// probabilities). Strongest links: pleasantness→affection .57, goal-conduciveness→joy .56,
    /// threat→fear .47, loss→sadness .42. Source: Yeo &amp; Ong (2024, <i>Psychological Bulletin</i>
    /// 150(12)); Roseman (1996); Scherer (2001).
    /// </remarks>
    public static class AppraisalEmotionMap
    {
        /// <summary>The PAD delta and emotion produced by an appraisal.</summary>
        /// <param name="Emotion">The selected discrete emotion.</param>
        /// <param name="DeltaValence">Change to apply to <c>PsychologyState.Valence</c>.</param>
        /// <param name="DeltaArousal">Change to apply to <c>PsychologyState.Arousal</c>.</param>
        /// <param name="DeltaDominance">Change to apply to <c>PsychologyState.Dominance</c>.</param>
        public readonly record struct AppraisalResult(
            DiscreteEmotion Emotion,
            double DeltaValence,
            double DeltaArousal,
            double DeltaDominance);

        /// <summary>
        /// Maps an appraisal outcome to an emotion and a PAD delta, using config-tunable per-dimension
        /// weights.
        /// </summary>
        /// <param name="o">The appraisal outcome.</param>
        /// <param name="cfg">Psychology configuration (per-dimension appraisal weights).</param>
        /// <returns>The selected emotion and the PAD delta to apply.</returns>
        public static AppraisalResult Map(AppraisalOutcome o, PsychologyConfig cfg)
        {
            ArgumentNullException.ThrowIfNull(o);
            ArgumentNullException.ThrowIfNull(cfg);

            if (!o.IsRelevant())
                return new AppraisalResult(DiscreteEmotion.Neutral, 0, 0, 0);

            // ── Derived appraisal quantities ──────────────────────────────────────
            var obstruction = Math.Max(0.0, -o.GoalConduciveness);          // [0..1] goal-blockage magnitude
            var conducive = Math.Max(0.0, o.GoalConduciveness);             // [0..1] goal-help magnitude
            var uncertainty = 1.0 - o.Certainty;                             // [0..1]
            var lowCoping = 1.0 - o.CopingPotential;                         // [0..1]
            // Threat = obstruction you cannot yet handle / are unsure about (Lazarus 1991).
            var threat = obstruction * (0.5 * lowCoping + 0.5 * uncertainty);
            // Loss = realised obstruction you are certain about and cannot reverse.
            var loss = obstruction * o.Certainty * lowCoping;
            var normViolationSelf = (o.Agency == AppraisalAgency.Self)
                ? Math.Max(0.0, -o.NormCompatibility) : 0.0;

            // ── PAD delta (config-weighted) ───────────────────────────────────────
            var scale = cfg.AppraisalPadDeltaScale;
            var dV = scale * (
                cfg.AppraisalPleasantnessValenceWeight * o.IntrinsicPleasantness +
                cfg.AppraisalGoalConducivenessValenceWeight * conducive -
                cfg.AppraisalLossValenceWeight * loss) * o.Relevance;

            var dA = scale * (
                cfg.AppraisalNoveltyArousalWeight * o.Novelty +
                cfg.AppraisalThreatArousalWeight * threat +
                0.2 * Math.Abs(o.IntrinsicPleasantness)) * o.Relevance;

            // Dominance follows control: self-agency and coping raise it; circumstance/low coping
            // lowers it. Other-accountability for obstruction is approach-motivated (anger keeps
            // dominance up), so it does not depress dominance the way circumstance does.
            var agencyControl = o.Agency switch
            {
                AppraisalAgency.Self => 1.0,
                AppraisalAgency.Other => 0.3,
                AppraisalAgency.Circumstance => -0.6,
                _ => 0.0
            };
            var dD = scale * cfg.AppraisalAgencyDominanceWeight *
                     ((o.CopingPotential - 0.5) * 2.0 + agencyControl) * 0.5 * o.Relevance;

            dV = Math.Clamp(dV, -1.0, 1.0);
            dA = Math.Clamp(dA, -1.0, 1.0);
            dD = Math.Clamp(dD, -1.0, 1.0);

            var emotion = SelectEmotion(o, obstruction, conducive, threat, loss, normViolationSelf);
            return new AppraisalResult(emotion, dV, dA, dD);
        }

        /// <summary>
        /// Selects the discrete emotion by scoring each candidate from the appraisal structure and
        /// taking the argmax. Link weights are the meta-analytic relative weights (Yeo &amp; Ong 2024);
        /// agency is the decisive discriminator among negative emotions (Roseman 1996).
        /// </summary>
        private static DiscreteEmotion SelectEmotion(
            AppraisalOutcome o, double obstruction, double conducive, double threat, double loss, double normViolationSelf)
        {
            Span<(DiscreteEmotion Emotion, double Score)> scores = stackalloc (DiscreteEmotion, double)[]
            {
                // Positive emotions
                (DiscreteEmotion.Joy,        0.56 * conducive + 0.30 * Math.Max(0, o.IntrinsicPleasantness)),
                (DiscreteEmotion.Pride,      0.40 * conducive * (o.Agency == AppraisalAgency.Self ? 1.0 : 0.0)
                                             + 0.30 * o.CopingPotential * conducive),
                (DiscreteEmotion.Tenderness, 0.57 * Math.Max(0, o.IntrinsicPleasantness) * (1.0 - o.Novelty)),

                // Negative emotions — agency discriminates
                (DiscreteEmotion.Anger,      0.45 * obstruction * (o.Agency == AppraisalAgency.Other ? 1.0 : 0.0)
                                             + 0.20 * obstruction * o.CopingPotential),
                (DiscreteEmotion.Fear,       0.47 * threat),
                (DiscreteEmotion.Sadness,    0.42 * loss * (o.Agency != AppraisalAgency.Other ? 1.0 : 0.3)),
                (DiscreteEmotion.Guilt,      0.40 * normViolationSelf + 0.25 * obstruction * (o.Agency == AppraisalAgency.Self ? 1.0 : 0.0)),
                (DiscreteEmotion.Shame,      0.45 * normViolationSelf * (1.0 - o.CopingPotential)),
                (DiscreteEmotion.Disgust,    0.30 * Math.Max(0, -o.IntrinsicPleasantness) * (obstruction < 0.3 ? 1.0 : 0.3)),

                // Novelty without clear valence
                (DiscreteEmotion.Surprise,   0.40 * o.Novelty * (1.0 - Math.Abs(o.GoalConduciveness))),
            };

            var best = DiscreteEmotion.Neutral;
            var bestScore = 0.08; // floor: below this, nothing is salient enough to label
            foreach (var (emotion, score) in scores)
            {
                if (score > bestScore)
                {
                    bestScore = score;
                    best = emotion;
                }
            }

            return best;
        }
    }
}
