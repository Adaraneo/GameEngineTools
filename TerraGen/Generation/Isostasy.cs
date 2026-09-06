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

    /// <summary>Root depth r = h·ρc/(ρm-ρc) — the classic Airy compensation relation for how deep a mountain's crustal root extends beneath topographic height h. NOT the erosion-rebound fraction — see <see cref="ErosionalReboundHeight"/> for that (a live divergence bug came from conflating the two, see its remarks).</summary>
    public static double AiryRootDepth(double topographicHeightM, double crustDensity, double mantleDensity)
        => topographicHeightM * crustDensity / (mantleDensity - crustDensity);

    /// <summary>How much of an eroded thickness reappears as surface uplift: rebound = Δh·ρc/ρm — ALWAYS &lt; 1 (≈0.81 for continental crust/mantle), so net erosion+rebound is always a net LOWERING, never a gain. Source: the standard erosional-isostasy result (e.g. England &amp; Molnar (1990), Geology 18(12):1173-1177) — distinct from <see cref="AiryRootDepth"/>'s ρc/(ρm-ρc) root-depth ratio, which is ALWAYS &gt; 1 and diverges to +Infinity if used here instead (confirmed live: a real --isostasy run compounded eroded-height feedback into float overflow within ~190 SPIM iterations before this fix).</summary>
    public static double ErosionalReboundHeight(double erodedHeightM, double crustDensity, double mantleDensity)
        => erodedHeightM * crustDensity / mantleDensity;
}
