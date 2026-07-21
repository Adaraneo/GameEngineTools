// PerceivedActAppraiser.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology.Appraisal
{
    using System;

    /// <summary>
    /// Bridges a listener's <see cref="PerceivedMeaning"/> into the existing Scherer-CPM pipeline: it
    /// produces an <see cref="AppraisalOutcome"/> (consumed by <see cref="AppraisalEmotionMap"/>), it is
    /// not a second appraisal engine. It deliberately fires only on interpretive <i>divergence</i> —
    /// a hostile-attribution directness shift, or irony taken literally / decoded — so a plainly-read
    /// act adds no affect here and the existing interaction paths keep owning the base emotion.
    /// </summary>
    public static class PerceivedActAppraiser
    {
        /// <summary>
        /// Maps <paramref name="meaning"/> to a CPM appraisal, or <c>null</c> when the reading carries no
        /// meaningful divergence (the caller then leaves emotion untouched).
        /// </summary>
        /// <param name="meaning">The listener's reading of the incoming act.</param>
        /// <param name="familiarity">Listener→speaker familiarity [0..100] (scales relevance).</param>
        /// <param name="current">Current psychology state (for coping potential).</param>
        public static AppraisalOutcome? ToAppraisal(PerceivedMeaning meaning, double familiarity, PsychologyState current)
        {
            ArgumentNullException.ThrowIfNull(meaning);
            var source = meaning.Source;

            var directnessShift = DirectnessRank(meaning.PerceivedDirectness) - DirectnessRank(source.Directness);
            var ironyMisread = source.ForceShift is not null
                && meaning.PerceivedPolarity == source.ForceShift.SurfacePolarity
                && meaning.PerceivedPolarity != source.Polarity;
            var ironyDecoded = source.ForceShift is not null && meaning.PerceivedPolarity == source.Polarity;

            var valence = 0.0;
            if (directnessShift > 0)
            {
                valence -= 0.20 * directnessShift;   // felt harsher than it was sent
            }

            if (ironyMisread)
            {
                valence -= 0.15;                      // took an ironic act at face value
            }

            if (ironyDecoded)
            {
                valence += 0.10;                      // shared understanding of the irony
            }

            if (meaning.PerceivedPolarity == Polarity.Negative)
            {
                valence -= 0.10;
            }

            if (Math.Abs(valence) < 0.05)
            {
                return null;   // no meaningful divergence → base interaction paths own the affect
            }

            var familiarityNorm = Math.Clamp(familiarity / 100.0, 0.0, 1.0);
            var relevance = Math.Clamp(Math.Abs(valence) * (0.5 + 0.5 * familiarityNorm), 0.0, 1.0);
            var coping = Math.Clamp(0.35 + 0.5 * current.Dominance - 0.3 * (current.Stress / 100.0), 0.0, 1.0);

            return new AppraisalOutcome(
                Relevance: relevance,
                Novelty: 0.3,
                IntrinsicPleasantness: valence,
                GoalConduciveness: valence * 0.5,
                Agency: AppraisalAgency.Other,
                Certainty: meaning.Confidence,
                CopingPotential: coping,
                NormCompatibility: 0.0);
        }

        private static int DirectnessRank(Directness directness) => directness switch
        {
            Directness.Indirect => 0,
            Directness.Neutral => 1,
            _ => 2,
        };
    }
}
