// KeplerSolver.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Universe;

/// <summary>Solves Kepler's equation and computes orbital positions at any time.</summary>
public static class KeplerSolver
{
    #region Kepler's equation

    /// <summary>
    /// Solves Kepler's equation M = E − e·sin(E) via Newton-Raphson iteration.
    /// Converges in 3–5 iterations for e &lt; 0.9.
    /// </summary>
    public static double SolveEccentricAnomaly(
        double meanAnomalyRad,
        double eccentricity,
        int maxIterations = 10,
        double tolerance = 1e-10)
    {
        double E = meanAnomalyRad;
        for (int i = 0; i < maxIterations; i++)
        {
            double dE = (E - eccentricity * Math.Sin(E) - meanAnomalyRad)
                      / (1.0 - eccentricity * Math.Cos(E));
            E -= dE;
            if (Math.Abs(dE) < tolerance) break;
        }
        return E;
    }

    #endregion Kepler's equation

    #region Position in orbital plane

    /// <summary>
    /// Cartesian position (x, y) in the orbital plane at a given time offset from epoch [AU].
    /// x points toward periapsis; y is 90° ahead in orbital motion direction.
    /// </summary>
    public static (double X, double Y) OrbitalPositionAu(
        OrbitalElements orbit,
        double tSinceEpochDays,
        double centralBodyGM)
    {
        double M = MeanAnomalyAtTime(orbit, tSinceEpochDays, centralBodyGM);
        double E = SolveEccentricAnomaly(M, orbit.Eccentricity);
        double x = orbit.SemiMajorAxisAu * (Math.Cos(E) - orbit.Eccentricity);
        double y = orbit.SemiMajorAxisAu
                 * Math.Sqrt(1.0 - orbit.Eccentricity * orbit.Eccentricity)
                 * Math.Sin(E);
        return (x, y);
    }

    /// <summary>Mean anomaly at elapsed time from epoch [radians].</summary>
    public static double MeanAnomalyAtTime(
        OrbitalElements orbit,
        double tSinceEpochDays,
        double centralBodyGM)
    {
        double n = 2.0 * Math.PI / orbit.OrbitalPeriodSeconds(centralBodyGM);
        double M0 = orbit.MeanLongitudeDeg * Math.PI / 180.0;
        return M0 + n * (tSinceEpochDays * 86_400.0);
    }

    #endregion Position in orbital plane

    #region Vis-viva velocity

    /// <summary>
    /// Orbital speed at distance r from central body using the vis-viva equation [m/s].
    /// v² = GM·(2/r − 1/a).
    /// </summary>
    public static double OrbitalVelocityMs(
        double centralBodyGM,
        double distanceMeters,
        double semiMajorAxisMeters)
        => Math.Sqrt(centralBodyGM * (2.0 / distanceMeters - 1.0 / semiMajorAxisMeters));

    #endregion Vis-viva velocity
}
