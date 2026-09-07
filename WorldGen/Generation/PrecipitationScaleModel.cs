namespace WorldGen.Generation;

/// <summary>Reference annual-precipitation magnitude by planet temperature — see docs/plans/planet-physics-driven-climate.md Stage 9.</summary>
// [PRIMARY] Held, I.M. & Soden, B.J., 2006, "Robust Responses of the Hydrological Cycle to Global Warming," J. Climate 19:5686-5699, doi:10.1175/JCLI3990.1 — global-mean precipitation scales only ~1-3%/K with warming (energy-budget constrained), well below the ~7%/K Clausius-Clapeyron rate for water-vapor capacity.
// [DESIGN SIMPLIFICATION] EarthReferencePrecipMm anchors the whole scale (unavoidably picked, not derived) and the spatial wet/dry SHAPE stays ClimateModel's existing humidity noise field, unchanged — only the absolute magnitude that [0,1] maps onto is now temperature-dependent instead of a flat constant.
public static class PrecipitationScaleModel
{
    private const double EarthReferencePrecipMm = 1000.0;
    private const double EarthBaselineC = 13.8885;
    private const double PrecipPerKelvinRate = 0.025;

    /// <summary>Reference "humidity=1" annual precipitation (mm/year) for this planet — EarthReferencePrecipMm at Earth's own baseline temperature, scaled by PrecipPerKelvinRate per Kelvin of difference.</summary>
    public static double ReferenceAnnualPrecipMm(PlanetSettings.Resolved planet)
    {
        var baselineC = PlanetaryTemperatureModel.EquilibriumSurfaceTemperatureC(planet);
        return EarthReferencePrecipMm * Math.Exp(PrecipPerKelvinRate * (baselineC - EarthBaselineC));
    }
}
