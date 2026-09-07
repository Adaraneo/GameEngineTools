namespace TerraGen.Generation;

/// <summary>Classifies a scan point's Köppen–Geiger climate code from lat/lon + elevation + planet physics — TerraGen-side counterpart to WorldGen's ClimateModel+KoppenWiring, adapted for PlanetScanner's flat lat/elevation input (no real precip seasonality, same simplification WorldGen's KoppenWiring makes).</summary>
public static class ClimateScanModel
{
    private const int NorthernHemispherePeakMonth = 6; // July
    private const int SouthernHemispherePeakMonth = 0; // January
    private const double LapseRateCPerKm = 6.5;
    private const double HumidityWavelengthMeters = 500_000.0;
    private const double AltitudeDrynessPerKm = 0.05;
    private const int Octaves = 4;
    private const double Persistence = 0.5;
    private const double Lacunarity = 2.0;
    private const double HumidityPhaseOffset = 7.318;

    /// <summary>Classifies one scan cell. <paramref name="measuredOceanFraction"/> should be the
    /// scan's OWN land/ocean split (see <see cref="PlanetScanner"/>), not the config target — same
    /// "measured, not config" preference the rest of the scanner follows.</summary>
    public static string Classify(PlanetSettings.Resolved planet, double latDeg, double lonDeg,
        double elevationMeters, double measuredOceanFraction)
    {
        var (equatorC, poleC) = PlanetaryTemperatureModel.DeriveEquatorPoleTemperatures(planet);
        var isNorthernHemisphere = latDeg >= 0.0;
        var northPoleC = poleC + HemisphericAsymmetryModel.PoleOffsetC(planet, isNorthernHemisphere: true, measuredOceanFraction);
        var southPoleC = poleC + HemisphericAsymmetryModel.PoleOffsetC(planet, isNorthernHemisphere: false, measuredOceanFraction);

        var latRad = latDeg * Math.PI / 180.0;
        var latitudeFactor = Math.Sin(latRad) * Math.Sin(latRad);
        var poleTemperatureCelsius = isNorthernHemisphere ? northPoleC : southPoleC;
        var altitudeAboveSeaKm = Math.Max(elevationMeters, 0.0) / 1000.0;
        var meanTemperatureC = double.Lerp(equatorC, poleTemperatureCelsius, latitudeFactor) - LapseRateCPerKm * altitudeAboveSeaKm;

        var (offsetX, offsetY, _) = PlanetNoise.LatLonToUnitVector(latDeg, lonDeg);
        var humidityNoise = Fbm2D(offsetX * planet.PlanetRadiusMeters / HumidityWavelengthMeters + HumidityPhaseOffset,
            offsetY * planet.PlanetRadiusMeters / HumidityWavelengthMeters + HumidityPhaseOffset, planet.Seed);
        var humidity = Math.Clamp((humidityNoise + 1.0) / 2.0 - AltitudeDrynessPerKm * altitudeAboveSeaKm, 0.0, 1.0);

        var amplitude = SeasonalTemperatureAmplitudeModel.AmplitudeC(planet, latDeg, measuredOceanFraction);
        var peakMonth = isNorthernHemisphere ? NorthernHemispherePeakMonth : SouthernHemispherePeakMonth;
        var monthlyTempC = new double[12];
        for (var m = 0; m < 12; m++)
            monthlyTempC[m] = meanTemperatureC + amplitude * Math.Cos(2.0 * Math.PI * (m - peakMonth) / 12.0);

        var annualPrecipMm = PrecipitationScaleModel.ReferenceAnnualPrecipMm(planet) * humidity;
        var monthlyPrecipMm = new double[12];
        for (var m = 0; m < 12; m++) monthlyPrecipMm[m] = annualPrecipMm / 12.0;

        return KoppenClassifier.Classify(monthlyTempC, monthlyPrecipMm, isNorthernHemisphere);
    }

    private static double Fbm2D(double x, double y, int seed)
    {
        var amplitude = 1.0;
        var frequency = 1.0;
        var sum = 0.0;
        var norm = 0.0;
        for (var o = 0; o < Octaves; o++)
        {
            sum += ValueNoise2D(x * frequency, y * frequency, seed + o * 101) * amplitude;
            norm += amplitude;
            amplitude *= Persistence;
            frequency *= Lacunarity;
        }
        return sum / norm;
    }

    private static double ValueNoise2D(double x, double y, int seed)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var x1 = x0 + 1;
        var y1 = y0 + 1;

        var tx = Smoothstep(x - x0);
        var ty = Smoothstep(y - y0);

        var v00 = LatticeValue(x0, y0, seed);
        var v10 = LatticeValue(x1, y0, seed);
        var v01 = LatticeValue(x0, y1, seed);
        var v11 = LatticeValue(x1, y1, seed);

        var top = v00 + (v10 - v00) * tx;
        var bottom = v01 + (v11 - v01) * tx;
        return top + (bottom - top) * ty;
    }

    private static double Smoothstep(double t) => t * t * (3.0 - 2.0 * t);

    private static double LatticeValue(int x, int y, int seed)
    {
        unchecked
        {
            var h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            var frac = (h & 0xFFFFFF) / (double)0xFFFFFF;
            return frac * 2.0 - 1.0;
        }
    }
}
