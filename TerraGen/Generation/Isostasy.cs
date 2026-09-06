namespace TerraGen.Generation;

/// <summary>Airy isostasy — the erosion→unloading→rebound feedback loop (Stage 3.1). Source: standard Airy isostasy (e.g. Lillie, R.J. (1999), Whole Earth Geophysics, Prentice Hall).</summary>
public static class Isostasy
{
    public sealed record Parameters(
        /// <summary>How often (in SPIM iterations) accumulated eroded height converts into an isostatic rebound rate added back into the working uplift field — exposed rather than hardcoded per the plan's requirement.</summary>
        int RecomputeIntervalIterations = 10,
        /// <summary>Source: Lillie (1999) standard mantle density.</summary>
        double MantleDensityKgM3 = 3300.0,
        /// <summary>Used only where no per-cell crust density (<c>RockLayer.DensityPerCell</c>) is supplied. Source: Lillie (1999) standard continental crust density.</summary>
        double DefaultCrustDensityKgM3 = 2670.0);

    /// <summary>Root depth r = h·ρc/(ρm-ρc) — the classic Airy compensation relation.</summary>
    public static double AiryRootDepth(double topographicHeightM, double crustDensity, double mantleDensity)
        => topographicHeightM * crustDensity / (mantleDensity - crustDensity);

    /// <summary>Converts an accumulated eroded-height loss over <paramref name="intervalYears"/> into an isostatic rebound RATE (m/yr) via the same ρc/(ρm-ρc) compensation factor <see cref="AiryRootDepth"/> uses.</summary>
    public static double ReboundRatePerYear(double erodedHeightAccumulatedM, double crustDensity, double mantleDensity, double intervalYears)
        => intervalYears > 0.0 ? AiryRootDepth(erodedHeightAccumulatedM, crustDensity, mantleDensity) / intervalYears : 0.0;
}
