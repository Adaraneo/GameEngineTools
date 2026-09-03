namespace WorldGen.Generation;

/// <summary>
/// The handful of flat-offset ↔ lat/lon ↔ unit-sphere conversions <see cref="TectonicPlates"/> and
/// <see cref="ClimateModel"/> need to evaluate a WorldGen candidate point against a plate boundary
/// or the latitude/altitude climate gradient — restricted to the DEFAULT reference point (0,0
/// lat/lon) since TerraGen's own CLI never exposes overriding <c>MountainOriginLatDeg</c>/
/// <c>MountainOriginLonDeg</c> — every tile WorldGen reads was necessarily generated against that
/// same fixed origin.
/// </summary>
public static class PlanetGeometry
{
    /// <summary>Converts a tile's flat meters-offset coordinates (east = +X, north = +Y) from the
    /// 0,0 reference origin into true (lat,lon) using the spherical "direct geodesic problem"
    /// (great-circle distance + bearing from a known start point — see e.g. Ed Williams' Aviation
    /// Formulary, "Lat/lon given radial and distance"), which stays exact at ANY offset distance.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT the same formula as TerraGen's own <c>PlanetNoise.OffsetToLatLon</c>, which
    /// stays a flat equirectangular projection for a different reason — it must keep independently
    /// generated terrain tiles agreeing exactly on the mountain layer at shared edges, and any
    /// change to that formula would break existing <c>terrain.db</c> files' seams (see its own
    /// remarks). WorldGen has no such tile-stitching constraint (it only samples individual
    /// candidate points, never blends a grid across a shared edge), and unlike the small tectonic
    /// danger bonus this feeds <see cref="ClimateModel"/>'s biome classification directly — the
    /// PRIMARY output for a location, where the flat projection's east-west stretching away from
    /// the reference latitude would otherwise misclassify biomes at any run spanning more than a
    /// couple hundred kilometers.
    /// </remarks>
    public static (double LatDeg, double LonDeg) OffsetToLatLon(double offsetXMeters, double offsetYMeters, double planetRadiusMeters)
    {
        var angularDistance = Math.Sqrt(offsetXMeters * offsetXMeters + offsetYMeters * offsetYMeters) / planetRadiusMeters;
        if (angularDistance < 1e-12)
            return (0.0, 0.0); // exactly at the reference origin — bearing is undefined, and both formulas agree here anyway

        var bearing = Math.Atan2(offsetXMeters, offsetYMeters); // compass convention: 0 = north, clockwise toward east

        // Direct geodesic problem specialized to a start point fixed at (lat1, lon1) = (0, 0), so
        // sin(lat1) = 0 and cos(lat1) = 1 drop out of the general spherical formula:
        //   lat2 = asin(sin(lat1)*cos(d) + cos(lat1)*sin(d)*cos(bearing))
        //   lon2 = lon1 + atan2(sin(bearing)*sin(d)*cos(lat1), cos(d) - sin(lat1)*sin(lat2))
        var latRad = Math.Asin(Math.Sin(angularDistance) * Math.Cos(bearing));
        var lonRad = Math.Atan2(Math.Sin(bearing) * Math.Sin(angularDistance), Math.Cos(angularDistance));

        return (latRad * (180.0 / Math.PI), lonRad * (180.0 / Math.PI));
    }

    public static (double X, double Y, double Z) LatLonToUnitVector(double latDeg, double lonDeg)
    {
        var latRad = latDeg * Math.PI / 180.0;
        var lonRad = lonDeg * Math.PI / 180.0;
        var cosLat = Math.Cos(latRad);
        return (cosLat * Math.Cos(lonRad), cosLat * Math.Sin(lonRad), Math.Sin(latRad));
    }
}
