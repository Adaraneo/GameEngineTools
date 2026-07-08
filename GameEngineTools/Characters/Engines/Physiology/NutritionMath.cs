// NutritionMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using System;

    /// <summary>
    /// Pure calculation helpers for the nutrition sub-model, kept separate from
    /// <see cref="DefaultPhysiologyEngine"/> for testability.
    /// </summary>
    public static class NutritionMath
    {
        #region Vitamin C x non-heme iron

        /// <summary>
        /// Anchor: with/without absorption ratio at 25 mg ascorbic acid in a single meal.
        /// </summary>
        /// <remarks>
        /// Source: Cook JD &amp; Monsen ER, Am J Clin Nutr 1977;30(2):235-241,
        /// DOI 10.1093/ajcn/30.2.235, PMID 835510 (primary study).
        /// </remarks>
        private const double VitCAnchorLowMg = 25.0;
        private const double VitCAnchorLowMultiplier = 1.65;

        /// <summary>
        /// Anchor: with/without absorption ratio at 1000 mg ascorbic acid in a single meal.
        /// </summary>
        /// <remarks>Same source as <see cref="VitCAnchorLowMultiplier"/>.</remarks>
        private const double VitCAnchorHighMg = 1000.0;
        private const double VitCAnchorHighMultiplier = 9.57;

        /// <summary>
        /// Gameplay cap on the per-meal multiplier. Deliberately conservative relative to the
        /// pharmacologic 9.57x ceiling, which only occurs at 1000 mg single-meal doses — far
        /// above typical per-serving vitamin C content in the food catalog.
        /// </summary>
        private const double GameplayMultiplierCap = 3.5;

        /// <summary>
        /// Computes the single-meal non-heme iron absorption multiplier from co-ingested
        /// vitamin C, by linear interpolation between the two literature anchor points and a
        /// hard gameplay cap.
        /// </summary>
        /// <param name="vitaminCMilligrams">Vitamin C content of the meal, in mg. Values below
        /// zero are treated as zero (no enhancement).</param>
        /// <returns>Multiplier to apply to the meal's non-heme <c>IronGain</c> portion.
        /// Always &gt;= 1.0.</returns>
        /// <remarks>
        /// IMPORTANT: this multiplier is a SINGLE-MEAL effect only. It must never be applied to
        /// long-term/whole-diet iron balance — the chronic effect of vitamin C on iron stores is
        /// negligible.
        /// Source: Cook JD &amp; Reddy MB, Am J Clin Nutr 2001;73(1):93-98,
        /// DOI 10.1093/ajcn/73.1.93, PMID 11124756 (primary study).
        /// Source: Cook JD, Watson SS, Simpson KM, Lipschitz DA, Skikne BS, Blood 1984;64(3):721-726
        /// (primary study; 2000 mg/day for up to 2 years did not raise serum ferritin).
        /// </remarks>
        public static double ComputeVitaminCIronMultiplier(double vitaminCMilligrams)
        {
            var mg = Math.Max(0.0, vitaminCMilligrams);
            if (mg <= 0.0) return 1.0;

            // Linear interpolation between the two literature anchors; extrapolated flat beyond
            // the high anchor rather than allowed to grow unbounded.
            var t = Math.Clamp(
                (mg - VitCAnchorLowMg) / (VitCAnchorHighMg - VitCAnchorLowMg),
                0.0, 1.0);
            var raw = VitCAnchorLowMultiplier + t * (VitCAnchorHighMultiplier - VitCAnchorLowMultiplier);

            return Math.Min(raw, GameplayMultiplierCap);
        }

        /// <summary>
        /// Splits a meal's total <c>IronGain</c> into heme and non-heme portions and applies the
        /// vitamin C multiplier to the non-heme portion only (heme iron absorption is not
        /// meaningfully enhanced by vitamin C).
        /// </summary>
        /// <param name="totalIronGain">Total per-hour iron gain from the food's
        /// <see cref="NutritionalProfile"/>.</param>
        /// <param name="hemeIronFraction">Fraction of <paramref name="totalIronGain"/> that is
        /// heme iron, 0..1.</param>
        /// <param name="vitaminCMilligrams">Vitamin C co-ingested in the same meal, in mg.</param>
        /// <returns>Effective iron gain after applying the vitamin C enhancement to the
        /// non-heme portion.</returns>
        public static double ComputeEffectiveIronGain(
            double totalIronGain, double hemeIronFraction, double vitaminCMilligrams)
        {
            var heme = totalIronGain * Math.Clamp(hemeIronFraction, 0.0, 1.0);
            var nonHeme = totalIronGain - heme;
            var multiplier = ComputeVitaminCIronMultiplier(vitaminCMilligrams);
            return heme + nonHeme * multiplier;
        }

        #endregion Vitamin C x non-heme iron
    }
}
