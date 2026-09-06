namespace TerraGen.Generation;

/// <summary>Latitude-dependent prevailing wind for OrographicPrecipitation, replacing a single fixed compass bearing — see docs/plans/planet-physics-driven-climate.md Stage 6.</summary>
// [PRIMARY] Belt boundary: Held & Hou (1980) small-angle thermal-Rossby-number scaling, J. Atmos. Sci. 37(3):515-533.
// [TEXTBOOK SYNTHESIS] Three-band surface wind structure (trade winds / westerlies / polar easterlies) from Coriolis-deflected Hadley/Ferrel/polar cell surface flow.
// [DESIGN SIMPLIFICATION] Equator/pole temperature delta and tropopause height are a local, independent re-derivation of WorldGen's PlanetaryTemperatureModel (no project reference — same duplication convention as PlanetSettings) — Held-Hou is a scaling model, not a quantitative predictor, so this is not meant to reproduce a real atmosphere exactly.
public static class PrevailingWindModel
{
    private const double StefanBoltzmannWm2K4 = 5.670374419e-8;
    private const double AuMeters = 1.495978707e11;
    private const double EarthObliquityDeg = 23.44;
    private const double EquatorPoleDeltaAtEarthObliquityK = 51.9999;
    private const double AssumedTropopauseHeightMeters = 15_000.0;
    private const double MinBeltBoundaryDeg = 5.0;
    private const double MaxBeltBoundaryDeg = 55.0;
    private const double PolarBeltBoundaryDeg = 60.0;

    /// <summary>Held-Hou tropical/subtropical belt boundary (degrees latitude) for this planet — faster rotation or a smaller equator/pole gradient narrows it.</summary>
    public static double SubtropicalBeltBoundaryDeg(PlanetSettings.Resolved planet)
    {
        var distanceMeters = Math.Max(planet.OrbitSemiMajorAxisAu, 1e-6) * AuMeters;
        var albedo = Math.Clamp(planet.PlanetAlbedo, 0.0, 0.99);
        var equilibriumK4 = (1.0 - albedo) * planet.StarLuminosityWatts
            / (16.0 * Math.PI * StefanBoltzmannWm2K4 * distanceMeters * distanceMeters);
        var baselineK = Math.Pow(Math.Max(equilibriumK4, 0.0), 0.25) + planet.PlanetGreenhouseWarmingK;

        var obliquityDeg = planet.PlanetObliquityDeg > 0 ? planet.PlanetObliquityDeg : EarthObliquityDeg;
        var gradientScale = Math.Clamp(EarthObliquityDeg / obliquityDeg, 0.3, 3.0);
        var deltaThetaK = EquatorPoleDeltaAtEarthObliquityK * gradientScale;

        var rotationHrs = planet.PlanetSiderealRotationHrs > 0 ? planet.PlanetSiderealRotationHrs : 23.9345;
        var omega = 2.0 * Math.PI / (rotationHrs * 3600.0);
        var a = planet.PlanetRadiusMeters;

        var numerator = 5.0 * planet.GravityMs2 * AssumedTropopauseHeightMeters * deltaThetaK;
        var denominator = 3.0 * omega * omega * a * a * Math.Max(baselineK, 1.0);
        var phiHRad = Math.Sqrt(Math.Max(numerator / denominator, 0.0));
        var phiHDeg = phiHRad * 180.0 / Math.PI;
        return Math.Clamp(phiHDeg, MinBeltBoundaryDeg, MaxBeltBoundaryDeg);
    }

    /// <summary>Compass bearing the wind blows FROM at this latitude — same convention as OrographicPrecipitation.Parameters.WindDirectionFromDeg.</summary>
    public static double WindDirectionFromDeg(double latDeg, double subtropicalBeltBoundaryDeg)
    {
        var absLat = Math.Abs(latDeg);
        var north = latDeg >= 0;

        if (absLat < subtropicalBeltBoundaryDeg) return north ? 45.0 : 135.0; // trade winds (equatorward)
        if (absLat < PolarBeltBoundaryDeg) return north ? 225.0 : 315.0; // mid-latitude westerlies (poleward)
        return north ? 45.0 : 135.0; // polar easterlies (equatorward)
    }
}
