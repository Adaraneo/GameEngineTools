// WeatherConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Weather
{
    /// <summary>
    /// Settings of the daily weather generator. Each default says where it comes from: a published figure, arithmetic on one, or my own pick.
    /// Bound from <c>World:Weather</c> in <c>appsettings.World.json</c>.
    /// </summary>
    public sealed class WeatherConfig
    {
        /// <summary>The same settings the code uses when nothing is bound.</summary>
        public static WeatherConfig Default { get; } = new();

        /// <summary>Whether the world uses generated weather at all; false keeps the plain seasonal curve and the hash-based yield factor.</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>Seed of the weather; null lets the host pick one. One seed gives one weather history.</summary>
        public int? Seed { get; set; }

        /// <summary>Days of history re-run before the asked day; 60, enough for a temperature persistence up to 0.8 and the rain chain (my arithmetic).</summary>
        public int WarmUpDays { get; set; } = 60;

        /// <summary>Standard deviation of the daily temperature anomaly (degC); 3, my pick in 2.5-3.5, no Central European source found.</summary>
        public double TemperatureAnomalySdC { get; set; } = 3.0;

        /// <summary>Day-to-day persistence of the anomaly; 0.65, the US average of 0.62-0.67 (Richardson 1982), Central Europe unsupported.</summary>
        public double TemperaturePersistence { get; set; } = 0.65;

        /// <summary>Standard deviation of the slow seasonal shift of temperature (degC); 0.7, what is missing from the 0.78-1.07 degC summer-mean spread of Kelly and O Grada 2014 after the daily part (my arithmetic).</summary>
        public double RegimeTemperatureSdC { get; set; } = 0.7;

        /// <summary>Daily temperature range on a dry day (degC); 10, my pick.</summary>
        public double DiurnalRangeDryC { get; set; } = 10.0;

        /// <summary>Daily temperature range on a wet day (degC); 6, my pick.</summary>
        public double DiurnalRangeWetC { get; set; } = 6.0;

        /// <summary>Chance that a dry day is followed by a wet one; 0.25, my pick (no Central European value found).</summary>
        public double ProbWetAfterDry { get; set; } = 0.25;

        /// <summary>Chance that a wet day is followed by a wet one; 0.55, my pick; with the value above about 36% of days are wet.</summary>
        public double ProbWetAfterWet { get; set; } = 0.55;

        /// <summary>Seasonal swing of the wet-day chance on the logit scale; 0.25, my pick, 0 gives no wet season.</summary>
        public double WetSeasonAmplitude { get; set; } = 0.25;

        /// <summary>Season fraction (0 = vernal equinox) at which wet days are most likely; 0.3, my pick.</summary>
        public double WetSeasonPeak { get; set; } = 0.3;

        /// <summary>Standard deviation of the slow seasonal shift of the wet-day chance on the logit scale; 0.35, my pick, sized to fill the missing year-to-year variance (Kim et al. 2012 found 15% too little).</summary>
        public double RegimeWetLogitSd { get; set; } = 0.35;

        /// <summary>Share of the wet regime that is local to a place rather than shared by the whole world; 0.5, my pick; droughts are more local than temperature anomalies (Ljungqvist et al. 2023).</summary>
        public double RegionalRegimeShare { get; set; } = 0.5;

        /// <summary>Mean rain on a wet day (mm); 5, my pick.</summary>
        public double MeanWetDayMm { get; set; } = 5.0;

        /// <summary>Shape of the gamma distribution of wet-day amounts; 0.75, below 1 as is typical (Richardson and Wright 1984), my pick.</summary>
        public double WetDayShape { get; set; } = 0.75;

        /// <summary>Smallest rain that makes a day wet (mm); 0.254, the WGEN limit (Richardson and Wright 1984).</summary>
        public double WetDayThresholdMm { get; set; } = 0.254;
    }
}
