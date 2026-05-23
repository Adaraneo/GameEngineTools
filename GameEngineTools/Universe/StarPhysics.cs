// StarPhysics.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Universe;

/// <summary>
/// Physical parameters of a main-sequence star.
/// Provides irradiance, equilibrium temperature, and habitable zone calculations.
/// </summary>
/// <param name="MassKg">Stellar mass [kg].</param>
/// <param name="RadiusKm">Photospheric radius [km].</param>
/// <param name="LuminosityWatts">Total power output [W].</param>
/// <param name="EffectiveTempK">Photospheric effective temperature [K].</param>
/// <param name="SpectralType">IAU spectral type (e.g. "G2 V", "K5 V", "M3 V").</param>
public record StarPhysics(
    double MassKg,
    double RadiusKm,
    double LuminosityWatts,
    double EffectiveTempK,
    string SpectralType
)
{
    #region Derived properties

    /// <summary>Standard gravitational parameter μ = GM [m³ s⁻²].</summary>
    public double GravitationalParameter => PhysicalConstants.G * MassKg;

    /// <summary>Luminosity expressed as a multiple of solar luminosity (L☉).</summary>
    public double LuminosityRatioToSun => LuminosityWatts / PhysicalConstants.SunLuminosity;

    #endregion Derived properties

    #region Irradiance

    /// <summary>
    /// Stellar irradiance (flux) at a given distance [W/m²].
    /// Follows the inverse-square law: E = L / (4π·d²).
    /// </summary>
    public double IrradianceAtAu(double distanceAu)
    {
        double d = distanceAu * PhysicalConstants.AuInMeters;
        return LuminosityWatts / (4.0 * Math.PI * d * d);
    }

    /// <summary>
    /// Orbit-averaged irradiance for an eccentric orbit.
    /// Higher than circular orbit at same semi-major axis: F_avg = F_circ / sqrt(1−e²).
    /// </summary>
    public double OrbitAveragedIrradiance(double semiMajorAxisAu, double eccentricity)
    {
        double f = IrradianceAtAu(semiMajorAxisAu);
        return f / Math.Sqrt(1.0 - eccentricity * eccentricity);
    }

    #endregion Irradiance

    #region Habitable zone (Kopparapu et al. 2013)

    /// <summary>
    /// Conservative habitable zone inner edge — runaway greenhouse limit [AU].
    /// Formula: sqrt(L/1.107).
    /// </summary>
    public double HzInnerConservativeAu => Math.Sqrt(LuminosityRatioToSun / 1.107);

    /// <summary>
    /// Conservative habitable zone outer edge — maximum greenhouse limit [AU].
    /// Formula: sqrt(L/0.356).
    /// </summary>
    public double HzOuterConservativeAu => Math.Sqrt(LuminosityRatioToSun / 0.356);

    /// <summary>Optimistic HZ inner edge (recent Venus limit) [AU].</summary>
    public double HzInnerOptimisticAu => Math.Sqrt(LuminosityRatioToSun / 1.776);

    /// <summary>Optimistic HZ outer edge (early Mars limit) [AU].</summary>
    public double HzOuterOptimisticAu => Math.Sqrt(LuminosityRatioToSun / 0.320);

    #endregion Habitable zone (Kopparapu et al. 2013)

    #region Equilibrium temperature

    /// <summary>
    /// Radiative equilibrium temperature of a planet at given distance [K].
    /// Does NOT include greenhouse warming. Add ~33 K for Earth-like atmosphere.
    /// </summary>
    public double EquilibriumTempK(double distanceAu, double albedo)
    {
        double d = distanceAu * PhysicalConstants.AuInMeters;
        return EffectiveTempK
               * Math.Sqrt(RadiusKm * 1000.0 / (2.0 * d))
               * Math.Pow(1.0 - albedo, 0.25);
    }

    #endregion Equilibrium temperature

    #region Known star instances

    /// <summary>The Sun — G2 V. Source: NASA NSSDCA.</summary>
    public static readonly StarPhysics Sol = new(
        MassKg: 1.9885e30,
        RadiusKm: 695_700,
        LuminosityWatts: 3.828e26,
        EffectiveTempK: 5_778,
        SpectralType: "G2 V"
    );

    /// <summary>
    /// Idealised K5 V star — "superhabitable" sweet spot.
    /// Longer-lived than G stars (~30 Gyr), lower UV, HZ not too close.
    /// </summary>
    public static readonly StarPhysics IdealKDwarf = new(
        MassKg: 0.72 * 1.9885e30,
        RadiusKm: 0.72 * 695_700,
        LuminosityWatts: 0.24 * 3.828e26,
        EffectiveTempK: 4_400,
        SpectralType: "K5 V"
    );

    #endregion Known star instances
}
