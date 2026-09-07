// PlanetaryTemperatureModel.cs
// Copyright (c) 50PSoftware

namespace TerraGen.Generation;

/// <summary>Derives an equator/pole temperature pair from the planet's own physics — independent port of WorldGen's own <c>PlanetaryTemperatureModel</c> (no project reference from TerraGen to WorldGen; see WorldGen.csproj) for <see cref="ClimateScanModel"/>'s use.</summary>
public static class PlanetaryTemperatureModel
{
    private const double StefanBoltzmannWm2K4 = 5.670374419e-8;
    private const double AuMeters = 1.495978707e11;

    private const double EarthObliquityDeg = 23.44;
    /// <summary>Equator-minus-baseline offset (°C) at Earth's obliquity — reproduces today's hardcoded 27°C.</summary>
    private const double EquatorOffsetAtEarthObliquityC = 13.1115;
    /// <summary>Baseline-minus-pole offset (°C) at Earth's obliquity — reproduces today's hardcoded -25°C.</summary>
    private const double PoleOffsetAtEarthObliquityC = 38.8885;

    /// <summary>Radiative-equilibrium surface temperature (°C) from starlight plus a flat greenhouse offset.</summary>
    public static double EquilibriumSurfaceTemperatureC(PlanetSettings.Resolved planet)
    {
        var distanceMeters = Math.Max(planet.OrbitSemiMajorAxisAu, 1e-6) * AuMeters;
        var albedo = Math.Clamp(planet.PlanetAlbedo, 0.0, 0.99);
        var equilibriumK4 = (1.0 - albedo) * planet.StarLuminosityWatts
            / (16.0 * Math.PI * StefanBoltzmannWm2K4 * distanceMeters * distanceMeters);
        var equilibriumK = Math.Pow(Math.Max(equilibriumK4, 0.0), 0.25);
        return equilibriumK - 273.15 + planet.PlanetGreenhouseWarmingK;
    }

    /// <summary>Equator/pole temperature pair (°C) for <see cref="ClimateScanModel"/>.</summary>
    public static (double EquatorC, double PoleC) DeriveEquatorPoleTemperatures(PlanetSettings.Resolved planet)
    {
        var baseline = EquilibriumSurfaceTemperatureC(planet);

        var obliquityDeg = planet.PlanetObliquityDeg > 0 ? planet.PlanetObliquityDeg : EarthObliquityDeg;
        var gradientScale = Math.Clamp(EarthObliquityDeg / obliquityDeg, 0.3, 3.0);

        var equatorC = baseline + EquatorOffsetAtEarthObliquityC * gradientScale;
        var poleC = baseline - PoleOffsetAtEarthObliquityC * gradientScale;
        return (equatorC, poleC);
    }
}
