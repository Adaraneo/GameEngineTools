// StadiumThresholds.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    /// <summary>
    /// Age boundaries (in years, inclusive lower bound) for each <see cref="StadiumType"/>.
    /// </summary>
    public sealed record StadiumThresholds(
        int ChildMin,
        int TeenagerMin,
        int AdultMin,
        int MidAgedMin,
        int OldMin)
    {
        /// <summary>
        /// Default thresholds: Baby 0-2, Child 3-11, Teenager 12-17,
        /// Adult 18-39, MidAged 40-64, Old 65+.
        /// </summary>
        public static StadiumThresholds Default => new(
            ChildMin: 3,
            TeenagerMin: 12,
            AdultMin: 18,
            MidAgedMin: 40,
            OldMin: 65);
    }
}
