// DefaultMovementSpeedProvider.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Movement;

using GameEngineTools.Characters.Core;

/// <summary>
/// Default implementation of <see cref="IMovementSpeedProvider"/>.
/// Computes walking speed in metres per minute from physiological state.
/// </summary>
/// <remarks>
/// Base speed is 80 m/min (~4.8 km/h, comfortable walking pace).
/// Modifiers:
/// - Low energy (&lt;30): 60% speed
/// - High pain (&gt;50): 70% speed
/// Multipliers stack multiplicatively.
/// </remarks>
public sealed class DefaultMovementSpeedProvider : IMovementSpeedProvider
{
    private const double BaseSpeedMetersPerMinute = 80.0;

    /// <inheritdoc/>
    public double GetSpeedMetersPerMinute(EnginesSnapshot snapshot)
    {
        var physio = snapshot.Physiology;
        var multiplier = 1.0;

        if (physio.Energy < 30)
            multiplier *= 0.6;
        if (physio.Pain > 50)
            multiplier *= 0.7;

        return BaseSpeedMetersPerMinute * multiplier;
    }
}
