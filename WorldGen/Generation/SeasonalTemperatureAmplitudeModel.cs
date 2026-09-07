namespace WorldGen.Generation;

/// <summary>Seasonal temperature-swing amplitude by latitude, including horizontal heat transport and land/ocean thermal-inertia contrast — see docs/plans/planet-physics-driven-climate.md Stage 8.</summary>
// [TEXTBOOK SYNTHESIS] 1-box linearized energy-balance response Amplitude=Q*/sqrt(B^2+(Cw)^2), appearing consistently in Lohmann 2020 (Earth Syst. Dyn. 11:1195-1208), North/Cahalan/Coakley 1981 (Rev. Geophys. 19:91-121), and standard atmospheric-dynamics teaching material.
// [PRIMARY] Horizontal diffusive heat transport adds n(n+1)*D to the radiative damping B for spherical-harmonic mode n (North & Coakley 1979's diffusive EBM); the obliquity-driven seasonal forcing is numerically ~pure mode n=1 (verified via Legendre decomposition against the real daily-insolation-swing curve before implementing), so this uses n=1's damping addition (2D) directly rather than a full spectral solve.
// [PRIMARY] North & Coakley (1979) itself states the seasonal cycle's amplitude/phase are only correct once land/ocean thermal-inertia disparity is included -- a single global C (tried first, see the plan's addendum) overshoots real amplitude ~2x. Ocean heat capacity here (9.7 W*yr/(m^2*K), ~75m seasonal mixed layer) is Lohmann (2020)'s own cited figure; land uses a much smaller representative soil active-layer value, since land barely affects the blended result either way (verified in tests).
// [DESIGN SIMPLIFICATION] Ocean fraction is uniform across every latitude (no actual per-point geography) -- a real 2D geography-resolved model (North, Mengel & Short 1983) is out of scope. Callers should pass the REAL measured ocean fraction from generated terrain when available (see WorldGen/Program.cs); oceanFraction=null falls back to the planet's own configured PlanetOceanFraction target.
public static class SeasonalTemperatureAmplitudeModel
{
    private const double RadiativeDampingBWm2K = 2.09;
    private const double DiffusivityDWm2K = 0.649;
    private const double DominantModeN = 1.0;

    private const double SecondsPerJulianYear = 3.1557e7;
    private const double OceanHeatCapacityJPerM2K = 9.7 * SecondsPerJulianYear;
    private const double LandHeatCapacityJPerM2K = 1.5e6;

    /// <summary>True orbital period (seconds) via Kepler's third law — generalizes per-planet, unlike a fixed 1-Earth-year assumption.</summary>
    public static double OrbitalPeriodSeconds(PlanetSettings.Resolved planet)
    {
        var aMeters = Math.Max(planet.OrbitSemiMajorAxisAu, 1e-6) * GameEngineTools.Universe.PhysicalConstants.AuInMeters;
        var mu = GameEngineTools.Universe.PhysicalConstants.G * Math.Max(planet.StarMassKg, 1.0);
        return 2.0 * Math.PI * Math.Sqrt(Math.Pow(aMeters, 3) / mu);
    }

    /// <summary>Seasonal temperature-swing amplitude (°C, half peak-to-peak) at this latitude — area-weighted blend of the ocean and land thermal-response amplitudes (NOT a blend of the heat capacities themselves, which would misrepresent the nonlinear per-surface-type response). <paramref name="oceanFraction"/> null falls back to <c>planet.PlanetOceanFraction</c>.</summary>
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
        // Matches the real Berger-style daily-insolation-swing integral closely across latitude (checked numerically before implementing) — see the type-level remarks.
        var qStar = s0 * (1.0 - albedo) * Math.Sin(obliquityRad) * Math.Abs(Math.Sin(latRad)) / 2.0;

        var omega = 2.0 * Math.PI / OrbitalPeriodSeconds(planet);
        var damping = RadiativeDampingBWm2K + DominantModeN * (DominantModeN + 1.0) * DiffusivityDWm2K;
        var cOmega = heatCapacityJPerM2K * omega;

        return (qStar / damping) / Math.Sqrt(1.0 + (cOmega / damping) * (cOmega / damping));
    }
}
