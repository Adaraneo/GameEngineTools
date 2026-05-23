// MoonPhysics.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Universe;

/// <summary>
/// Physical and orbital description of a moon.
/// </summary>
/// <param name="TidallyLocked">
/// True when the moon's rotation = orbital period (synchronous). Nearly universal for moons in
/// close orbits around giant planets — tidal locking timescale very short.
/// </param>
/// <param name="OrbitalResonance">
/// Resonance with another moon (e.g. "2:1 with Dione"). Null if none.
/// Resonances maintain non-zero eccentricity → tidal heating.
/// </param>
/// <param name="HasSubsurfaceOcean">Confirmed or strongly inferred subsurface liquid water.</param>
public record MoonPhysics(
    double MassKg,
    double MeanRadiusKm,
    double EquatorialRadiusKm,
    double PolarRadiusKm,
    double MeanDensityKgM3,
    double SurfaceGravityMs2,
    double EscapeVelocityKms,
    double ObliquityDeg,
    double SiderealRotationHrs,
    double Albedo,
    bool TidallyLocked,
    string? OrbitalResonance,
    bool HasSubsurfaceOcean
);

/// <summary>
/// Orbital elements of a moon relative to its parent planet (not the Sun).
/// Distances in km. Same 6-element format as stellar <see cref="OrbitalElements"/>.
/// </summary>
public record MoonOrbit(
    double SemiMajorAxisKm,
    double Eccentricity,
    double InclinationDeg,      // to parent planet's equatorial plane
    double LongAscNodeDeg,
    double ArgPeriapsisDeg,
    double MeanLongitudeDeg,
    bool IsRetrograde         // inclination > 90° — means captured body
)
{
    /// <summary>Orbital period of the moon around its parent planet [s].</summary>
    public double OrbitalPeriodSeconds(double parentBodyGM) =>
        2.0 * Math.PI * Math.Sqrt(
            Math.Pow(SemiMajorAxisKm * 1000.0, 3) / parentBodyGM);
}

/// <summary>
/// Computed habitability effects of a moon on its host planet.
/// Not a physical model — a gameplay/simulation heuristic.
/// </summary>
public static class MoonHabitabilityEffects
{
    #region Tidal force

    /// <summary>
    /// Tidal force on the host planet relative to the Earth-Moon system (= 1.0).
    /// Formula: scales as (M_moon / M_Luna) × (d_Luna / d_moon)³.
    /// </summary>
    public static double TidalForceRatio(double moonMassKg, double moonDistanceKm)
    {
        const double lunaM = 7.342e22;
        const double lunaDistKm = 384_400;
        return (moonMassKg / lunaM) * Math.Pow(lunaDistKm / moonDistanceKm, 3);
    }

    #endregion Tidal force

    #region Obliquity stabilisation

    /// <summary>
    /// Approximate obliquity stabilisation strength relative to Earth (= 1.0).
    /// Based on mass ratio moon/planet × inverse cube of distance.
    /// Above ~0.5 of Earth value: effective stabilisation over Gyr timescales.
    /// Below ~0.1: negligible; planet obliquity may drift 20–85° without other stabilising factors.
    /// </summary>
    public static double ObliquityStabilisationStrength(
        double moonMassKg,
        double moonDistanceKm,
        double planetMassKg)
    {
        const double lunaMoonRatio = 7.342e22 / 5.9726e24;  // Luna/Earth mass ratio
        const double lunaDistKm = 384_400;

        double moonRatio = moonMassKg / planetMassKg;
        double distFactor = Math.Pow(lunaDistKm / moonDistanceKm, 3);
        return (moonRatio / lunaMoonRatio) * distFactor;
    }

    #endregion Obliquity stabilisation
}
