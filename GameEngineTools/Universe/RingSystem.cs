// RingSystem.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Universe;

/// <summary>
/// A single band within a planetary ring system.
/// Each band has its own opacity, width, and albedo.
/// </summary>
/// <param name="Name">Conventional name (e.g. "B Ring", "Cassini Division").</param>
/// <param name="InnerRadiusKm">Inner edge distance from planet centre [km].</param>
/// <param name="OuterRadiusKm">Outer edge distance from planet centre [km].</param>
/// <param name="MeanOpticalDepth">Normal optical depth τ. 0 = transparent; >1 = opaque.</param>
/// <param name="AlbedoGeometric">Geometric albedo. Ice rings: 0.6–0.9; dark rings: 0.01–0.05.</param>
/// <param name="IsGap">True for named gaps (Cassini Division etc.) rather than material bands.</param>
public record RingBand(
    string Name,
    double InnerRadiusKm,
    double OuterRadiusKm,
    double MeanOpticalDepth,
    double AlbedoGeometric,
    bool   IsGap = false
)
{
    /// <summary>Radial width of the band [km].</summary>
    public double WidthKm => OuterRadiusKm - InnerRadiusKm;

    /// <summary>Mean orbital radius of the band [km].</summary>
    public double MeanRadiusKm => (InnerRadiusKm + OuterRadiusKm) / 2.0;

    /// <summary>
    /// Orbital period of a ring particle at mean radius [seconds].
    /// Each particle orbits independently (Kepler's 3rd law).
    /// </summary>
    public double ParticleOrbitalPeriodSeconds(double planetGM) =>
        2.0 * Math.PI * Math.Sqrt(Math.Pow(MeanRadiusKm * 1000.0, 3) / planetGM);
}

/// <summary>
/// Complete ring system with climate effect calculations.
/// </summary>
public record RingSystem(
    string                  PlanetName,
    double                  PlanetEquatorialRadiusKm,
    double                  PlanetGM,
    IReadOnlyList<RingBand> Bands
)
{
    #region Geometry

    /// <summary>Inner edge of the full ring system [km from planet centre].</summary>
    public double InnerEdgeKm => Bands.Min(b => b.InnerRadiusKm);

    /// <summary>Outer edge of the full ring system [km from planet centre].</summary>
    public double OuterEdgeKm => Bands.Max(b => b.OuterRadiusKm);

    /// <summary>Total radial span of the ring system [km].</summary>
    public double TotalSpanKm => OuterEdgeKm - InnerEdgeKm;

    /// <summary>
    /// Roche limit for a fluid body of given densities [km from planet centre].
    /// Material inside this radius cannot self-gravitate into a moon.
    /// Formula: 2.44 · R_planet · (ρ_planet / ρ_body)^(1/3).
    /// </summary>
    public double RocheLimitKm(double planetMeanDensity, double bodyDensity) =>
        PlanetEquatorialRadiusKm * 2.44 * Math.Pow(planetMeanDensity / bodyDensity, 1.0 / 3.0);

    #endregion

    #region Climate effects

    /// <summary>
    /// Shadow belt centre latitude at a given orbital fraction.
    /// The shadow sweeps between +ε and −ε degrees latitude through the year.
    /// </summary>
    /// <param name="obliquityDeg">Planet's axial tilt [degrees].</param>
    /// <param name="orbitalFraction">Position in orbit [0–1].</param>
    public static double ShadowBeltLatitudeDeg(double obliquityDeg, double orbitalFraction)
        => obliquityDeg * Math.Sin(2.0 * Math.PI * orbitalFraction);

    /// <summary>
    /// Fraction of direct starlight blocked at a surface latitude by ring shadow [0–1].
    /// Simplified: assumes a ring of given mean optical depth, scaled by proximity to shadow centre.
    /// </summary>
    /// <param name="surfaceLatitudeDeg">Observer latitude on planet [−90..90].</param>
    /// <param name="shadowCentreLat">Shadow belt centre latitude (from <see cref="ShadowBeltLatitudeDeg"/>).</param>
    /// <param name="ringOpticalDepth">Mean optical depth of the dominant ring band.</param>
    /// <param name="shadowHalfWidthDeg">Half-width of shadow band on surface [degrees]. Typically 1–3°.</param>
    public static double ShadowFraction(
        double surfaceLatitudeDeg,
        double shadowCentreLat,
        double ringOpticalDepth,
        double shadowHalfWidthDeg = 2.0)
    {
        double dist = Math.Abs(surfaceLatitudeDeg - shadowCentreLat);
        if (dist > shadowHalfWidthDeg) return 0.0;
        double fraction = 1.0 - dist / shadowHalfWidthDeg;
        // Beer-Lambert: fraction of light blocked by optical depth τ
        return fraction * (1.0 - Math.Exp(-ringOpticalDepth));
    }

    /// <summary>
    /// Approximate reflected ring irradiance delivered to the polar surface [W/m²].
    /// Rough upper bound — actual value depends on ring geometry, phase angle, viewing angle.
    /// </summary>
    /// <param name="starIrradianceAtPlanet">Incoming stellar flux at current orbital distance [W/m²].</param>
    /// <param name="dominantRingAlbedo">Albedo of the main ring.</param>
    public double ApproximatePolarRingGlow(double starIrradianceAtPlanet, double dominantRingAlbedo)
    {
        double ringArea   = Math.PI * (Math.Pow(OuterEdgeKm, 2) - Math.Pow(InnerEdgeKm, 2)) * 1e6; // m²
        double r          = PlanetEquatorialRadiusKm * 1000.0;
        double solidAngle = ringArea / (r * r);
        return dominantRingAlbedo * starIrradianceAtPlanet * solidAngle / (4.0 * Math.PI);
    }

    #endregion
}
