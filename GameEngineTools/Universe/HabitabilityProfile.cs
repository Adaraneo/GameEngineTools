// HabitabilityProfile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Universe;

/// <summary>
/// Derived habitability assessment for a planet + star + orbit combination.
/// Computed by <see cref="HabitabilityCalculator"/>. Not a physical model — a simulation heuristic.
/// </summary>
public record HabitabilityProfile
{
    #region Core scores [0–1]

    /// <summary>Is this planet in the habitable zone of its star? [0–1]</summary>
    public required double HabitableZoneScore { get; init; }

    /// <summary>Is surface gravity compatible with complex life? [0–1]</summary>
    public required double GravityScore { get; init; }

    /// <summary>Is atmospheric pressure and composition compatible with life? [0–1]</summary>
    public required double AtmosphereScore { get; init; }

    /// <summary>Does the magnetic field protect atmosphere and surface? [0–1]</summary>
    public required double MagneticScore { get; init; }

    /// <summary>Are seasons moderate enough for complex life? [0–1]</summary>
    public required double ClimateStabilityScore { get; init; }

    /// <summary>Combined score: product of all above. 1 = Earth-like. 0 = uninhabitable.</summary>
    public double OverallScore =>
        HabitableZoneScore * GravityScore * AtmosphereScore * MagneticScore * ClimateStabilityScore;

    #endregion Core scores [0–1]

    #region Life implications

    /// <summary>Estimated mean surface temperature [K].</summary>
    public required double MeanSurfaceTempK { get; init; }

    /// <summary>Roughly what kind of life is expected, given the parameters.</summary>
    public required LifeComplexityLevel ExpectedLifeComplexity { get; init; }

    /// <summary>Key limiting factors — what prevents higher life complexity.</summary>
    public required IReadOnlyList<string> LimitingFactors { get; init; }

    #endregion Life implications
}

/// <summary>Maximum expected complexity of life given planet conditions.</summary>
public enum LifeComplexityLevel
{
    /// <summary>Chemistry cannot support life as we know it.</summary>
    Uninhabitable,

    /// <summary>Simple chemistry possible; no biological metabolism likely.</summary>
    PreBiotic,

    /// <summary>Microbial life (prokaryotes) possible.</summary>
    Microbial,

    /// <summary>Complex single-celled life (eukaryotes) possible.</summary>
    Eukaryotic,

    /// <summary>Multicellular organisms possible.</summary>
    Multicellular,

    /// <summary>Complex animals with organs and nervous systems possible.</summary>
    ComplexAnimal,

    /// <summary>Potentially intelligent life — all major constraints met.</summary>
    PotentiallyIntelligent,
}

/// <summary>Computes habitability from planet + orbital + star parameters.</summary>
public static class HabitabilityCalculator
{
    #region Main entry point

    /// <summary>Compute a full habitability profile for a planet in a given orbit around a given star.</summary>
    public static HabitabilityProfile Compute(
        PlanetConfig planet,
        OrbitalElements orbit,
        StarPhysics star)
    {
        double hz = HabitableZoneScore(orbit.SemiMajorAxisAu, star);
        double grav = GravityScore(planet.SurfaceGravityVsEarth);
        double atm = AtmosphereScore(planet.AtmospherePressureBar, planet.Atmosphere);
        double mag = MagneticScore(planet.MagneticFieldStrengthVsEarth);
        double climate = ClimateStabilityScore(planet.ObliquityDeg, orbit.Eccentricity,
                             planet.IsTidallyLocked, planet.PrimaryMoon, planet.MassKg);

        double tempK = star.EquilibriumTempK(orbit.SemiMajorAxisAu, planet.Albedo)
                     + planet.GreenhouseWarmingK;

        var limits = ComputeLimitingFactors(planet, orbit, star, tempK, hz);
        var complexity = DetermineComplexity(hz, grav, atm, mag, climate, tempK, limits);

        return new HabitabilityProfile
        {
            HabitableZoneScore = hz,
            GravityScore = grav,
            AtmosphereScore = atm,
            MagneticScore = mag,
            ClimateStabilityScore = climate,
            MeanSurfaceTempK = tempK,
            ExpectedLifeComplexity = complexity,
            LimitingFactors = limits,
        };
    }

    #endregion Main entry point

    #region Component scores

    private static double HabitableZoneScore(double orbitAu, StarPhysics star)
    {
        if (orbitAu >= star.HzInnerConservativeAu && orbitAu <= star.HzOuterConservativeAu)
            return 1.0;
        if (orbitAu >= star.HzInnerOptimisticAu && orbitAu <= star.HzOuterOptimisticAu)
            return 0.5;
        return 0.0;
    }

