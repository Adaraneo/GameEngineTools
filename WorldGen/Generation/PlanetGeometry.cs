namespace WorldGen.Generation;

/// <summary>
/// The handful of flat-offset ↔ lat/lon ↔ unit-sphere conversions <see cref="TectonicPlates"/>
/// needs to classify a WorldGen candidate point against a plate boundary — an independent port of
/// the same formulas TerraGen's <c>PlanetNoise</c> uses (no project reference; see
/// WorldGen.csproj), restricted to the DEFAULT reference point (0,0 lat/lon) since TerraGen's own
/// CLI never exposes overriding <c>MountainOriginLatDeg</c>/<c>MountainOriginLonDeg</c> — every
/// tile WorldGen reads was necessarily generated against that same fixed origin.
/// </summary>
public static class PlanetGeometry
{
    /// <summary>Inverse of TerraGen's <c>PlanetNoise.LatLonToOffset</c> — converts a tile's flat
    /// meters-offset coordinates back into true (lat,lon) against the 0,0 reference origin.</summary>
    public static (double LatDeg, double LonDeg) OffsetToLatLon(double offsetXMeters, double offsetYMeters, double planetRadiusMeters)
    {
        var lat = offsetYMeters / planetRadiusMeters * (180.0 / Math.PI);
        var lon = offsetXMeters / planetRadiusMeters * (180.0 / Math.PI); // cos(0 deg) = 1
        return (lat, lon);
    }

    public static (double X, double Y, double Z) LatLonToUnitVector(double latDeg, double lonDeg)
    {
        var latRad = latDeg * Math.PI / 180.0;
        var lonRad = lonDeg * Math.PI / 180.0;
        var cosLat = Math.Cos(latRad);
        return (cosLat * Math.Cos(lonRad), cosLat * Math.Sin(lonRad), Math.Sin(latRad));
    }
}
