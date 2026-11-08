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
        /// <summary>Temperature below which a growing crop collects no thermal time. 0 degC: the Georgia (US) winter-wheat study that gives the totals below.</summary>
        public double BaseTemperatureCelsius { get; set; } = 0.0;

        /// <summary>Temperature above which more heat adds nothing. 26 degC: Hanks (1991) wheat model, linear up to 26.</summary>
        public double CeilingTemperatureCelsius { get; set; } = 26.0;

        /// <summary>Degree-days from sowing to maturity. 2800 (published range 2827-2936, base 0 degC, winter wheat); spring crops need less.</summary>
        public double DegreeDaysToMaturity { get; set; } = 2800.0;

        /// <summary>Seed per hectare in kilograms. 150: Walter of Henley's 2-2.5 bushels/acre, converted with a 27 kg bushel (the medieval bushel differed).</summary>
        public double SowingRateKgPerHectare { get; set; } = 150.0;

        /// <summary>Harvest as a multiple of the seed. 5: my pick inside the documented 3-10 for pre-industrial Europe, usually below 5 in the Middle Ages.</summary>
        public double YieldRatio { get; set; } = 5.0;

        /// <summary>Grams of grain in one storage unit. 150: roughly one meal of energy at 3.4 kcal/g and 4-5 meals a day (my arithmetic). Change it to rescale all stocks.</summary>
        public double GramsPerUnit { get; set; } = 150.0;

        /// <summary>Share of a ripe crop lost per day it stands unreaped (shattering, birds). 0.02: my guess, to be replaced by a source.</summary>
        public double OverripeLossPerDay { get; set; } = 0.02;

        /// <summary>Share of stored dry grain lost per year (damp, vermin). 0.03: my guess for well-kept grain, to be replaced by a source.</summary>
        public double StorageLossPerYear { get; set; } = 0.03;

        /// <summary>World days between two applications of the storage loss (rounded to whole units each time).</summary>
        public double StorageLossIntervalDays { get; set; } = 30.0;

        /// <summary>A ready-to-use default configuration for tests and headless runs.</summary>
        public static CropConfig Default { get; } = new();
    }
}
