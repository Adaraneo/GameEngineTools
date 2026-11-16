// StadiumResolver.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    /// <summary>
    /// Resolves a character's life stage based on their age in years.
    /// </summary>
    /// <remarks>
    /// Age boundaries are intentionally kept simple and culturally neutral —
    /// they can be overridden via the <see cref="StadiumThresholds"/> record
    /// if a specific game world uses different definitions.
    /// </remarks>
    public static class StadiumResolver
    {
        /// <summary>
        /// Returns the <see cref="StadiumType"/> for a given age in years.
        /// Uses default thresholds appropriate for a medieval fantasy setting.
        /// </summary>
        /// <param name="ageYears">The character's age in whole years.</param>
        /// <param name="thresholds">
        /// Optional custom thresholds. When <c>null</c>, defaults are used.
        /// </param>
        public static StadiumType Resolve(int ageYears, StadiumThresholds? thresholds = null)
        {
            var t = thresholds ?? StadiumThresholds.Default;

            return ageYears switch
            {
                _ when ageYears < t.ChildMin => StadiumType.Baby,
                _ when ageYears < t.TeenagerMin => StadiumType.Child,
                _ when ageYears < t.AdultMin => StadiumType.Teenager,
                _ when ageYears < t.MidAgedMin => StadiumType.Adult,
                _ when ageYears < t.OldMin => StadiumType.MidAged,
                _ => StadiumType.Old
            };
        }
    }
}
