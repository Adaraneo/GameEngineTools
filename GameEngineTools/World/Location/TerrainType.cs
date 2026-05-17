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
    Mountain
}
