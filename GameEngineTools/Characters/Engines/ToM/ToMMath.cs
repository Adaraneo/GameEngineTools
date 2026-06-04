// ToMMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.ToM
{
    using System;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Pure, stateless math for Theory-of-Mind recursion depth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Humans default to ~2 levels of recursive mentalising ("I think that you think…") and can
    /// push to ~4 on average, with a long tail (Kinderman, Dunbar &amp; Bentall 1998; Stiller &amp;
    /// Dunbar 2007). Per-NPC ceilings are drawn from a normal distribution (mean ≈ 4, SD ≈ 1).
    /// </para>
    /// <para>
    /// Cognitive load collapses recursion: under stress the usable depth degrades by 1–2 levels.
    /// </para>
    /// </remarks>
    public static class ToMMath
    {
        /// <summary>Default working recursion depth used for everyday reasoning.</summary>
        public const int DefaultRecursionDepth = 2;

        /// <summary>Population mean of the per-NPC ToM ceiling.</summary>
        public const double CeilingMean = 4.0;

        /// <summary>Population standard deviation of the per-NPC ToM ceiling.</summary>
        public const double CeilingSd = 1.0;

        private const int CeilingMin = 1;
        private const int CeilingMax = 8;

        /// <summary>
        /// Draws a per-NPC ToM ceiling from a normal distribution (mean 4, SD 1), rounded to an
        /// integer and clamped to <see cref="CeilingMin"/>..<see cref="CeilingMax"/>.
        /// </summary>
        public static int GenerateCeiling(IRandomSource random)
        {
            // Box-Muller via the project RNG (deterministic, testable).
            var u1 = 1.0 - random.NextUnit();
            var u2 = 1.0 - random.NextUnit();
            var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

            var raw = CeilingMean + z * CeilingSd;
            var rounded = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
            return Math.Clamp(rounded, CeilingMin, CeilingMax);
        }

        /// <summary>
        /// Returns the usable recursion depth given a base ceiling and current stress [0..100].
        /// High stress collapses depth by up to two levels; the result never drops below 1.
        /// </summary>
        public static int EffectiveToMDepth(int baseCeiling, double stress)
        {
            var degrade = stress switch
            {
                > 70.0 => 2,
                > 40.0 => 1,
                _ => 0
            };

            return Math.Max(1, baseCeiling - degrade);
        }
    }
}
