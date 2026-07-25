// PerceivedActAppraiser.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology.Appraisal
{
    using System;

    /// <summary>
    /// Bridges a listener's <see cref="PerceivedMeaning"/> into the existing Scherer-CPM pipeline: it
    /// produces an <see cref="AppraisalOutcome"/> (consumed by <see cref="AppraisalEmotionMap"/>), it is
    /// not a second appraisal engine. It fires on interpretive <i>divergence</i> — a hostile-attribution
    /// directness shift, or irony taken literally / decoded — and, when the opt-in connotation layer is
    /// enabled, on the lemma's connotative colouring (<see cref="PerceivedMeaning.ConnotationDelta"/>;
    /// zero when the layer is off, so the divergence-only behaviour is preserved byte-identically).
    /// A plainly-read act with a neutral lemma adds no affect here — the existing interaction paths
    /// keep owning the base emotion.
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

            // Opt-in connotation contribution of the word choice itself ("chválit" warms, "odmítat"
            // stings) — additive and independent of divergence; exactly 0 when the layer is disabled.
            valence += meaning.ConnotationDelta;

            // Small epsilon, not a hard 0.05 cliff: connotation then reads as GRADED (a mildly warm
            // word yields a mildly warm reaction) rather than on/off. Flag-off divergence always has
            // |valence| ≥ 0.10 (hostile shift / irony), so this change is byte-identical when off — it
            // only lets small connotation through when the layer is on. The emotion still scales
            // continuously with Relevance downstream (AppraisalEmotionMap), so tiny valence ⇒ tiny effect.
            if (Math.Abs(valence) < 0.01)
            {
                return null;   // effectively no affect (numerical floor) → base paths own it
            }

            valence = Math.Clamp(valence, -1.0, 1.0);

            var familiarityNorm = Math.Clamp(familiarity / 100.0, 0.0, 1.0);
            var relevance = Math.Clamp(Math.Abs(valence) * (0.5 + 0.5 * familiarityNorm), 0.0, 1.0);

            // A word that carries connotation IS relevant — lift it just over AppraisalEmotionMap's
            // relevance floor so mild warmth/coolness still registers (graded via IntrinsicPleasantness)
            // instead of being zeroed by the floor. Only applies when the layer is on (ConnotationDelta ≠ 0),
            // so the divergence-only, flag-off path keeps its original relevance byte-for-byte.
            if (meaning.ConnotationDelta != 0.0)
            {
                relevance = Math.Max(relevance, 0.06);
            }

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
