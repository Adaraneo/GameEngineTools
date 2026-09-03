// TerrainGeoReference.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    /// <summary>
    /// The single planet-wide reference point and radius a terrain.db's <see cref="TerrainHeightmap"/>
    /// tiles' <c>OriginX/OriginY</c> are flat-projected against (see <c>TerraGen.Generation.TileGenerator</c>'s
    /// remarks on why every tile from every run shares one fixed reference). Persisted once per
    /// database by <c>TerraGen.Program</c> right after a batch run, so a consumer — chiefly
    /// TerrainEditor, recovering a loaded tile's true (lat,lon) via
    /// <see cref="World.FlatPlanetProjection.OffsetToLatLon"/> — never has to assume the (0,0)
    /// default matches what actually generated this particular file.
    /// </summary>
    /// <param name="RefLatDeg">Reference latitude (degrees) — <c>TileGenerator.RunSettings.MountainOriginLatDeg</c>.</param>
    /// <param name="RefLonDeg">Reference longitude (degrees) — <c>TileGenerator.RunSettings.MountainOriginLonDeg</c>.</param>
    /// <param name="PlanetRadiusMeters">Planet radius (meters) used for the projection — <c>TileGenerator.RunSettings.PlanetRadiusMeters</c>.</param>
    public sealed record TerrainGeoReference(double RefLatDeg, double RefLonDeg, double PlanetRadiusMeters);
}
