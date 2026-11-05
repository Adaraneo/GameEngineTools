// InterestActionMap.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interests
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Traits;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>Maps actions to the RIASEC dimension they exercise; shared by the drift engine and the behavior modifier.</summary>
    internal static class InterestActionMap
    {
        public const int Realistic = 0;
        public const int Investigative = 1;
        public const int Artistic = 2;
        public const int Social = 3;
        public const int Enterprising = 4;
        public const int Conventional = 5;
        public const int Dim = 6;

        // Holland's dominant theme of the occupation's work (R things, I analysis, A creating, S helping, E persuading, C data and order); unknown occupations map to nothing.
        private static readonly Dictionary<string, int> WorkThemeByOccupation = new(StringComparer.OrdinalIgnoreCase)
        {
            ["craftsperson"] = Realistic, ["farmer"] = Realistic, ["guard"] = Realistic, ["laborer"] = Realistic,
            ["merchant"] = Enterprising, ["scholar"] = Investigative, ["healer"] = Social, ["artist"] = Artistic,
            ["baker"] = Realistic, ["chef"] = Realistic, ["farmhand"] = Realistic, ["food_worker"] = Realistic, ["orchardist"] = Realistic,
            ["barista"] = Enterprising, ["barkeeper"] = Enterprising, ["shopkeeper"] = Enterprising,
            ["clerk"] = Conventional, ["librarian"] = Conventional,
            ["doctor"] = Investigative, ["programmer"] = Investigative, ["trainer"] = Social,
        };

        /// <summary>The dimension an action exercises, or −1 when it exercises none.</summary>
        public static int DimensionOf(string actionName, string? occupation) => actionName switch
        {
            Create => Artistic,
            Work => WorkDimension(occupation),
            Produce or Process => Realistic,
            Sell => Enterprising,
            ReachOut => Social,
            _ => -1
        };

        /// <summary>The theme of an occupation's work, or −1 when unknown.</summary>
        public static int WorkDimension(string? occupation)
            => occupation is not null && WorkThemeByOccupation.TryGetValue(occupation, out var dim) ? dim : -1;

        /// <summary>The interest in one dimension relative to the character's own mean (profiles are read ipsatively).</summary>
        public static double Relative(InterestProfile p, int dimension)
        {
            var values = new[] { p.Realistic, p.Investigative, p.Artistic, p.Social, p.Enterprising, p.Conventional };
            var mean = 0.0;
            foreach (var v in values) mean += v;
            return values[dimension] - mean / Dim;
        }
    }
}
