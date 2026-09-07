namespace WorldGen.Generation;

/// <summary>Hemispheric winter-severity asymmetry from orbital eccentricity/perihelion timing — the SMALLER of two real contributors (land/ocean distribution is the dominant one, out of scope here) — see docs/plans/planet-physics-driven-climate.md Stage 4.</summary>
// [PRIMARY] Yang et al., 2025, "What Causes the Hemispheric Difference in the Asymmetry of the Temperature Annual Cycle?", Geophys. Res. Lett., doi:10.1029/2024GL112611 -- both eccentricity/perihelion timing and land/ocean mixed-layer depth contribute; land/ocean is the PRIMARY driver, not modeled here (no per-hemisphere geography data in this codebase, same limitation as Stage 8).
// [TEXTBOOK SYNTHESIS] Distance factor (1+e*cos(lambda-omega))^2/(1-e^2)^2, the same Kepler-orbit inverse-square term Stage 3 (Milankovitch) already cites, evaluated at each hemisphere's own winter-solstice true solar longitude (270 deg north, 90 deg south, vernal-equinox-referenced).
public static class HemisphericAsymmetryModel
{
    private const double NorthernWinterSolsticeLambdaDeg = 270.0;
    private const double SouthernWinterSolsticeLambdaDeg = 90.0;

    /// <summary>Additive offset (°C) to the pole temperature for this hemisphere — 0 when OrbitEccentricity=0 or PeriapsisPhase places perihelion at an equinox (both hemispheres symmetric by construction).</summary>
    public static double PoleOffsetC(PlanetSettings.Resolved planet, bool isNorthernHemisphere)
    {
        var eccentricity = Math.Clamp(planet.OrbitEccentricity, 0.0, 0.9);
        var periapsisDeg = planet.PeriapsisPhase * 360.0;
        var northDistanceFactor = DistanceFactor(NorthernWinterSolsticeLambdaDeg, periapsisDeg, eccentricity);
        var southDistanceFactor = DistanceFactor(SouthernWinterSolsticeLambdaDeg, periapsisDeg, eccentricity);
        // Relative to the MEAN of both hemispheres, not 1.0 -- eccentricity alone (even with no
        // asymmetric phase) shifts both hemispheres' distance factor above 1 by the same shared
        // amount, which isn't asymmetry; only the hemisphere-to-hemisphere DIFFERENCE is.
        var meanDistanceFactor = (northDistanceFactor + southDistanceFactor) / 2.0;
        var distanceFactor = isNorthernHemisphere ? northDistanceFactor : southDistanceFactor;

        // Reuses Stage 8's own seasonal-amplitude scale (evaluated at the pole, where its |sin(lat)|
        // dependence peaks) as the reference magnitude, instead of an independent flux/damping
        // calculation — keeps this proportionate to (and always smaller than) the obliquity-driven
        // seasonal swing, matching the literature's "pales in comparison to obliquity" framing.
        var poleAmplitudeC = SeasonalTemperatureAmplitudeModel.AmplitudeC(planet, 90.0);
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
