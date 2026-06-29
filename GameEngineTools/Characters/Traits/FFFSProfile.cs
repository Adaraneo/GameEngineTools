// FFFSProfile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using System;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Trait-level sensitivity to the Fight-Flight-Freeze System (FFFS) — fast, active escape/panic
    /// response to proximal threat. Empirically separable from BIS-anxiety (FFFS↔trait anxiety r≈.23 vs.
    /// BIS↔trait anxiety r≈.82; Corr &amp; Cooper 2016) and from deliberative Prevention-focus vigilance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bridges to Big Five via the Neuroticism FEAR-FACET specifically (FFFS↔Neuroticism r≈.35–.43),
    /// NOT via trait anxiety (FFFS↔STAI r only ≈.23 — explicitly weaker). Includes a gender prior per
    /// Corr &amp; Cooper (2016): FFFS↔gender r≈−.33 (females higher), unlike BIS which showed no gender
    /// difference.
    /// </para>
    /// Sources: Gray &amp; McNaughton (2000); McNaughton &amp; Corr (2004, <i>Neuroscience and
    /// Biobehavioral Reviews</i> 28:285–305); Corr &amp; Cooper (2016, <i>Psychological Assessment</i>
    /// 28(11):1427–1440, RST-PQ validation).
    /// </remarks>
    /// <param name="Sensitivity">FFFS trait sensitivity [0..1]. Higher = faster/stronger escape-panic
    /// activation under proximal threat.</param>
    public sealed record FFFSProfile(double Sensitivity);

    /// <summary>
    /// Generates an <see cref="FFFSProfile"/> from the Neuroticism fear-facet plus a female gender prior,
    /// mirroring <see cref="DarkCoreGenerator"/>'s Box-Muller idiom and constant-shift pattern.
    /// </summary>
    public static class FFFSGenerator
    {
        // Source: Corr & Cooper 2016 — FFFS↔Neuroticism r≈.35–.43 (weaker than BIS↔Neuroticism r≈.71–.72).
        private const double NeuroticismWeight = 0.40;

        // Source: Corr & Cooper 2016 — FFFS↔gender r≈−.33 (females score higher); applied as a constant
        // shift, mirroring DarkCoreGenerator's MaleShift pattern but in the opposite direction.
        private const double FemaleShift = 0.10;

        private const double NoiseSigma = 0.15;

        /// <summary>
        /// Samples an <see cref="FFFSProfile"/> from a <see cref="BigFive"/> profile and biological sex
        /// using the project's seeded <see cref="IRandomSource"/>.
        /// </summary>
        /// <param name="rng">Deterministic per-character RNG; never <c>null</c>.</param>
        /// <param name="bigFive">The character's Big Five traits, each in [0..1].</param>
        /// <param name="biology">Biological sex; <see cref="SexBiology.Female"/> receives a +0.10 shift
        /// (Corr &amp; Cooper 2016). <see cref="SexBiology.Male"/>/<see cref="SexBiology.Unknown"/> get none.</param>
        /// <returns>An <see cref="FFFSProfile"/> with Sensitivity in [0..1].</returns>
        public static FFFSProfile Generate(IRandomSource rng, BigFive bigFive, SexBiology biology)
        {
            ArgumentNullException.ThrowIfNull(rng);

            var raw = 0.5
                + NeuroticismWeight * (bigFive.Neuroticism - 0.5)
                + NextStandardNormal(rng) * NoiseSigma;

            if (biology == SexBiology.Female)
                raw += FemaleShift;

            return new FFFSProfile(Sensitivity: Math.Clamp(raw, 0.0, 1.0));
        }

        /// <summary>
        /// Standard normal via Box-Muller using the project's <see cref="IRandomSource"/> — identical
        /// idiom to <see cref="DarkCoreGenerator"/>.
        /// </summary>
        private static double NextStandardNormal(IRandomSource rng)
        {
            var u1 = 1.0 - rng.NextUnit();
            var u2 = 1.0 - rng.NextUnit();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
