// PlanetConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Universe;

/// <summary>
/// Complete configurable planet definition.
/// All parameters that matter for climate, weather, and life.
/// </summary>
public record PlanetConfig
{
    #region Identity

    /// <summary>Planet name — used in logging and narrative output.</summary>
    public required string Name { get; init; }

    /// <summary>Scientific classification of planet type.</summary>
    public required PlanetArchetype Archetype { get; init; }

    #endregion Identity

    #region Physical (bulk)

    /// <summary>Total mass [kg].</summary>
    public required double MassKg { get; init; }

    /// <summary>Equatorial radius [km].</summary>
    public required double EquatorialRadiusKm { get; init; }

    /// <summary>Polar radius [km]. Set = EquatorialRadius for a perfect sphere.</summary>
    public required double PolarRadiusKm { get; init; }

    /// <summary>Mean density [kg/m³]. Derived property, but can be set directly.</summary>
    public double MeanDensityKgM3 { get; init; }

    #endregion Physical (bulk)

    #region Rotation

    /// <summary>Sidereal rotation period [hours]. Negative = retrograde.</summary>
    public required double SiderealRotationHrs { get; init; }

    /// <summary>
    /// Axial tilt relative to orbital plane [°].
    /// 0° = no seasons. 23.4° = Earth. 97.8° = Uranus (near pole-on).
    /// </summary>
    public required double ObliquityDeg { get; init; }

    /// <summary>True when rotation period = orbital period (star-facing hemisphere fixed).</summary>
    public bool IsTidallyLocked { get; init; }

    #endregion Rotation

    #region Atmosphere

    /// <summary>Mean surface pressure [bar]. Earth = 1.013 bar.</summary>
    public required double AtmospherePressureBar { get; init; }

    /// <summary>Dominant gas composition. Used for greenhouse effect and biochemistry.</summary>
    public required AtmosphereComposition Atmosphere { get; init; }

    /// <summary>
    /// Total greenhouse warming offset above equilibrium temperature [K].
    /// Earth = +33 K. Venus ≈ +500 K. Airless bodies = 0 K.
    /// </summary>
    public required double GreenhouseWarmingK { get; init; }

    #endregion Atmosphere

    #region Surface

    /// <summary>Bond albedo [0–1]. Fraction of incoming radiation reflected. Earth = 0.306.</summary>
    public required double Albedo { get; init; }

    /// <summary>Fraction of surface covered by liquid water [0–1].</summary>
    public required double OceanFraction { get; init; }

    /// <summary>Fraction of surface covered by land [0–1]. OceanFraction + LandFraction ≤ 1.</summary>
    public required double LandFraction { get; init; }

    /// <summary>Whether the planet has active plate tectonics (CO₂ recycling, mountain building).</summary>
    public bool HasPlateTectonics { get; init; }

    #endregion Surface

    #region Magnetic field

    /// <summary>
    /// Magnetic field strength relative to Earth [0 = none, 1 = Earth, > 1 = stronger].
    /// Affects atmosphere retention and surface radiation dose.
    /// </summary>
    public required double MagneticFieldStrengthVsEarth { get; init; }

    #endregion Magnetic field

    #region Moon and ring system

    /// <summary>
    /// Volitelný primární měsíc — nejhmotnější těleso na oběžné dráze planety.
    /// <c>null</c> = planeta bez měsíce.
    /// </summary>
    public (MoonPhysics Physics, MoonOrbit Orbit)? PrimaryMoon { get; init; }

    /// <summary>Volitelný prstencový systém. <c>null</c> = žádné prstence.</summary>
    public RingSystem? Rings { get; init; }

    #endregion Moon and ring system

    #region Computed physical properties

    /// <summary>Standard gravitational parameter μ = GM [m³ s⁻²].</summary>
    public double GravitationalParameter => PhysicalConstants.G * MassKg;

    /// <summary>Mean radius [km].</summary>
    public double MeanRadiusKm => (2.0 * EquatorialRadiusKm + PolarRadiusKm) / 3.0;

