namespace TerraGen.Generation;

/// <summary>Airy isostasy — the erosion→unloading→rebound feedback loop (Stage 3.1). Source: standard Airy isostasy (e.g. Lillie, R.J. (1999), Whole Earth Geophysics, Prentice Hall).</summary>
public static class Isostasy
{
    public sealed record Parameters(
        /// <summary>How often (in SPIM iterations) accumulated eroded height converts into a direct isostatic rebound height added back into the terrain — exposed rather than hardcoded per the plan's requirement.</summary>
        int RecomputeIntervalIterations = 10,
        /// <summary>Source: Lillie (1999) standard mantle density.</summary>
        double MantleDensityKgM3 = 3300.0,
        /// <summary>Used only where no per-cell crust density (<c>RockLayer.DensityPerCell</c>) is supplied. Source: Lillie (1999) standard continental crust density.</summary>
        double DefaultCrustDensityKgM3 = 2670.0);

    /// <summary>Root depth r = h·ρc/(ρm-ρc) — the classic Airy compensation relation. Also used directly as the one-time rebound HEIGHT <see cref="StreamPowerErosion.Erode"/> adds back per recompute interval — a bounded state correction proportional to that interval's own erosion, not a rate that persists into later iterations (an earlier version of this feedback added a perpetual rate instead and diverged to +Infinity on aggressive erodibility/plate configurations).</summary>
    public static double AiryRootDepth(double topographicHeightM, double crustDensity, double mantleDensity)
        => topographicHeightM * crustDensity / (mantleDensity - crustDensity);
}
