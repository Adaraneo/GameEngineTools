// DiscountedValueMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System;

    /// <summary>
    /// Pure temporal-discounting math used by <see cref="DiscountedValueModifier"/>. Stateless,
    /// analogous to <see cref="BehaviorMath"/>.
    /// </summary>
    internal static class DiscountedValueMath
    {
        /// <summary>
        /// Green &amp; Myerson (2004) hyperboloid discount factor: <c>F(D) = 1 / (1 + k·D)^s</c>.
        /// Monotonically decreasing in <paramref name="delayDays"/> for fixed positive k, s; equals
        /// 1.0 at D = 0 (no discount). With s &lt; 1 the tail decays more slowly than a simple
        /// exponential, the empirically observed shape.
        /// </summary>
        /// <param name="delayDays">Delay to the reward/cost, in days. Values ≤ 0 yield 1.0.</param>
        /// <param name="k">Per-day discount rate (always &gt; 0).</param>
        /// <param name="s">Hyperboloid exponent; Green &amp; Myerson report s &lt; 1.0.</param>
        /// <returns>Discount factor in (0, 1].</returns>
        /// <remarks>Source: Green &amp; Myerson 2004, <i>Psychological Bulletin</i> 130(5):769–792.</remarks>
        public static double HyperboloidFactor(double delayDays, double k, double s)
            => delayDays <= 0.0 ? 1.0 : 1.0 / Math.Pow(1.0 + k * delayDays, s);

        /// <summary>
        /// Laibson (1997) quasi-hyperbolic β-δ discount factor: <c>F(D) = β · δ^D</c> for D &gt; 0,
        /// and 1.0 at D = 0. The drop from 1.0 to β·δ^ε as D crosses zero is the intentional
        /// "present bias" discontinuity (time-inconsistent / commitment-device scenarios).
        /// </summary>
        /// <param name="delayDays">Delay in days; values ≤ 0 yield 1.0 (the present).</param>
        /// <param name="beta">Present-bias parameter β in (0, 1]; β = 1 means no present bias.</param>
        /// <param name="delta">Per-day exponential discount factor δ in (0, 1).</param>
        /// <returns>Discount factor in (0, 1].</returns>
        /// <remarks>Source: Laibson 1997, <i>QJE</i> 112:443–478.</remarks>
        public static double QuasiHyperbolicFactor(double delayDays, double beta, double delta)
            => delayDays <= 0.0 ? 1.0 : beta * Math.Pow(delta, delayDays);
    }
}
