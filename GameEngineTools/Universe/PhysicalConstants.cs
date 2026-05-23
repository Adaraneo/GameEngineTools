// PhysicalConstants.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Universe;

/// <summary>
/// Fundamental physical and astronomical constants.
/// Values: CODATA 2018 / IAU 2012 nominal.
/// </summary>
public static class PhysicalConstants
{
    #region Fundamental

    /// <summary>Newtonian gravitational constant G [kg⁻¹ m³ s⁻²]. CODATA 2018.</summary>
    public const double G = 6.67430e-11;

    /// <summary>Stefan-Boltzmann constant σ [W m⁻² K⁻⁴].</summary>
    public const double StefanBoltzmann = 5.670374e-8;

    #endregion Fundamental

    #region Astronomical

    /// <summary>1 AU = 149 597 870 700 m exactly (IAU 2012).</summary>
    public const double AuInMeters = 1.495978707e11;

    /// <summary>
    /// GM of the Sun — measured directly from planetary orbits.
    /// Prefer over G × M_Sun (far more precise).
    /// </summary>
    public const double SunGM = 1.32712440018e20;

    /// <summary>GM of Earth [m³ s⁻²].</summary>
    public const double EarthGM = 3.986004418e14;

    /// <summary>Solar luminosity [W].</summary>
    public const double SunLuminosity = 3.828e26;

    /// <summary>Solar radius [km].</summary>
    public const double SunRadiusKm = 695_700;

    /// <summary>Solar effective temperature [K].</summary>
    public const double SunTempK = 5_778;

    /// <summary>Earth mass [kg].</summary>
    public const double EarthMassKg = 5.9726e24;

    /// <summary>Earth equatorial radius [km].</summary>
    public const double EarthRadiusKm = 6_371.0;

    /// <summary>Earth surface gravity [m/s²].</summary>
    public const double EarthGravity = 9.78;

    #endregion Astronomical
}
