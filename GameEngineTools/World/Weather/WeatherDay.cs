// WeatherDay.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Weather
{
    /// <summary>The weather of one world day at one place; temperatures are a departure from the planet's seasonal curve.</summary>
    public readonly record struct WeatherDay
    {
        /// <summary>Whole world days since the epoch (negative before it).</summary>
        public long Day { get; init; }

        /// <summary>Departure of the daily mean temperature from the seasonal curve (degC), the same for the whole world.</summary>
        public double TemperatureAnomalyC { get; init; }

        /// <summary>Whether it rained at this place.</summary>
        public bool Wet { get; init; }

        /// <summary>Rain of the day (mm), 0 on a dry day.</summary>
        public double PrecipitationMm { get; init; }

        /// <summary>Difference between the day's highest and lowest temperature (degC), smaller on a wet day.</summary>
        public double DiurnalRangeC { get; init; }
    }
}
