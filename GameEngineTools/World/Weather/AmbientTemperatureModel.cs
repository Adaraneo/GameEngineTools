// AmbientTemperatureModel.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Weather
{
    using System;

    /// <summary>Air temperature through the day from the day's mean and range; a plain sine, the temporary stand-in until a Parton and Logan (1981) curve is wanted.</summary>
    public static class AmbientTemperatureModel
    {
        /// <summary>
        /// The outdoor temperature at <paramref name="hourOfDay"/> (world hours): the mean plus half the range times a cosine that peaks <paramref name="peakAfterNoonHours"/> after solar noon.
        /// </summary>
        /// <param name="dailyMeanC">Mean of the day (degC); the curve averages to it over a day.</param>
        /// <param name="rangeC">Highest minus lowest temperature of the day (degC).</param>
        /// <param name="hourOfDay">World hours since midnight.</param>
        /// <param name="solarNoonHour">World hour of solar noon; NaN falls back to mid-day.</param>
        /// <param name="hoursPerDay">Length of the world day in hours.</param>
        /// <param name="peakAfterNoonHours">World hours after solar noon at which the day is warmest.</param>
        public static double Outdoor(double dailyMeanC, double rangeC, double hourOfDay, double solarNoonHour, double hoursPerDay, double peakAfterNoonHours)
        {
            var noon = double.IsNaN(solarNoonHour) ? hoursPerDay / 2.0 : solarNoonHour;
            var peak = noon + peakAfterNoonHours;
            return dailyMeanC + 0.5 * rangeC * Math.Cos(2.0 * Math.PI * (hourOfDay - peak) / hoursPerDay);
        }

        /// <summary>Air inside a building: the daily mean plus <paramref name="swingFraction"/> of how far the outdoor air is from it.</summary>
        public static double Indoor(double dailyMeanC, double outdoorNowC, double swingFraction)
            => dailyMeanC + swingFraction * (outdoorNowC - dailyMeanC);
    }
}
