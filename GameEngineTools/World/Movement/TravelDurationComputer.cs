// TravelDurationComputer.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Movement;

using GameEngineTools.World.Utils.Time;

/// <summary>
/// Stateless helper that converts distance + speed into travel duration.
/// </summary>
public static class TravelDurationComputer
{
    /// <summary>
    /// Computes travel time in minutes given distance and speed.
    /// Returns <see cref="double.MaxValue"/> if speed is zero or negative.
    /// </summary>
    /// <param name="distanceMeters">Distance to travel in metres.</param>
    /// <param name="speedMetersPerMinute">Character's current speed in metres per minute.</param>
    public static double ComputeMinutes(double distanceMeters, double speedMetersPerMinute)
    {
        if (speedMetersPerMinute <= 0)
            return double.MaxValue;
        return distanceMeters / speedMetersPerMinute;
    }

    /// <summary>
    /// Computes travel time as a <see cref="WTimeSpan"/> given distance and speed.
    /// </summary>
    /// <param name="distanceMeters">Distance to travel in metres.</param>
    /// <param name="speedMetersPerMinute">Character's current speed in metres per minute.</param>
    public static WTimeSpan ComputeSpan(double distanceMeters, double speedMetersPerMinute)
        => WTimeSpan.FromMinutes(ComputeMinutes(distanceMeters, speedMetersPerMinute));
}
