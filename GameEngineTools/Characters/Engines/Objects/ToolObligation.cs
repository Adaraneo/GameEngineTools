// ToolObligation.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Engines.Memory;

    /// <summary>The borrower's memory of a tool they owe back: an episode the borrower may forget, a reminder renews, and the walk back to the shed is as urgent as that memory is strong; every number is a design pick.</summary>
    public static class ToolObligation
    {
        // Salience of the episode: a base, plus what conscientiousness and regard for the owner add (each 0 to 1).
        private const double BaseSalience = 0.3;
        private const double ConscientiousnessWeight = 0.3;
        private const double RegardWeight = 0.3;

        // Utility the walk back gains at full memory strength.
        private const double WalkSpan = 24.0;

        // Memory strength from which the obligation is felt strongly enough to leave the work shift.
        private const double OnShiftStrength = 0.7;

        /// <summary>Hours a borrower keeps a freshly lent tool before handing it back to the owner at a meeting (a design pick: a loan is for working, not for returning on the spot).</summary>
        public const double MinHoldHours = 4.0;

        /// <summary>World days an owner carries a handed-back tool before putting it down where he stands (a design pick).</summary>
        public const double MaxCarryDays = 1.0;

        /// <summary>Chance per scene tick that an owner standing where his own tool lies takes it back: base and the part added by Conscientiousness [0..1] (design picks).</summary>
        public const double NoticeChanceBase = 0.02;

        /// <summary>The part of the notice chance that rises with Conscientiousness (a design pick).</summary>
        public const double NoticeChanceConscientiousness = 0.06;

        /// <summary>Tools one person carries at most: two hands (a design pick standing in for a hands model).</summary>
        public const int MaxCarriedTools = 2;

        /// <summary>Gets the chance per tick that a person with the given Conscientiousness notices and takes back his own tool lying here.</summary>
        public static double NoticeChance(double conscientiousness)
        {
            return NoticeChanceBase + (NoticeChanceConscientiousness * Math.Clamp(conscientiousness, 0.0, 1.0));
        }

        /// <summary>Gets the canonical <c>What</c> of the episode that says the tool is owed back.</summary>
        public static string What(string toolId)
        {
            return $"Obligation:ToolBorrowed|tool={toolId}";
        }

        /// <summary>Gets how much the episode matters to this borrower: more to a conscientious person and to one who thinks well of the owner.</summary>
        public static double Salience(double conscientiousness, double regardForOwner)
        {
            return Math.Clamp(BaseSalience + ConscientiousnessWeight * Math.Clamp(conscientiousness, 0.0, 1.0) + RegardWeight * Math.Clamp(regardForOwner, 0.0, 1.0), 0.0, 1.0);
        }

        /// <summary>Gets the strength of the memory that this tool is owed back, or <c>null</c> when the borrower has none (never borrowed, or forgotten).</summary>
        public static double? StrengthFor(IReadOnlyList<EpisodicMemory> episodes, string toolId)
        {
            var what = What(toolId);
            foreach (var episode in episodes)
            {
                if (string.Equals(episode.What, what, StringComparison.Ordinal))
                {
                    return episode.Strength;
                }
            }

            return null;
        }

        /// <summary>Gets the utility of walking the tool back: the plain utility, raised by the strength of the memory (none adds nothing).</summary>
        public static double WalkUtility(double plainUtility, double? strength)
        {
            return plainUtility + WalkSpan * Math.Clamp(strength ?? 0.0, 0.0, 1.0);
        }

        /// <summary>Gets whether the obligation is felt strongly enough to be acted on during the work shift.</summary>
        public static bool FeltOnShift(double? strength)
        {
            return strength is { } value && value >= OnShiftStrength;
        }

        /// <summary>Gets whether a carried tool is brought back now: off the work shift, on it when the obligation is strongly felt, or always for an owner who holds a handed-back tool (it is not his to keep in hand).</summary>
        public static bool ReturnsNow(bool onWorkShift, double? strength, bool ownerHoldingHandedBack = false)
        {
            return ownerHoldingHandedBack || !onWorkShift || FeltOnShift(strength);
        }
    }
}
