// BarterRules.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Production
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Engines.Economy;
    using GameEngineTools.World.Economy;

    /// <summary>One feasible barter: shop stock to take, paid with a held item, or with labor hours when <see cref="Give"/> is <c>null</c>.</summary>
    public sealed record BarterOffer(WorldObject Take, WorldObject? Give, double LaborHours);

    /// <summary>Shared by the behavior gate and the commit handler, so both agree on what a barter is.</summary>
    public static class BarterRules
    {
        /// <summary>
        /// The cheapest food in the shop's stock that the character can pay for: first with labor credit, otherwise with a held fresh
        /// item of another kind worth at least the price (its reference price times the shop buy-back fraction). <c>null</c> when none.
        /// </summary>
        public static BarterOffer? FindOffer(
            IEnumerable<WorldObject> available,
            IEnumerable<WorldObject>? held,
            double laborHours,
            EconomyConfig config,
            Func<WorldObject, bool> isFresh)
        {
            var stock = available
                .Where(o => ShopCounterFactory.IsStock(o) && o.Category == WorldObjectCategory.Food && o.Price is not null)
                .OrderBy(o => o.Price)
                .ToList();
            if (stock.Count == 0)
                return null;

            foreach (var take in stock)
            {
                var hours = take.Price!.Value * config.LaborHoursPerValue;
                if (laborHours >= hours)
                    return new BarterOffer(take, null, hours);
            }

            if (held is null)
                return null;

            foreach (var give in held
                         .Where(h => h.Category is WorldObjectCategory.Food or WorldObjectCategory.Drink && isFresh(h))
                         .OrderBy(h => EconomyLedger.ReferencePrice(h.ItemKind)))
            {
                var worth = EconomyLedger.ReferencePrice(give.ItemKind) * config.ShopBuyBackFraction;
                var take = stock.FirstOrDefault(s => s.ItemKind != give.ItemKind && s.Price!.Value <= worth);
                if (take is not null)
                    return new BarterOffer(take, give, 0.0);
            }

            return null;
        }
    }
}
