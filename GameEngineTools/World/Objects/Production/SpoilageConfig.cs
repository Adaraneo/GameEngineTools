// SpoilageConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Production
{
    using System.Collections.Generic;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Per-category food-spoilage rates. Freshness decays as a simplified exponential/linear model,
    /// NOT a full Arrhenius temperature-dependent kinetic model — that fidelity is out of scope for
    /// believability-driven NPC simulation.
    /// </summary>
    /// <remarks>
    /// Rates are a design simplification derived from typical <c>RespawnMinutes</c> proxies (faster
    /// respawn ≈ faster spoilage), NOT literature-derived physical constants. See
    /// docs/research/food-economy-research-findings.md §3 for the Arrhenius framework these approximate.
    /// Bindable from <c>Characters:Spoilage</c>; <see cref="Default"/> is used when unconfigured.
    /// </remarks>
    public sealed record SpoilageConfig
    {
        /// <summary>Fraction of freshness lost per in-world hour, per item kind. Missing kinds never spoil.</summary>
        public IReadOnlyDictionary<PickupItemKind, double> RatePerHour { get; init; }
            = new Dictionary<PickupItemKind, double>
            {
                // // Source: docs/research/food-economy-research-findings.md §3 (design simplification, not a physical constant)
                [PickupItemKind.Milk]   = 1.0 / 60,   // ~2.5 days
                [PickupItemKind.Bread]  = 1.0 / 72,   // ~3 days
                [PickupItemKind.Food]   = 1.0 / 48,   // ~2 days (generic perishable)
                [PickupItemKind.Cheese] = 1.0 / 336,  // ~14 days
                [PickupItemKind.Grain]  = 1.0 / 720,  // ~30 days (dry)
                [PickupItemKind.Flour]  = 1.0 / 720,  // ~30 days (dry)
            };

        /// <summary>The default rate table.</summary>
        public static SpoilageConfig Default { get; } = new();
    }

    /// <summary>Computes food freshness lazily from <c>WorldObject.ProducedAt</c> and a spoilage rate.</summary>
    public static class Spoilage
    {
        /// <summary>
        /// Freshness in [0..1] at <paramref name="now"/>: 1.0 when just produced, 0.0 when spoiled.
        /// Objects without a <c>ProducedAt</c> or a configured rate never spoil (returns 1.0).
        /// </summary>
        public static double Freshness(WorldObject obj, WDateTime now, SpoilageConfig config)
        {
            if (obj.ProducedAt is not { } producedAt)
                return 1.0;
            if (!config.RatePerHour.TryGetValue(obj.ItemKind, out var ratePerHour) || ratePerHour <= 0)
                return 1.0;

            var hours = (now - producedAt).TotalHours;
            if (hours <= 0) return 1.0;
            var freshness = 1.0 - hours * ratePerHour;
            return freshness < 0 ? 0 : freshness > 1 ? 1 : freshness;
        }

        /// <summary>True when the object has spoiled (freshness at or below zero) and is no longer edible.</summary>
        public static bool IsSpoiled(WorldObject obj, WDateTime now, SpoilageConfig config)
            => Freshness(obj, now, config) <= 0.0;
    }
}
