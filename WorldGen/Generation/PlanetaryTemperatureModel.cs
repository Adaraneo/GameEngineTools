// PlanetaryTemperatureModel.cs
// Copyright (c) 50PSoftware

namespace WorldGen.Generation;

/// <summary>Derives WorldGen's equator/pole temperature pair from the planet's own physics instead of hand-tuned CLI defaults — see docs/plans/planet-physics-driven-climate.md Stage 3/4.</summary>
// [PRIMARY] Equilibrium temp: Stefan-Boltzmann radiative-balance formula (textbook derivation).
// [DESIGN SIMPLIFICATION] Equator/pole split offsets calibrated to reproduce today's 27C/-25C at Earth defaults; obliquity scaling is qualitative (real split needs ice-albedo/ocean feedback, out of scope).
// [DESIGN SIMPLIFICATION] Milankovitch term: single 41kyr sinusoid, amplitude from the ~25% 65N summer-insolation swing (Laskar et al. 2004) — not Berger (1978)'s literal coefficients.
public static class PlanetaryTemperatureModel
{
    private const double StefanBoltzmannWm2K4 = 5.670374419e-8;
    private const double AuMeters = 1.495978707e11;

    private const double EarthObliquityDeg = 23.44;
    /// <summary>Equator-minus-baseline offset (°C) at Earth's obliquity — reproduces today's hardcoded 27°C.</summary>
    private const double EquatorOffsetAtEarthObliquityC = 13.1115;
    /// <summary>Baseline-minus-pole offset (°C) at Earth's obliquity — reproduces today's hardcoded -25°C.</summary>
    private const double PoleOffsetAtEarthObliquityC = 38.8885;

    private const double MilankovitchPeriodKyr = 41.0;
    private const double MilankovitchPoleForcingAmplitudeFraction = 0.25;

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

    /// <summary>Equator/pole temperature pair (°C) for <see cref="ClimateModel"/>; epochKyr 0 = today's un-shifted Milankovitch state.</summary>
    public static (double EquatorC, double PoleC) DeriveEquatorPoleTemperatures(PlanetSettings.Resolved planet, double epochKyr = 0.0)
    {
        var baseline = EquilibriumSurfaceTemperatureC(planet);

        var obliquityDeg = planet.PlanetObliquityDeg > 0 ? planet.PlanetObliquityDeg : EarthObliquityDeg;
        var gradientScale = Math.Clamp(EarthObliquityDeg / obliquityDeg, 0.3, 3.0);

        var milankovitchForcing = MilankovitchPoleForcingAmplitudeFraction
            * Math.Sin(2.0 * Math.PI * epochKyr / MilankovitchPeriodKyr);

        var equatorC = baseline + EquatorOffsetAtEarthObliquityC * gradientScale;
        var poleC = baseline - PoleOffsetAtEarthObliquityC * gradientScale * (1.0 + milankovitchForcing);
        return (equatorC, poleC);
    }
}
