// TerrainType.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Location;

/// <summary>
/// Broad physical terrain category for a location.
/// Used by the movement system (speed modifiers) and by behavior modifiers
/// when outdoor/indoor distinction matters.
/// </summary>
public enum TerrainType
{
    /// <summary>Enclosed building interior — default.</summary>
    Indoor,

    /// <summary>Open courtyard or garden area within a settlement.</summary>
    Courtyard,

    /// <summary>Natural forest or woodland area.</summary>
    Forest,

    /// <summary>Paved or dirt road between settlements.</summary>
    Road,

    /// <summary>River, lake, or other body of water requiring crossing.</summary>
    Water,

    /// <summary>Highland terrain with altitude effects.</summary>
    Mountain,

    /// <summary>Open, flat, low-lying land — fields and grassland away from any settlement.</summary>
    Plains,

    /// <summary>Land bordering open water — beaches, cliffs, harbor fronts.</summary>
    Coastline,

    /// <summary>Hot, dry land — low humidity and high temperature (simplified Köppen "B").</summary>
    Desert,

    /// <summary>Cold land near or below freezing — polar latitudes or high altitude (simplified
    /// Köppen "E").</summary>
    Tundra,

    /// <summary>Warm, seasonally dry grassland — moderate-to-low humidity and high temperature,
    /// flat enough to build on (simplified Köppen "Aw"/"BSh").</summary>
    Savanna,

    /// <summary>Hot, consistently wet lowland — high humidity and high temperature (simplified
    /// Köppen "Af").</summary>
    Jungle
}
