// EconomyConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Economy
{
    using System.Collections.Generic;

    /// <summary>
    /// Tuning configuration for the Tier 2 food economy: posted-price stock feedback, wages, and the
    /// willingness-to-pay shaping used by the provisioning bridge. Bound from the <c>Economy</c> root section of <c>appsettings.Economy.json</c>.
    /// </summary>
    /// <remarks>
    /// The price-formation coefficients here are <b>tuned game-balance parameters, not citable economic
    /// constants</b>. Gode &amp; Sunder (1993, <i>JPE</i> 101(1):119-137) establish only that <i>some</i>
    /// simple institutional rule converges toward allocative efficiency — they do not prescribe these
    /// specific coefficients. Same "mechanism confirmed, magnitude/shape tuned" pattern already used by
    /// <c>PmddEstradiolWithdrawalRef</c> elsewhere in the codebase.
    /// </remarks>
    public sealed class EconomyConfig
    {
        /// <summary>Target on-hand stock level a shop aims for; drives the price-feedback sign.</summary>
        public int TargetStockLevel { get; set; } = 8;

        /// <summary>Price change per unit of stock away from <see cref="TargetStockLevel"/> (elasticity).</summary>
        public double PriceElasticityPerUnit { get; set; } = 0.15;

        /// <summary>Hard floor on any shop price.</summary>
        public double MinPrice { get; set; } = 0.5;

        /// <summary>Hard ceiling on any shop price.</summary>
        public double MaxPrice { get; set; } = 20.0;

        /// <summary>
        /// Reference wealth used to normalize the concave affordability dampening in the provisioning
        /// bridge's willingness-to-pay: at this wealth the affordability factor is ~1.
        /// </summary>
        public double ReferenceWealth { get; set; } = 20.0;

        /// <summary>
        /// Loss-aversion coefficient λ on spending. Default <b>1.955</b> — Brown, Imai, Vieider &amp;
        /// Camerer (2024, <i>JEL</i> 62(2):485-516) pool 607 estimates to mean λ = 1.955 (95% CI
        /// [1.820, 2.102]). This deliberately replaces the older canonical λ = 2.25 from Tversky &amp;
        /// Kahneman (1992, <i>J. Risk &amp; Uncertainty</i> 5:297-323), which 2024 meta-analyses show sits
        /// at the upper edge of credible values (Walasek et al. 2024 pool λ ≈ 1.31). Mechanism confirmed,
        /// magnitude context-dependent — kept configurable in the documented range 1.3–2.25.
        /// </summary>
        public double SpendingLossAversionLambda { get; set; } = 1.955;

        /// <summary>
        /// Wage paid per hour worked, keyed by occupation id (see <c>OccupationIds</c>). A missing key
        /// falls back to <see cref="DefaultWagePerHour"/>. Game-balance seed data, not literature.
        /// </summary>
        public Dictionary<string, double> WagePerHourByOccupation { get; set; } = new()
        {
            ["farmer"] = 0.8,
            ["laborer"] = 0.8,
            ["craftsperson"] = 1.2,
            ["artist"] = 1.0,
            ["guard"] = 1.1,
            ["scholar"] = 1.4,
            ["healer"] = 1.5,
            ["merchant"] = 1.6,
        };

        /// <summary>Wage per hour for an occupation with no configured rate (or none at all).</summary>
        public double DefaultWagePerHour { get; set; } = 1.0;

        /// <summary>
        /// Starting wealth seeded at character creation, keyed by occupation id. Avoids the "cold start"
        /// problem where no NPC can afford a first purchase before earning a first wage (decision §1.4).
        /// Game-balance seed data, not literature.
        /// </summary>
        public Dictionary<string, double> StartingWealthByOccupation { get; set; } = new()
        {
            ["farmer"] = 6.0,
            ["laborer"] = 6.0,
            ["craftsperson"] = 10.0,
            ["artist"] = 8.0,
            ["guard"] = 10.0,
            ["scholar"] = 14.0,
            ["healer"] = 14.0,
            ["merchant"] = 20.0,
        };

        /// <summary>Starting wealth for an occupation with no configured seed (or none at all).</summary>
        public double DefaultStartingWealth { get; set; } = 8.0;

        /// <summary>Resolves the starting wealth for an occupation id, falling back to the default.</summary>
        public double StartingWealth(string? occupationId)
            => occupationId is not null && StartingWealthByOccupation.TryGetValue(occupationId, out var w)
                ? w
                : DefaultStartingWealth;

        /// <summary>Resolves the hourly wage for an occupation id, falling back to the default.</summary>
        public double WagePerHour(string? occupationId)
            => occupationId is not null && WagePerHourByOccupation.TryGetValue(occupationId, out var w)
                ? w
                : DefaultWagePerHour;

        /// <summary>A ready-to-use default configuration (mirrors the JSON defaults) for tests/headless runs.</summary>
        public static EconomyConfig Default { get; } = new();
    }
}
