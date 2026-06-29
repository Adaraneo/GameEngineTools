// TemporalDiscountProfile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;

    /// <summary>
    /// Per-character hyperboloid discount-rate parameter k (Green &amp; Myerson 2004), sampled once at
    /// character creation from a lognormal distribution.
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> correlated with Big Five: Yeh, Myerson &amp; Green (2021, HCP N = 1,206)
    /// find no replicated personality correlates of discount rate after controlling for income and
    /// education, so there is no regression onto OCEAN (unlike <see cref="DarkCoreProfile"/>). k is the
    /// sole free parameter; the hyperboloid exponent s is a shared population constant living in
    /// <see cref="BehaviorConfig.DiscountHyperboloidExponent"/>.
    /// </remarks>
    /// <param name="K">Per-day discount rate, lognormally distributed, always &gt; 0.</param>
    public sealed record TemporalDiscountProfile(double K);

    /// <summary>
    /// Generates a <see cref="TemporalDiscountProfile"/> by sampling k from a lognormal distribution,
    /// independently of Big Five (see <see cref="TemporalDiscountProfile"/> remarks).
    /// </summary>
    public static class TemporalDiscountGenerator
    {
        /// <summary>
        /// Samples k from <c>Lognormal(ln(meanK), sigma)</c> using the project's seeded
        /// <see cref="IRandomSource"/>. Big Five is intentionally not an input.
        /// </summary>
        /// <param name="rng">Deterministic per-character RNG; never <c>null</c>.</param>
        /// <param name="cfg">
        /// Behavior config supplying the population mean
        /// (<see cref="BehaviorConfig.DiscountRateKMean"/>) and log-space SD
        /// (<see cref="BehaviorConfig.DiscountRateKLogSigma"/>).
        /// </param>
        /// <returns>A <see cref="TemporalDiscountProfile"/> with <c>K &gt; 0</c>.</returns>
        public static TemporalDiscountProfile Generate(IRandomSource rng, BehaviorConfig cfg)
        {
            ArgumentNullException.ThrowIfNull(rng);
            ArgumentNullException.ThrowIfNull(cfg);

            var z = NextStandardNormal(rng); // mirror DarkCoreGenerator's Box-Muller idiom
            var logK = Math.Log(cfg.DiscountRateKMean) + z * cfg.DiscountRateKLogSigma;
            return new TemporalDiscountProfile(Math.Exp(logK));
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
