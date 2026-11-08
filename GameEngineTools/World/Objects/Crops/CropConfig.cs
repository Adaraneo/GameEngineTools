// CropConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Crops
{
    /// <summary>
    /// Settings of the grain cycle. Each default says where it comes from: a published figure, arithmetic on one, or my own pick.
    /// Bound from <c>World:Crops</c> in <c>appsettings.World.json</c>.
    /// </summary>
    public sealed class CropConfig
    {
        /// <summary>Temperature below which a crop collects no heat; 0 degC, the base of the Georgia winter-wheat study behind the totals below.</summary>
        public double BaseTemperatureCelsius { get; set; } = 0.0;

        /// <summary>Temperature above which more heat adds nothing; 26 degC, Hanks (1991) wheat model.</summary>
        public double CeilingTemperatureCelsius { get; set; } = 26.0;

        /// <summary>Degree-days from sowing to maturity; 2800 (published 2827-2936 at base 0 degC, winter wheat; spring crops need less).</summary>
        public double DegreeDaysToMaturity { get; set; } = 2800.0;

        /// <summary>Seed per hectare; 150 kg, Walter of Henley 2-2.5 bushels per acre at 27 kg a bushel (the medieval bushel differed).</summary>
        public double SowingRateKgPerHectare { get; set; } = 150.0;

        /// <summary>Harvest as a multiple of the seed; 5, my pick in the documented 3-10, giving about 600 kg/ha net (Campbell, Norfolk: about 270-1075).</summary>
        public double YieldRatio { get; set; } = 5.0;

        /// <summary>Grams of grain in one storage unit; 150, about a meal of energy; a grain-eating adult uses 1100-1300 units a year (Campbell, Dyer; 3.4 kcal/g assumed).</summary>
        public double GramsPerUnit { get; set; } = 150.0;

        /// <summary>Share of a ripe crop lost per day it stands unreaped; 0.005, wheat trials give 0.2-2.5% a day (W. Australia, Maryland), Clarke and De Pauw 1983.</summary>
        public double OverripeLossPerDay { get; set; } = 0.005;

        /// <summary>Share of stored dry grain lost per year; 0.05, FAO on traditional storage (about 5% or less; Purdue review 1.3-7.3%).</summary>
        public double StorageLossPerYear { get; set; } = 0.05;

        /// <summary>World days between two applications of the storage loss (rounded to whole units each time).</summary>
        public double StorageLossIntervalDays { get; set; } = 30.0;

        /// <summary>A ready-to-use default configuration for tests and headless runs.</summary>
        public static CropConfig Default { get; } = new();
    }
}