    /// <summary>Surface gravitational acceleration at equator [m/s²].</summary>
    public double SurfaceGravityMs2 =>
        GravitationalParameter / Math.Pow(EquatorialRadiusKm * 1000.0, 2);

    /// <summary>Surface gravity as a multiple of Earth's.</summary>
    public double SurfaceGravityVsEarth => SurfaceGravityMs2 / PhysicalConstants.EarthGravity;

    /// <summary>Escape velocity [km/s].</summary>
    public double EscapeVelocityKms =>
        Math.Sqrt(2.0 * GravitationalParameter / (MeanRadiusKm * 1000.0)) / 1000.0;

    /// <summary>Ellipticity (flattening) = (R_eq − R_pol) / R_eq.</summary>
    public double Ellipticity =>
        (EquatorialRadiusKm - PolarRadiusKm) / EquatorialRadiusKm;

    #endregion Computed physical properties

    #region Known planet instances

    /// <summary>Earth — reference configuration.</summary>
    public static readonly PlanetConfig Earth = new()
    {
        Name = "Earth",
        Archetype = PlanetArchetype.RockyTerrestrial,
        MassKg = 5.9726e24,
        EquatorialRadiusKm = 6_378.1,
        PolarRadiusKm = 6_356.8,
        MeanDensityKgM3 = 5_514,
        SiderealRotationHrs = 23.9345,
        ObliquityDeg = 23.44,
        IsTidallyLocked = false,
        AtmospherePressureBar = 1.013,
        Atmosphere = AtmosphereComposition.EarthLike,
        GreenhouseWarmingK = 33.0,
        Albedo = 0.306,
        OceanFraction = 0.71,
        LandFraction = 0.29,
        HasPlateTectonics = true,
        MagneticFieldStrengthVsEarth = 1.0,
        PrimaryMoon = (
            new MoonPhysics(
                MassKg: 7.342e22,
                MeanRadiusKm: 1_737.4,
                EquatorialRadiusKm: 1_738.1,
                PolarRadiusKm: 1_736.0,
                MeanDensityKgM3: 3_346.4,
                SurfaceGravityMs2: 1.62,
                EscapeVelocityKms: 2.38,
                ObliquityDeg: 6.68,
                SiderealRotationHrs: 655.7,
                Albedo: 0.12,
                TidallyLocked: true,
                OrbitalResonance: null,
                HasSubsurfaceOcean: false),
            new MoonOrbit(
                SemiMajorAxisKm: 384_400,
                Eccentricity: 0.0549,
                InclinationDeg: 5.145,
                LongAscNodeDeg: 0,
                ArgPeriapsisDeg: 0,
                MeanLongitudeDeg: 0,
                IsRetrograde: false)),
        Rings = null,
    };

    #endregion Known planet instances
}

/// <summary>Dominant atmospheric chemistry — determines greenhouse potential and biochemistry.</summary>
public enum AtmosphereComposition
{
    /// <summary>No significant atmosphere (airless body).</summary>
    None,

    /// <summary>~78% N₂, ~21% O₂, CO₂ trace. Aerobic life possible.</summary>
    EarthLike,

    /// <summary>CO₂ dominated (Venus, Mars type). Greenhouse dominated; no O₂.</summary>
    CO2Dominated,

    /// <summary>H₂/He dominated. Gas giant or hydrogen-rich super-Earth.</summary>
    HydrogenHelium,

    /// <summary>N₂ dominated with CH₄ haze (Titan type). Cold; organic chemistry.</summary>
    NitrogenMethane,

    /// <summary>Custom — parameters defined separately in simulation config.</summary>
    Custom,
}

/// <summary>Fundamental planet type — determines default parameter ranges and biome templates.</summary>
public enum PlanetArchetype
{
    RockyTerrestrial,
    SuperEarth,
    OceanWorld,
    DesertPlanet,
    TidalLocked,
    GasGiant,
    IceGiant,
}