    private static double GravityScore(double gVsEarth)
        => gVsEarth is >= 0.4 and <= 2.0 ? 1.0
         : gVsEarth is >= 0.2 and <= 3.5 ? 0.5
         : 0.1;

    private static double AtmosphereScore(double pressureBar, AtmosphereComposition composition)
    {
        double pressScore = pressureBar is >= 0.1 and <= 5.0 ? 1.0
                          : pressureBar is >= 0.01 and <= 10.0 ? 0.5
                          : pressureBar < 0.006 ? 0.0 : 0.2;
        double compScore = composition switch
        {
            AtmosphereComposition.EarthLike => 1.0,
            AtmosphereComposition.CO2Dominated => 0.4,
            AtmosphereComposition.NitrogenMethane => 0.2,
            AtmosphereComposition.HydrogenHelium => 0.1,
            AtmosphereComposition.None => 0.0,
            _ => 0.6,
        };
        return pressScore * compScore;
    }

    private static double MagneticScore(double fieldVsEarth)
        => fieldVsEarth >= 0.5 ? 1.0
         : fieldVsEarth >= 0.1 ? 0.7
         : fieldVsEarth >= 0.01 ? 0.4
         : 0.1;

    private static double ClimateStabilityScore(
        double obliquityDeg,
        double eccentricity,
        bool tidallyLocked,
        (MoonPhysics Physics, MoonOrbit Orbit)? primaryMoon = null,
        double planetMassKg = PhysicalConstants.EarthMassKg)
    {
        if (tidallyLocked) return 0.5;
        double oblScore = obliquityDeg < 35 ? 1.0
                        : obliquityDeg < 54 ? 0.7
                        : obliquityDeg < 75 ? 0.3 : 0.1;
        double eccScore = eccentricity < 0.2 ? 1.0
                        : eccentricity < 0.4 ? 0.7
                        : eccentricity < 0.6 ? 0.4 : 0.1;
        double baseScore = oblScore * eccScore;

        // A large moon stabilizes axial tilt → a bonus to climate stability
        if (primaryMoon is { } moon)
        {
            var stab = MoonHabitabilityEffects.ObliquityStabilisationStrength(
                moon.Physics.MassKg, moon.Orbit.SemiMajorAxisKm, planetMassKg);
            var bonus = Math.Min(0.15, stab * 0.15);   // max +15 % za Luně-ekvivalentní měsíc
            baseScore = Math.Min(1.0, baseScore + bonus);
        }

        return baseScore;
    }

    private static IReadOnlyList<string> ComputeLimitingFactors(
        PlanetConfig planet, OrbitalElements orbit, StarPhysics star,
        double tempK, double hzScore)
    {
        var list = new List<string>();
        if (hzScore < 0.5) list.Add("Outside habitable zone");
        if (tempK < 200) list.Add("Too cold for liquid water");
        if (tempK > 373 + planet.GreenhouseWarmingK) list.Add("Too hot — possible runaway greenhouse");
        if (planet.SurfaceGravityVsEarth > 3) list.Add("High gravity limits animal size and flight");
        if (planet.AtmospherePressureBar < 0.006) list.Add("Pressure below water triple point — no surface liquid");
        if (planet.MagneticFieldStrengthVsEarth < 0.05) list.Add("No magnetic field — atmosphere at risk of stripping");
        if (planet.ObliquityDeg > 54) list.Add("Extreme obliquity — severe seasonal temperature swings");
        if (orbit.Eccentricity > 0.4) list.Add("High eccentricity — large annual temperature variation");
        if (planet.IsTidallyLocked) list.Add("Tidally locked — habitable zone limited to terminator band");
        return list;
    }

    private static LifeComplexityLevel DetermineComplexity(
        double hz, double grav, double atm, double mag, double climate,
        double tempK, IReadOnlyList<string> limits)
    {
        double score = hz * grav * atm * mag * climate;
        if (score < 0.05 || tempK < 150 || tempK > 500) return LifeComplexityLevel.Uninhabitable;
        if (score < 0.15) return LifeComplexityLevel.PreBiotic;
        if (score < 0.30) return LifeComplexityLevel.Microbial;
        if (score < 0.50) return LifeComplexityLevel.Eukaryotic;
        if (score < 0.65) return LifeComplexityLevel.Multicellular;
        if (score < 0.85) return LifeComplexityLevel.ComplexAnimal;
        return LifeComplexityLevel.PotentiallyIntelligent;
    }

    #endregion Component scores
}
