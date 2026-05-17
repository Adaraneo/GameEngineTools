// IMovementSpeedProvider.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Movement;

using GameEngineTools.Characters.Core;

/// <summary>
/// Computes the effective movement speed for a character based on their current state.
/// </summary>
public interface IMovementSpeedProvider
{
    /// <summary>
    /// Returns the character's movement speed in metres per minute.
    /// </summary>
    /// <param name="snapshot">Current engine snapshot of the character.</param>
    double GetSpeedMetersPerMinute(EnginesSnapshot snapshot);
}
