// UniverseConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Astro;

using GameEngineTools.Universe;

/// <summary>
/// Flat binding record pro sekci <c>World:Universe</c> v appsettings.
/// Výchozí hodnoty odpovídají Zemi/Sol — herní světy je přepíší v <c>appsettings.World.json</c>.
/// </summary>
/// <remarks>
/// Flat properties (bez vnořených recordů) jsou nutné pro správné fungování IOptions bindingu.
/// Konverzní metody <see cref="ToStarPhysics"/>, <see cref="ToOrbitalElements"/>
/// a <see cref="ToPlanetConfig"/> sestaví typed objekty pro <c>GameEngineTools.Universe</c>.
/// </remarks>
public sealed record UniverseConfig(
    // ── Hvězda — výchozí: Sol (G2 V) ─────────────────────────────────────────
    string StarSpectralType                  = "G2 V",
    double StarMassKg                        = 1.9885e30,
    double StarRadiusKm                      = 695_700,
    double StarLuminosityWatts               = 3.828e26,
    double StarEffectiveTempK                = 5_778,

    // ── Orbita — výchozí: Země J2000.0 ───────────────────────────────────────
    double OrbitSemiMajorAxisAu              = 1.000001,
    double OrbitEccentricity                 = 0.01671022,
    double OrbitInclinationDeg               = 0.00005,

    // ── Planeta — výchozí: Země ───────────────────────────────────────────────
    string PlanetName                        = "Earth",
    double PlanetMassKg                      = 5.9726e24,
    double PlanetEquatorialRadiusKm          = 6_378.1,
    double PlanetPolarRadiusKm               = 6_356.8,
    double PlanetSiderealRotationHrs         = 23.9345,
    double PlanetObliquityDeg                = 23.44,
    double PlanetAlbedo                      = 0.306,
    double PlanetGreenhouseWarmingK          = 33.0,
    double PlanetAtmospherePressureBar       = 1.013,
    string PlanetAtmosphere                  = "EarthLike",
    string PlanetArchetype                   = "RockyTerrestrial",
    double PlanetMagneticFieldStrengthVsEarth = 1.0,

    // ── Primární měsíc — výchozí: Luna ───────────────────────────────────────
    bool   HasMoon                            = false,
    double MoonMassKg                         = 7.342e22,
    double MoonMeanRadiusKm                   = 1_737.4,
    double MoonOrbitalDistanceKm              = 384_400,
    double MoonOrbitalEccentricity            = 0.0549,
    double MoonOrbitalInclinationDeg          = 5.145,
    double MoonAlbedo                         = 0.12,
    bool   MoonTidallyLocked                  = true,

    // ── Prstencový systém — výchozí: žádné prstence ──────────────────────────
    bool   HasRings                           = false,
    double RingInnerRadiusKm                  = 0.0,
    double RingOuterRadiusKm                  = 0.0,
    double RingMeanOpticalDepth               = 1.0,
    double RingAlbedo                         = 0.7)
{
    /// <summary>Výchozí konstruktor — hodnoty Země/Sol, bez měsíce a prstenců.</summary>
    public UniverseConfig() : this(
        StarSpectralType:                  "G2 V",
        StarMassKg:                        1.9885e30,
        StarRadiusKm:                      695_700,
        StarLuminosityWatts:               3.828e26,
        StarEffectiveTempK:                5_778,
        OrbitSemiMajorAxisAu:              1.000001,
        OrbitEccentricity:                 0.01671022,
        OrbitInclinationDeg:               0.00005,
        PlanetName:                        "Earth",
        PlanetMassKg:                      5.9726e24,
        PlanetEquatorialRadiusKm:          6_378.1,
        PlanetPolarRadiusKm:               6_356.8,
        PlanetSiderealRotationHrs:         23.9345,
        PlanetObliquityDeg:                23.44,
        PlanetAlbedo:                      0.306,
        PlanetGreenhouseWarmingK:          33.0,
        PlanetAtmospherePressureBar:       1.013,
        PlanetAtmosphere:                  "EarthLike",
        PlanetArchetype:                   "RockyTerrestrial",
        PlanetMagneticFieldStrengthVsEarth: 1.0,
        HasMoon:                           false,
        MoonMassKg:                        7.342e22,
        MoonMeanRadiusKm:                  1_737.4,
        MoonOrbitalDistanceKm:             384_400,
        MoonOrbitalEccentricity:           0.0549,
        MoonOrbitalInclinationDeg:         5.145,
        MoonAlbedo:                        0.12,
        MoonTidallyLocked:                 true,
        HasRings:                          false,
        RingInnerRadiusKm:                 0.0,
        RingOuterRadiusKm:                 0.0,
        RingMeanOpticalDepth:              1.0,
        RingAlbedo:                        0.7) { }

    /// <summary>Sestaví <see cref="StarPhysics"/> z flat properties.</summary>
    public StarPhysics ToStarPhysics() => new(
        StarMassKg,
        StarRadiusKm,
        StarLuminosityWatts,
        StarEffectiveTempK,
        StarSpectralType);

    /// <summary>Sestaví <see cref="OrbitalElements"/> z flat properties.</summary>
    public OrbitalElements ToOrbitalElements() => new(
        SemiMajorAxisAu:  OrbitSemiMajorAxisAu,
        Eccentricity:     OrbitEccentricity,
        InclinationDeg:   OrbitInclinationDeg,
        LongAscNodeDeg:   0,
        ArgPeriapsisDeg:  0,
        MeanLongitudeDeg: 0);

    /// <summary>Sestaví <see cref="PlanetConfig"/> z flat properties.</summary>
    public PlanetConfig ToPlanetConfig() => new()
    {
        Name                         = PlanetName,
        Archetype                    = Enum.Parse<PlanetArchetype>(PlanetArchetype),
        MassKg                       = PlanetMassKg,
        EquatorialRadiusKm           = PlanetEquatorialRadiusKm,
        PolarRadiusKm                = PlanetPolarRadiusKm,
        SiderealRotationHrs          = PlanetSiderealRotationHrs,
        ObliquityDeg                 = PlanetObliquityDeg,
        Albedo                       = PlanetAlbedo,
        GreenhouseWarmingK           = PlanetGreenhouseWarmingK,
        AtmospherePressureBar        = PlanetAtmospherePressureBar,
        Atmosphere                   = Enum.Parse<AtmosphereComposition>(PlanetAtmosphere),
        MagneticFieldStrengthVsEarth = PlanetMagneticFieldStrengthVsEarth,
        OceanFraction                = 0.5,
        LandFraction                 = 0.5,
        HasPlateTectonics            = true,
        PrimaryMoon                  = ToMoon(),
        Rings                        = ToRingSystem(),
    };

    /// <summary>
    /// Sestaví primární měsíc z flat properties.
    /// Vrátí <c>null</c> pokud <see cref="HasMoon"/> je <c>false</c>.
    /// </summary>
    public (MoonPhysics Physics, MoonOrbit Orbit)? ToMoon()
    {
        if (!HasMoon) return null;
        return (
            new MoonPhysics(
                MassKg:              MoonMassKg,
                MeanRadiusKm:        MoonMeanRadiusKm,
                EquatorialRadiusKm:  MoonMeanRadiusKm,
                PolarRadiusKm:       MoonMeanRadiusKm,
                MeanDensityKgM3:     0,
                SurfaceGravityMs2:   0,
                EscapeVelocityKms:   0,
                ObliquityDeg:        0,
                SiderealRotationHrs: 0,
                Albedo:              MoonAlbedo,
                TidallyLocked:       MoonTidallyLocked,
                OrbitalResonance:    null,
                HasSubsurfaceOcean:  false),
            new MoonOrbit(
                SemiMajorAxisKm:  MoonOrbitalDistanceKm,
                Eccentricity:     MoonOrbitalEccentricity,
                InclinationDeg:   MoonOrbitalInclinationDeg,
                LongAscNodeDeg:   0,
                ArgPeriapsisDeg:  0,
                MeanLongitudeDeg: 0,
                IsRetrograde:     false));
    }

    /// <summary>
    /// Sestaví prstencový systém z flat properties.
    /// Vrátí <c>null</c> pokud <see cref="HasRings"/> je <c>false</c>.
    /// </summary>
    public RingSystem? ToRingSystem()
    {
        if (!HasRings || RingOuterRadiusKm <= RingInnerRadiusKm) return null;
        return new RingSystem(
            PlanetName,
            PlanetEquatorialRadiusKm,
            PhysicalConstants.G * PlanetMassKg,
            new[] { new RingBand("Main", RingInnerRadiusKm, RingOuterRadiusKm,
                                 RingMeanOpticalDepth, RingAlbedo) });
    }
}
