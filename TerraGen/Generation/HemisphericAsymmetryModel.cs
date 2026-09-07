namespace TerraGen.Generation;

/// <summary>Hemispheric winter-severity asymmetry from orbital eccentricity/perihelion timing — independent port of WorldGen's own <c>HemisphericAsymmetryModel</c> (see docs/plans/planet-physics-driven-climate.md Stage 4).</summary>
public static class HemisphericAsymmetryModel
{
    private const double NorthernWinterSolsticeLambdaDeg = 270.0;
    private const double SouthernWinterSolsticeLambdaDeg = 90.0;

    /// <summary>Additive offset (°C) to the pole temperature for this hemisphere. <paramref name="oceanFraction"/> null falls back to planet.PlanetOceanFraction.</summary>
    public static double PoleOffsetC(PlanetSettings.Resolved planet, bool isNorthernHemisphere, double? oceanFraction = null)
    {
        var eccentricity = Math.Clamp(planet.OrbitEccentricity, 0.0, 0.9);
        var periapsisDeg = planet.PeriapsisPhase * 360.0;
        var northDistanceFactor = DistanceFactor(NorthernWinterSolsticeLambdaDeg, periapsisDeg, eccentricity);
        var southDistanceFactor = DistanceFactor(SouthernWinterSolsticeLambdaDeg, periapsisDeg, eccentricity);
        var meanDistanceFactor = (northDistanceFactor + southDistanceFactor) / 2.0;
        var distanceFactor = isNorthernHemisphere ? northDistanceFactor : southDistanceFactor;

        var poleAmplitudeC = SeasonalTemperatureAmplitudeModel.AmplitudeC(planet, 90.0, oceanFraction);
        return poleAmplitudeC * (distanceFactor - meanDistanceFactor);
    }

    private static double DistanceFactor(double lambdaDeg, double periapsisDeg, double eccentricity)
    {
        var angleRad = (lambdaDeg - periapsisDeg) * Math.PI / 180.0;
        var numerator = Math.Pow(1.0 + eccentricity * Math.Cos(angleRad), 2.0);
        var denominator = Math.Pow(1.0 - eccentricity * eccentricity, 2.0);
        return numerator / denominator;
    }
}
