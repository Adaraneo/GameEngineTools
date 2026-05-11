// OrbitalElements.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Universe;

/// <summary>
/// The six Keplerian orbital elements that fully describe an elliptical orbit at epoch J2000.0.
/// </summary>
public record OrbitalElements(
    double SemiMajorAxisAu,    // a — size of orbit
    double Eccentricity,       // e — shape (0 = circle)
    double InclinationDeg,     // i — tilt of orbital plane
    double LongAscNodeDeg,     // Ω — RAAN
    double ArgPeriapsisDeg,    // ω — periapsis direction
    double MeanLongitudeDeg    // L₀ — position at epoch
)
{
    /// <summary>Semi-major axis in metres.</summary>
    public double SemiMajorAxisMeters => SemiMajorAxisAu * PhysicalConstants.AuInMeters;

    /// <summary>Periapsis distance [AU].</summary>
    public double PeriapsisAu => SemiMajorAxisAu * (1.0 - Eccentricity);

    /// <summary>Apoapsis distance [AU].</summary>
    public double ApoapsisAu => SemiMajorAxisAu * (1.0 + Eccentricity);

    /// <summary>Orbital period [s] via Kepler's third law.</summary>
    public double OrbitalPeriodSeconds(double centralBodyGM) =>
        2.0 * Math.PI * Math.Sqrt(Math.Pow(SemiMajorAxisMeters, 3) / centralBodyGM);

    /// <summary>Orbital period in Earth days (86 400 s each).</summary>
    public double OrbitalPeriodDays(double centralBodyGM) =>
        OrbitalPeriodSeconds(centralBodyGM) / 86_400.0;

    /// <summary>Earth reference orbit (J2000.0).</summary>
    public static readonly OrbitalElements Earth = new(
        SemiMajorAxisAu:   1.000001,
        Eccentricity:      0.01671022,
        InclinationDeg:    0.00005,
        LongAscNodeDeg:   -11.26064,
        ArgPeriapsisDeg:   102.94719,
        MeanLongitudeDeg:  100.46435
    );
}
