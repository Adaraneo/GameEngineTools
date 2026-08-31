using GameEngineTools.World.Location;

namespace TerrainEditor.Models;

/// <summary>
/// Flat, UI-friendly view of a <see cref="GameEngineTools.World.Location.LocationDescriptor"/>
/// row — position/region are what the map canvas itself cares about; the trailing social/physical
/// fields exist so the location-edit dialog has something to read and write back without a
/// separate round-trip to the database.
/// </summary>
public sealed record LocationInfo(
    string Id,
    string DisplayName,
    string Region,
    double X,
    double Y,
    double AltitudeMeters,
    LocationType Type = LocationType.Public,
    TerrainType Terrain = TerrainType.Courtyard,
    double BaseNoise = 0.2,
    double NoisePerPerson = 0.04,
    int Capacity = 20,
    bool AllowsPrivacy = false);
