// RockType.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    using System.Collections.Generic;

    /// <summary>Lithology categories used to couple terrain erosion/strength to rock type (TerraGen SPIM Stage 2). See <see cref="RockPropertiesTable"/> for cited per-type values.</summary>
#pragma warning disable CS1591 // self-explanatory rock-type names, no per-member doc needed
    public enum RockType { Granite, Basalt, Gneiss, Schist, Quartzite, Marble, Limestone, Sandstone, Shale }
#pragma warning restore CS1591

    /// <summary>Per-lithology physical properties feeding TerraGen's stream-power erosion coupling.</summary>
    /// <param name="ErodibilityK">Stream-power law coefficient, m^0.2/yr (units specific to m=0.5 — see <see cref="RockPropertiesTable"/>).</param>
    /// <param name="DensityKgM3">Bulk rock density.</param>
    /// <param name="UcsMpa">Unconfined compressive strength, midpoint of the cited range.</param>
    public readonly record struct RockProperties(double ErodibilityK, double DensityKgM3, double UcsMpa);

    /// <summary>Cited per-lithology property table for <see cref="RockType"/>. Hillslope diffusivity is deliberately NOT here — see <see cref="GlobalHillslopeDiffusivityD"/>'s remarks.</summary>
    public static class RockPropertiesTable
    {
        /// <summary>⚠ Design simplification (not a literature per-rock-type table): Roering, Kirchner &amp; Dietrich (1999), Water Resources Research 35(3):853-870, calibrated D=0.003 m²/yr at ONE site (Oregon Coast Range) — not a per-lithology dataset. A single global value is used instead of fabricating a per-rock split; SPIM Stage 1/2 doesn't consume this yet (no hillslope-diffusion term is wired into the solver), it's reserved for a future stage that adds one.</summary>
        public const double GlobalHillslopeDiffusivityD = 0.003;

        /// <summary>UCS (MPa) source: Johnson, R.B. &amp; DeGraff, J.V. (1988), Principles of Engineering Geology, Wiley — cited averages per rock type, used verbatim.</summary>
        /// <remarks>Density (kg/m³) source: Telford, Geldart &amp; Sheriff (1990), Applied Geophysics 2nd ed., Cambridge Univ. Press — typical range MIDPOINTS (their exact table isn't reproduced verbatim here). ErodibilityK source: Stock, J.D. &amp; Montgomery, D.R. (1999), JGR 104(B3):4983-4993, doi:10.1029/98JB02139 — cites the 5-order-of-magnitude range [1e-7, 1e-2] m^0.2/yr from resistant (granite/metamorphic) to weak (mudstone) lithology, WITHOUT a full per-rock-type table; ⚠ each rock's K below is a design-simplification interpolation, log-uniformly spaced across that cited range ranked by descending UCS (a proxy for erosion resistance, not identical to it — real K also depends on jointing/weathering the intact-rock UCS number doesn't capture) — NOT individually sourced per rock.</remarks>
        public static readonly IReadOnlyDictionary<RockType, RockProperties> Values = new Dictionary<RockType, RockProperties>
        {
            [RockType.Quartzite] = new RockProperties(ErodibilityK: 1.00e-7, DensityKgM3: 2650, UcsMpa: 288.8),
            [RockType.Basalt] = new RockProperties(ErodibilityK: 4.22e-7, DensityKgM3: 2900, UcsMpa: 214.1),
            [RockType.Granite] = new RockProperties(ErodibilityK: 1.78e-6, DensityKgM3: 2700, UcsMpa: 181.7),
            [RockType.Gneiss] = new RockProperties(ErodibilityK: 7.50e-6, DensityKgM3: 2700, UcsMpa: 174.4),
            [RockType.Limestone] = new RockProperties(ErodibilityK: 3.16e-5, DensityKgM3: 2550, UcsMpa: 120.9),
            [RockType.Marble] = new RockProperties(ErodibilityK: 1.33e-4, DensityKgM3: 2700, UcsMpa: 120.5),
            [RockType.Shale] = new RockProperties(ErodibilityK: 5.62e-4, DensityKgM3: 2400, UcsMpa: 103.0),
            [RockType.Sandstone] = new RockProperties(ErodibilityK: 2.37e-3, DensityKgM3: 2350, UcsMpa: 90.1),
            [RockType.Schist] = new RockProperties(ErodibilityK: 1.00e-2, DensityKgM3: 2700, UcsMpa: 57.8),
        };
    }
}
