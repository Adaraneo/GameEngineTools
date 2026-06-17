// DarkCoreProfile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using System;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// A character's position on the general dark-core factor (D-factor) of personality —
    /// the common latent variance underlying Narcissism, Machiavellianism, Psychopathy,
    /// and related dark traits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The D-factor is defined as the general tendency to maximise one's own utility at the
    /// expense of others, accompanied by a set of justifying beliefs that make such behaviour
    /// feel acceptable to the actor (Moshagen, Hilbig &amp; Zettler 2018, <i>Psychological Review</i>
    /// 125(5)). It has a latent correlation of r ≈ −0.95 with HEXACO Honesty-Humility
    /// (Hodson et al. 2018, <i>JRP</i> 73); in the Big Five framework the best single-trait
    /// proxy is low Agreeableness (Howard &amp; Van Zandt 2020).
    /// </para>
    /// <para>
    /// <b>Note on HEXACO:</b> if HEXACO Honesty-Humility is ever added to the personality model,
    /// prefer <c>DarkCore = z(−HonestyHumility)</c> over the current Big Five approximation.
    /// </para>
    /// <para>
    /// Sources: Moshagen, Hilbig &amp; Zettler (2018); Hodson et al. (2018); Howard &amp; Van Zandt (2020);
    /// Muris et al. (2017, <i>Perspectives on Psychological Science</i> 12(2)).
    /// </para>
    /// </remarks>
    /// <param name="DarkCore">
    /// General dark-core factor [0..1]. Higher values indicate a stronger disposition to maximise
    /// self-interest at others' expense. Population distribution is right-skewed — most characters
    /// cluster near zero with a minority reaching high values.
    /// </param>
    /// <param name="JustifyingBeliefs">
    /// Strength of the belief system that rationalises self-serving or exploitative behaviour [0..1].
    /// A semi-independent component that keeps D from being merely reversed Agreeableness — it
    /// accounts for the ≈50% of D variance that Big Five Agreeableness alone does not capture
    /// (Moshagen et al. 2018).
    /// </param>
    public sealed record DarkCoreProfile(double DarkCore, double JustifyingBeliefs);

    /// <summary>
    /// Generates a <see cref="DarkCoreProfile"/> from Big Five personality traits and biological
    /// sex using empirically motivated regression equations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generation mirrors <see cref="ComparisonOrientationGenerator"/>:
    /// <c>0.5 + Σ ρ·(trait − 0.5)</c> + optional Gaussian residual (Box-Muller, σ≈0.10) + clamp.
    /// Two overloads are provided: one accepting <see cref="Random"/> (null = deterministic) and
    /// one accepting the project's <see cref="IRandomSource"/>.
    /// </para>
    /// <para>
    /// <b>JustifyingBeliefs</b> is generated as a low-weight semi-independent term driven primarily
    /// by Neuroticism (rumination amplifies self-serving narratives) and low Conscientiousness
    /// (reduced guilt/self-discipline). This represents the portion of D variance not captured by
    /// Agreeableness and prevents DarkCore from being trivially reducible to reversed Agreeableness
    /// (Moshagen et al. 2018 report only ≈50% shared variance with low A).
    /// </para>
    /// <para>
    /// <b>DarkCore formula:</b> <c>0.5 − 0.50·(A − 0.5) + JustifyingBeliefsOffset + residual</c>,
    /// where <c>JustifyingBeliefsOffset = (JB − 0.5) × 0.25</c>. The −0.50 Agreeableness weight
    /// reflects the strong but not complete Big Five proxy (Howard &amp; Van Zandt 2020).
    /// </para>
    /// <para>
    /// <b>Male shift:</b> +0.06 added before clamping for <see cref="SexBiology.Male"/> characters.
    /// Source: Muris et al. (2017) meta-analysis showing men score higher on all dark-triad facets,
    /// most prominently on the impulsive/psychopathy facet.
    /// </para>
    /// <para>
    /// <b>Right-skew transform:</b> after computing the raw score in [0,1], a power transform
    /// <c>x → x^1.6</c> is applied. Because exponentiation with a power &gt;1 compresses high values
    /// toward the origin, the resulting distribution is right-skewed — most characters cluster near
    /// zero while a minority reaches high values. The monotonic <c>x → x^1.6</c> transform on
    /// [0,1]→[0,1] preserves the "low Agreeableness → higher DarkCore" ordering.
    /// </para>
    /// </remarks>
    public static class DarkCoreGenerator
    {
        #region Regression coefficients

        // DarkCore: Agreeableness is the dominant Big Five proxy (Howard & Van Zandt 2020).
        // The weight of −0.50 reflects r ≈ −0.50 between low-A and D in Big Five frameworks.
        // Source: Howard & Van Zandt 2020
        private const double DarkCoreAgreeablenessWeight = -0.50; // Source: Howard & Van Zandt 2020

        // JustifyingBeliefs: semi-independent component; small loads on Neuroticism and low-C.
        // Represents the self-serving narrative system (Moshagen et al. 2018).
        // Source: Moshagen, Hilbig & Zettler 2018
        private const double JustifyingBeliefsNeuroticismWeight = 0.18;    // Source: Moshagen et al. 2018
        private const double JustifyingBeliefsConscientiousnessWeight = -0.14; // Source: Moshagen et al. 2018

        // How much JustifyingBeliefs offsets DarkCore (keeps the two correlated but not identical).
        // A weight of 0.25 means ±0.5 range in JB translates to ±0.125 shift in DarkCore.
        private const double JustifyingBeliefsOffsetWeight = 0.25;

        // Male sex shift — largest for the psychopathy/impulsivity facet, applied as constant.
        // Source: Muris et al. 2017, Perspectives on Psychological Science 12(2).
        private const double MaleShift = 0.06; // Source: Muris et al. 2017

        // Right-skew exponent: x^1.6 on [0,1] compresses high scores, producing a right-skewed
        // distribution where most NPCs are low-D and a minority reaches high values.
        // The transform is monotonic on [0,1] and preserves low-A → higher D ordering.
        private const double SkewExponent = 1.6;

        #endregion

        private const double NoiseSigma = 0.10;

        /// <summary>
        /// Generates a <see cref="DarkCoreProfile"/> from a <see cref="BigFive"/> profile and
        /// biological sex.
        /// </summary>
        /// <param name="bigFive">The character's Big Five traits, each in [0..1].</param>
        /// <param name="biology">Biological sex; <see cref="SexBiology.Male"/> receives a +0.06
        /// shift before clamping (Muris et al. 2017).</param>
        /// <param name="random">
        /// Random source for inter-individual residual noise.
        /// Pass <c>null</c> for a deterministic (mean-only) profile, useful for tests.
        /// </param>
        /// <returns>A <see cref="DarkCoreProfile"/> with all components in [0..1].</returns>
        public static DarkCoreProfile Generate(BigFive bigFive, SexBiology biology, Random? random = null)
        {
            var a = bigFive.Agreeableness;
            var n = bigFive.Neuroticism;
            var c = bigFive.Conscientiousness;

            // Step 1: generate JustifyingBeliefs as a semi-independent term.
            // Formula: 0.5 + Σ(ρ·(trait − 0.5)) + noise
            var justifyingBeliefs = 0.5
                + JustifyingBeliefsNeuroticismWeight * (n - 0.5)
                + JustifyingBeliefsConscientiousnessWeight * (c - 0.5);

            if (random is not null)
                justifyingBeliefs += SampleNoise(random);

            justifyingBeliefs = Math.Clamp(justifyingBeliefs, 0.0, 1.0);

            // Step 2: compute JustifyingBeliefs offset — how much JB shifts DarkCore above/below
            // what Agreeableness alone predicts. A character with high JB but moderate A is still
            // more dark-core than one with the same A but low JB.
            var jbOffset = (justifyingBeliefs - 0.5) * JustifyingBeliefsOffsetWeight;

            // Step 3: DarkCore raw score.
            // Formula: 0.5 + DarkCoreAgreeablenessWeight·(A − 0.5) + jbOffset + noise
            var darkCoreRaw = 0.5
                + DarkCoreAgreeablenessWeight * (a - 0.5)
                + jbOffset;

            if (random is not null)
                darkCoreRaw += SampleNoise(random);

            // Step 4: male shift applied BEFORE skew/clamp.
            // Source: Muris et al. 2017 — men score higher on all dark-triad facets.
            if (biology == SexBiology.Male)
                darkCoreRaw += MaleShift;

            // Step 5: clamp to [0,1] before applying the skew transform.
            darkCoreRaw = Math.Clamp(darkCoreRaw, 0.0, 1.0);

            // Step 6: right-skew transform — x^1.6 on [0,1] → [0,1].
            // Monotonic; compresses high values so the population is right-skewed (most near 0).
            var darkCore = Math.Pow(darkCoreRaw, SkewExponent);

            return new DarkCoreProfile(
                DarkCore: Math.Clamp(darkCore, 0.0, 1.0),
                JustifyingBeliefs: justifyingBeliefs);
        }

        /// <summary>
        /// Generates a <see cref="DarkCoreProfile"/> using the project's seeded
        /// <see cref="IRandomSource"/> — mirrors the pattern used by
        /// <see cref="ComparisonOrientationGenerator.Generate(IRandomSource, BigFive)"/>.
        /// </summary>
        /// <param name="rng">Deterministic per-character RNG; never <c>null</c>.</param>
        /// <param name="bigFive">The character's Big Five traits, each in [0..1].</param>
        /// <param name="biology">Biological sex; <see cref="SexBiology.Male"/> receives a +0.06
        /// shift before clamping (Muris et al. 2017).</param>
        /// <returns>A <see cref="DarkCoreProfile"/> with all components in [0..1].</returns>
        public static DarkCoreProfile Generate(IRandomSource rng, BigFive bigFive, SexBiology biology)
        {
            ArgumentNullException.ThrowIfNull(rng);

            var a = bigFive.Agreeableness;
            var n = bigFive.Neuroticism;
            var c = bigFive.Conscientiousness;

            // JustifyingBeliefs — semi-independent term with Box-Muller residual.
            var justifyingBeliefs = 0.5
                + JustifyingBeliefsNeuroticismWeight * (n - 0.5)
                + JustifyingBeliefsConscientiousnessWeight * (c - 0.5)
                + NextStandardNormal(rng) * NoiseSigma;

            justifyingBeliefs = Math.Clamp(justifyingBeliefs, 0.0, 1.0);

            var jbOffset = (justifyingBeliefs - 0.5) * JustifyingBeliefsOffsetWeight;

            // DarkCore raw score.
            var darkCoreRaw = 0.5
                + DarkCoreAgreeablenessWeight * (a - 0.5)
                + jbOffset
                + NextStandardNormal(rng) * NoiseSigma;

            // Male shift before skew/clamp.
            if (biology == SexBiology.Male)
                darkCoreRaw += MaleShift;

            darkCoreRaw = Math.Clamp(darkCoreRaw, 0.0, 1.0);

            // Right-skew transform.
            var darkCore = Math.Pow(darkCoreRaw, SkewExponent);

            return new DarkCoreProfile(
                DarkCore: Math.Clamp(darkCore, 0.0, 1.0),
                JustifyingBeliefs: justifyingBeliefs);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Samples Gaussian noise using the Box-Muller transform.
        /// σ = <see cref="NoiseSigma"/>; μ = 0.
        /// </summary>
        private static double SampleNoise(Random rng)
        {
            // Box-Muller: two uniform samples → one standard normal.
            var u1 = 1.0 - rng.NextDouble();
            var u2 = 1.0 - rng.NextDouble();
            var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return z * NoiseSigma;
        }

        /// <summary>
        /// Standard normal via Box-Muller using the project's <see cref="IRandomSource"/>.
        /// Identical idiom to <see cref="ComparisonOrientationGenerator"/>.
        /// </summary>
        private static double NextStandardNormal(IRandomSource rng)
        {
            var u1 = 1.0 - rng.NextUnit();
            var u2 = 1.0 - rng.NextUnit();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
