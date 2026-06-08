// InterestProfile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using System;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Holland's RIASEC vocational-interest profile. Each dimension is in [0..1].
    /// </summary>
    /// <remarks>
    /// Generated once from Big Five + sex + occupational exposure, then drifts from rewarding
    /// experience (R5). Big Five → RIASEC weights are multivariate estimates from Larson et al.
    /// (2002). Sex priors are population effect sizes from Su, Rounds &amp; Armstrong (2009) —
    /// applied as distributional shifts, never deterministically (large within-sex overlap).
    /// </remarks>
    public sealed record InterestProfile(
        /// <summary>Realistic — hands-on, mechanical, physical ("Things"). Weak Big Five link; mostly exposure-driven.</summary>
        double Realistic,

        /// <summary>Investigative — analytic, scientific, curious. Predictor: Openness (.28).</summary>
        double Investigative,

        /// <summary>Artistic — creative, expressive. Strongest predictor: Openness (.48).</summary>
        double Artistic,

        /// <summary>Social — helping, teaching, "People". Predictor: Extraversion (.31).</summary>
        double Social,

        /// <summary>Enterprising — leading, persuading. Predictor: Extraversion (.41).</summary>
        double Enterprising,

        /// <summary>Conventional — organising, detail, order. Predictor: Conscientiousness (.25).</summary>
        double Conventional);

    /// <summary>
    /// Generates an <see cref="InterestProfile"/> from Big Five, biological sex, and occupational exposure.
    /// </summary>
    public static class InterestProfileGenerator
    {
        private const double NoiseSigma = 0.10;

        // Su, Rounds & Armstrong (2009) sex effect sizes (Cohen's d, positive = men higher).
        // Applied as a modest distributional shift, NOT a deterministic difference.
        private const double SexShiftScale = 0.10;

        private const double D_Realistic = 0.84;
        private const double D_Investigative = 0.26;
        private const double D_Artistic = -0.35;
        private const double D_Social = -0.68;
        private const double D_Enterprising = -0.10; // weak / mixed in the literature
        private const double D_Conventional = -0.33;

        /// <summary>
        /// Generates a profile. <paramref name="random"/> may be <c>null</c> for a deterministic
        /// (mean-only) profile in tests.
        /// </summary>
        public static InterestProfile Generate(
            BigFive bigFive, SexBiology sex, string? occupation = null, Random? random = null)
        {
            var o = bigFive.Openness;
            var e = bigFive.Extraversion;
            var c = bigFive.Conscientiousness;

            // Big Five → RIASEC (Larson et al. 2002, multivariate).
            var artistic = 0.5 + 0.48 * (o - 0.5);
            var enterprising = 0.5 + 0.41 * (e - 0.5);
            var social = 0.5 + 0.31 * (e - 0.5);
            var investigative = 0.5 + 0.28 * (o - 0.5);
            var conventional = 0.5 + 0.25 * (c - 0.5);

            // Realistic is exposure-driven: occupation hint nudges it; otherwise neutral.
            var realistic = 0.5 + OccupationRealisticBias(occupation);

            // Sex prior (population shift; sign by biological sex; none for Intersex/Unknown).
            var sexSign = sex switch
            {
                SexBiology.Male => 1.0,
                SexBiology.Female => -1.0,
                _ => 0.0
            };
            realistic += sexSign * D_Realistic * SexShiftScale;
            investigative += sexSign * D_Investigative * SexShiftScale;
            artistic += sexSign * D_Artistic * SexShiftScale;
            social += sexSign * D_Social * SexShiftScale;
            enterprising += sexSign * D_Enterprising * SexShiftScale;
            conventional += sexSign * D_Conventional * SexShiftScale;

            if (random is not null)
            {
                realistic += Noise(random);
                investigative += Noise(random);
                artistic += Noise(random);
                social += Noise(random);
                enterprising += Noise(random);
                conventional += Noise(random);
            }

            return new InterestProfile(
                Realistic: Clamp(realistic),
                Investigative: Clamp(investigative),
                Artistic: Clamp(artistic),
                Social: Clamp(social),
                Enterprising: Clamp(enterprising),
                Conventional: Clamp(conventional));
        }

        private static double OccupationRealisticBias(string? occupation)
        {
            if (string.IsNullOrWhiteSpace(occupation)) return 0.0;
            var o = occupation.ToLowerInvariant();
            // Crude exposure heuristic — manual/physical trades raise Realistic.
            if (o.Contains("farm") || o.Contains("smith") || o.Contains("labor") ||
                o.Contains("craft") || o.Contains("build") || o.Contains("guard") ||
                o.Contains("hunt") || o.Contains("miner"))
                return 0.20;
            return 0.0;
        }

        private static double Noise(Random rng)
        {
            var u1 = 1.0 - rng.NextDouble();
            var u2 = 1.0 - rng.NextDouble();
            var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return z * NoiseSigma;
        }

        private static double Clamp(double v) => Math.Clamp(v, 0.0, 1.0);
    }
}
