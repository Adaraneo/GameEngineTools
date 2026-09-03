// EconomyLedger.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Economy
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Engines.Economy;
    using GameEngineTools.World.Objects;

    /// <summary>
    /// Scene-level singleton tracking per-shop, per-item-kind posted prices with stock feedback.
    /// Adjusted from the object-interaction commit path after each buy/sell/restock — same
    /// scene-aggregate pattern as <c>StatusLedger</c>/<c>CommunityReputationLedger</c>.
    /// </summary>
    /// <remarks>
    /// Price formation: posted-price with stock feedback (Tier 2 decision §1.2), NOT a Sugarscape MRS
    /// double-auction. Gode &amp; Sunder (1993, <i>JPE</i> 101(1):119-137, full title "Allocative
    /// Efficiency of Markets with Zero-Intelligence Traders: Market as a Partial Substitute for
    /// Individual Rationality") establish that market <b>institutions</b>, not agent rationality, carry
    /// most allocative efficiency — a simple institutional stock-feedback rule is a defensible, cheap,
    /// LOD-friendly substitute for a full auction mechanism. The elasticity coefficient is a tuned
    /// game-balance parameter, not a citable economic constant.
    /// </remarks>
    public sealed class EconomyLedger
    {
        #region Public API

        /// <summary>
        /// Current posted price for a shop's stock of a given item kind. On a cache miss (e.g. a
        /// freshly constructed ledger after a process restart), <paramref name="persistedFallback"/> —
        /// the caller's already-resolved persisted price for this shop/kind, if it has one — is
        /// preferred over the hardcoded <see cref="SeedPrice"/> constant.
        /// </summary>
        public double GetPrice(string shopId, PickupItemKind kind, double? persistedFallback = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(shopId);
            return _prices.TryGetValue((shopId, kind), out var price) ? price : persistedFallback ?? SeedPrice(kind);
        }

        /// <summary>
        /// Adjusts the price for a shop/item-kind pair based on the current stock level relative to the
        /// configured target band, and returns the new price. Called after every buy/sell/restock — NOT
        /// on a per-tick timer, to avoid an O(shops×kinds) sweep (LOD discipline, §6/§7).
        /// </summary>
        /// <remarks>
        /// Below target → price rises (scarcity); above target → price falls (glut). Clamped to
        /// [<see cref="EconomyConfig.MinPrice"/>, <see cref="EconomyConfig.MaxPrice"/>]. The elasticity
        /// coefficient is a tuned game-balance parameter, not a literature-derived formula.
        /// </remarks>
        public double AdjustPriceForStockChange(string shopId, PickupItemKind kind, int newStockCount, EconomyConfig config)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(shopId);
            ArgumentNullException.ThrowIfNull(config);

            var current = GetPrice(shopId, kind);
            var delta = (config.TargetStockLevel - newStockCount) * config.PriceElasticityPerUnit;
            var adjusted = Math.Clamp(current + delta, config.MinPrice, config.MaxPrice);

            _prices[(shopId, kind)] = adjusted;
            return adjusted;
        }

        #endregion Public API

        #region Private state

        private readonly Dictionary<(string ShopId, PickupItemKind Kind), double> _prices = new();

        /// <summary>Fallback starting price for a shop/kind pair the ledger has not yet transacted.</summary>
        private static double SeedPrice(PickupItemKind kind) => kind switch
        {
            PickupItemKind.Bread => 2.0,
            PickupItemKind.Cheese => 3.0,
            PickupItemKind.Flour => 1.5,
            PickupItemKind.Grain => 1.0,
            PickupItemKind.Milk => 1.0,
            PickupItemKind.Food => 2.0,
            PickupItemKind.Drink => 1.0,
            _ => 1.0,
        };

        #endregion Private state
    }
}
