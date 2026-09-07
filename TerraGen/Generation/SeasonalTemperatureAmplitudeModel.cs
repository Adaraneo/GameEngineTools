namespace TerraGen.Generation;

/// <summary>Seasonal temperature-swing amplitude by latitude — independent port of WorldGen's own <c>SeasonalTemperatureAmplitudeModel</c> (see docs/plans/planet-physics-driven-climate.md Stage 8; no project reference from TerraGen to WorldGen).</summary>
public static class SeasonalTemperatureAmplitudeModel
{
    private const double RadiativeDampingBWm2K = 2.09;
    private const double DiffusivityDWm2K = 0.649;
    private const double DominantModeN = 1.0;

    private const double SecondsPerJulianYear = 3.1557e7;
    private const double OceanHeatCapacityJPerM2K = 9.7 * SecondsPerJulianYear;
    private const double LandHeatCapacityJPerM2K = 1.5e6;

    /// <summary>True orbital period (seconds) via Kepler's third law.</summary>
    public static double OrbitalPeriodSeconds(PlanetSettings.Resolved planet)
    {
        var aMeters = Math.Max(planet.OrbitSemiMajorAxisAu, 1e-6) * GameEngineTools.Universe.PhysicalConstants.AuInMeters;
        var mu = GameEngineTools.Universe.PhysicalConstants.G * Math.Max(planet.StarMassKg, 1.0);
        return 2.0 * Math.PI * Math.Sqrt(Math.Pow(aMeters, 3) / mu);
    }

    /// <summary>Seasonal temperature-swing amplitude (°C, half peak-to-peak) at this latitude — area-weighted blend of the ocean and land thermal-response amplitudes. <paramref name="oceanFraction"/> null falls back to <c>planet.PlanetOceanFraction</c>.</summary>
    public static double AmplitudeC(PlanetSettings.Resolved planet, double latDeg, double? oceanFraction = null)
    {
        var resolvedOceanFraction = Math.Clamp(oceanFraction ?? planet.PlanetOceanFraction, 0.0, 1.0);
        var oceanAmplitude = AmplitudeForHeatCapacity(planet, latDeg, OceanHeatCapacityJPerM2K);
        var landAmplitude = AmplitudeForHeatCapacity(planet, latDeg, LandHeatCapacityJPerM2K);
        return resolvedOceanFraction * oceanAmplitude + (1.0 - resolvedOceanFraction) * landAmplitude;
    }

    private static double AmplitudeForHeatCapacity(PlanetSettings.Resolved planet, double latDeg, double heatCapacityJPerM2K)
    {
        var distanceMeters = Math.Max(planet.OrbitSemiMajorAxisAu, 1e-6) * GameEngineTools.Universe.PhysicalConstants.AuInMeters;
        var albedo = Math.Clamp(planet.PlanetAlbedo, 0.0, 0.99);
        var s0 = planet.StarLuminosityWatts / (4.0 * Math.PI * distanceMeters * distanceMeters);
        var obliquityRad = planet.PlanetObliquityDeg * Math.PI / 180.0;
        var latRad = latDeg * Math.PI / 180.0;
        var qStar = s0 * (1.0 - albedo) * Math.Sin(obliquityRad) * Math.Abs(Math.Sin(latRad)) / 2.0;

        var omega = 2.0 * Math.PI / OrbitalPeriodSeconds(planet);
        var damping = RadiativeDampingBWm2K + DominantModeN * (DominantModeN + 1.0) * DiffusivityDWm2K;
        var cOmega = heatCapacityJPerM2K * omega;

        return (qStar / damping) / Math.Sqrt(1.0 + (cOmega / damping) * (cOmega / damping));
    }
}
