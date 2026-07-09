// UniverseConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Astro;

using GameEngineTools.Universe;

/// <summary>
/// Flat binding record pro sekci <c>World:Universe</c> v appsettings.
/// Default values match Earth/Sol — game worlds override them in <c>appsettings.World.json</c>.
/// </summary>
/// <remarks>
/// Flat properties (no nested records) are required for IOptions binding to work correctly.
/// The conversion methods <see cref="ToStarPhysics"/>, <see cref="ToOrbitalElements"/>
/// and <see cref="ToPlanetConfig"/> build typed objects for <c>GameEngineTools.Universe</c>.
/// </remarks>
public sealed record UniverseConfig(
    // ── Star — default: Sol (G2 V) ───────────────────────────────────────────
    string StarSpectralType = "G2 V",
    double StarMassKg = 1.9885e30,
    double StarRadiusKm = 695_700,
    double StarLuminosityWatts = 3.828e26,
    double StarEffectiveTempK = 5_778,

    // ── Orbit — default: Earth J2000.0 ───────────────────────────────────────
    double OrbitSemiMajorAxisAu = 1.000001,
    double OrbitEccentricity = 0.01671022,
    double OrbitInclinationDeg = 0.00005,

    // ── Planet — default: Earth ──────────────────────────────────────────────
    string PlanetName = "Earth",
    double PlanetMassKg = 5.9726e24,
    double PlanetEquatorialRadiusKm = 6_378.1,
    double PlanetPolarRadiusKm = 6_356.8,
    double PlanetSiderealRotationHrs = 23.9345,
    double PlanetObliquityDeg = 23.44,
    double PlanetAlbedo = 0.306,
    double PlanetGreenhouseWarmingK = 33.0,
    double PlanetAtmospherePressureBar = 1.013,
    string PlanetAtmosphere = "EarthLike",
    string PlanetArchetype = "RockyTerrestrial",
    double PlanetMagneticFieldStrengthVsEarth = 1.0,

    // ── Primary moon — default: Luna ─────────────────────────────────────────
    bool HasMoon = false,
    double MoonMassKg = 7.342e22,
    double MoonMeanRadiusKm = 1_737.4,
    double MoonOrbitalDistanceKm = 384_400,
    double MoonOrbitalEccentricity = 0.0549,
    double MoonOrbitalInclinationDeg = 5.145,
    double MoonAlbedo = 0.12,
    bool MoonTidallyLocked = true,

    // ── Ring system — default: no rings ──────────────────────────────────────
    bool HasRings = false,
    double RingInnerRadiusKm = 0.0,
    double RingOuterRadiusKm = 0.0,
    double RingMeanOpticalDepth = 1.0,
    double RingAlbedo = 0.7,

    // ── Calendar overlay ─────────────────────────────────────────────────────
    // When UseAsCalendarSource is true, the runtime derives WorldTimeSpec from this section
    // (via PlanetaryCalendarFactory) instead of the hand-authored InitWorldClock section.
    bool UseAsCalendarSource   = false,
    int  CalendarMonthCount    = 12,
    int  CalendarTargetYearDays = 0,          // 0 = derive the year length from the orbit
    long CalendarTicksPerSecond = 10_000_000,
    int  CalendarMinutesPerHour = 60,
    int  CalendarSecondsPerMinute = 60,
    int  CalendarLeapYearInterval = 0,        // 0 = no leap years
    int  CalendarLeapExtraDays  = 0)
{
    /// <summary>Default constructor — Earth/Sol values, with no moon or rings.</summary>
    public UniverseConfig() : this(
        StarSpectralType: "G2 V",
        StarMassKg: 1.9885e30,
        StarRadiusKm: 695_700,
        StarLuminosityWatts: 3.828e26,
        StarEffectiveTempK: 5_778,
        OrbitSemiMajorAxisAu: 1.000001,
        OrbitEccentricity: 0.01671022,
        OrbitInclinationDeg: 0.00005,
        PlanetName: "Earth",
        PlanetMassKg: 5.9726e24,
        PlanetEquatorialRadiusKm: 6_378.1,
        PlanetPolarRadiusKm: 6_356.8,
        PlanetSiderealRotationHrs: 23.9345,
        PlanetObliquityDeg: 23.44,
        PlanetAlbedo: 0.306,
        PlanetGreenhouseWarmingK: 33.0,
        PlanetAtmospherePressureBar: 1.013,
        PlanetAtmosphere: "EarthLike",
        PlanetArchetype: "RockyTerrestrial",
        PlanetMagneticFieldStrengthVsEarth: 1.0,
        HasMoon: false,
        MoonMassKg: 7.342e22,
        MoonMeanRadiusKm: 1_737.4,
        MoonOrbitalDistanceKm: 384_400,
        MoonOrbitalEccentricity: 0.0549,
        MoonOrbitalInclinationDeg: 5.145,
        MoonAlbedo: 0.12,
        MoonTidallyLocked: true,
        HasRings: false,
        RingInnerRadiusKm: 0.0,
        RingOuterRadiusKm: 0.0,
        RingMeanOpticalDepth: 1.0,
        RingAlbedo: 0.7,
        UseAsCalendarSource: false,
        CalendarMonthCount: 12,
        CalendarTargetYearDays: 0,
        CalendarTicksPerSecond: 10_000_000,
        CalendarMinutesPerHour: 60,
        CalendarSecondsPerMinute: 60,
        CalendarLeapYearInterval: 0,
        CalendarLeapExtraDays: 0)
    { }

    /// <summary>Builds <see cref="StarPhysics"/> from the flat properties.</summary>
    public StarPhysics ToStarPhysics() => new(
        StarMassKg,
        StarRadiusKm,
        StarLuminosityWatts,
        StarEffectiveTempK,
        StarSpectralType);

    /// <summary>Builds <see cref="OrbitalElements"/> from the flat properties.</summary>
    public OrbitalElements ToOrbitalElements() => new(
        SemiMajorAxisAu: OrbitSemiMajorAxisAu,
        Eccentricity: OrbitEccentricity,
        InclinationDeg: OrbitInclinationDeg,
        LongAscNodeDeg: 0,
        ArgPeriapsisDeg: 0,
        MeanLongitudeDeg: 0);

    /// <summary>Builds <see cref="PlanetConfig"/> from the flat properties.</summary>
    public PlanetConfig ToPlanetConfig() => new()
    {
        Name = PlanetName,
        Archetype = Enum.Parse<PlanetArchetype>(PlanetArchetype),
        MassKg = PlanetMassKg,
        EquatorialRadiusKm = PlanetEquatorialRadiusKm,
        PolarRadiusKm = PlanetPolarRadiusKm,
        SiderealRotationHrs = PlanetSiderealRotationHrs,
        ObliquityDeg = PlanetObliquityDeg,
        Albedo = PlanetAlbedo,
        GreenhouseWarmingK = PlanetGreenhouseWarmingK,
        AtmospherePressureBar = PlanetAtmospherePressureBar,
        Atmosphere = Enum.Parse<AtmosphereComposition>(PlanetAtmosphere),
        MagneticFieldStrengthVsEarth = PlanetMagneticFieldStrengthVsEarth,
        OceanFraction = 0.5,
        LandFraction = 0.5,
        HasPlateTectonics = true,
        PrimaryMoon = ToMoon(),
        Rings = ToRingSystem(),
    };

    /// <summary>
    /// Builds the primary moon from the flat properties.
    /// Returns <c>null</c> if <see cref="HasMoon"/> is <c>false</c>.
    /// </summary>
    public (MoonPhysics Physics, MoonOrbit Orbit)? ToMoon()
    {
        if (!HasMoon) return null;
        return (
            new MoonPhysics(
                MassKg: MoonMassKg,
                MeanRadiusKm: MoonMeanRadiusKm,
                EquatorialRadiusKm: MoonMeanRadiusKm,
                PolarRadiusKm: MoonMeanRadiusKm,
                MeanDensityKgM3: 0,
                SurfaceGravityMs2: 0,
                EscapeVelocityKms: 0,
                ObliquityDeg: 0,
                SiderealRotationHrs: 0,
                Albedo: MoonAlbedo,
                TidallyLocked: MoonTidallyLocked,
                OrbitalResonance: null,
                HasSubsurfaceOcean: false),
            new MoonOrbit(
                SemiMajorAxisKm: MoonOrbitalDistanceKm,
                Eccentricity: MoonOrbitalEccentricity,
                InclinationDeg: MoonOrbitalInclinationDeg,
                LongAscNodeDeg: 0,
                ArgPeriapsisDeg: 0,
                MeanLongitudeDeg: 0,
                IsRetrograde: false));
    }

    /// <summary>
    /// Builds the ring system from the flat properties.
    /// Returns <c>null</c> if <see cref="HasRings"/> is <c>false</c>.
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

    /// <summary>Builds the <see cref="CalendarOptions"/> cultural overlay from the flat properties.</summary>
    public CalendarOptions ToCalendarOptions() => new(
        MonthCount:       CalendarMonthCount,
        TargetYearDays:   CalendarTargetYearDays > 0 ? CalendarTargetYearDays : null,
        MinutesPerHour:   CalendarMinutesPerHour,
        SecondsPerMinute: CalendarSecondsPerMinute,
        TicksPerSecond:   CalendarTicksPerSecond,
        LeapYearInterval: CalendarLeapYearInterval,
        LeapExtraDays:    CalendarLeapExtraDays);
}
