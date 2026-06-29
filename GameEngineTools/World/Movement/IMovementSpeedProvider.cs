// IMovementSpeedProvider.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Movement;

using GameEngineTools.Characters.Core;
using GameEngineTools.World.Location;

/// <summary>
/// Computes the effective movement speed for a character based on their current state.
/// </summary>
public interface IMovementSpeedProvider
{
    /// <summary>
    /// Returns the character's movement speed in metres per minute.
    /// </summary>
    /// <param name="snapshot">Current engine snapshot of the character.</param>
    /// <param name="terrain">
    /// Terrain of the path being travelled. Defaults to <see cref="TerrainType.Indoor"/>
    /// for backward compatibility with call sites that have not yet been updated.
    /// </param>
    double GetSpeedMetersPerMinute(EnginesSnapshot snapshot, TerrainType terrain = TerrainType.Indoor);
}
