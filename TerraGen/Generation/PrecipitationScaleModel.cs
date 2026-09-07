namespace TerraGen.Generation;

/// <summary>Reference annual-precipitation magnitude by planet temperature — independent port of WorldGen's own <c>PrecipitationScaleModel</c> (see docs/plans/planet-physics-driven-climate.md Stage 9).</summary>
public static class PrecipitationScaleModel
{
    private const double EarthReferencePrecipMm = 1000.0;
    private const double EarthBaselineC = 13.8885;
    private const double PrecipPerKelvinRate = 0.025;

    /// <summary>Reference "humidity=1" annual precipitation (mm/year) for this planet.</summary>
    public static double ReferenceAnnualPrecipMm(PlanetSettings.Resolved planet)
    {
        var baselineC = PlanetaryTemperatureModel.EquilibriumSurfaceTemperatureC(planet);
        return EarthReferencePrecipMm * Math.Exp(PrecipPerKelvinRate * (baselineC - EarthBaselineC));
    }
}
