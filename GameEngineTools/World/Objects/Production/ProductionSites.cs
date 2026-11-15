// ProductionSites.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Production
{
    using System;
    using System.Linq;
    using GameEngineTools.World.Work;

    /// <summary>
    /// What kind of production fixture an object is. A <b>raw site</b> (field, pasture) carries a Production affordance and names the item it yields in <see cref="WorldObject.ItemTypeId"/>.
    /// A <b>workstation</b> (mill, oven, kiln) is an object of a catalog type that craft tasks name as their station; it may serve several tasks, so it is not identified by a product.
    /// </summary>
    public static class ProductionSites
    {
        /// <summary>True for an object with a Production affordance (a raw site or a workstation).</summary>
        public static bool HasProduction(WorldObject o) => o.Affordances.Any(a => a.Type == AffordanceType.Production);

        /// <summary>A workstation: a production object whose type some craft task names as its station.</summary>
        public static bool IsStation(WorkCatalog catalog, WorldObject o) => HasProduction(o) && catalog.IsStation(o.TypeId);

        /// <summary>A raw site: a production object that is not a workstation.</summary>
        public static bool IsRawSite(WorkCatalog catalog, WorldObject o) => HasProduction(o) && !catalog.IsStation(o.TypeId);

        /// <summary>Outputs one action makes here: the Production affordance, rounded, at least 1.</summary>
        public static int Yield(WorldObject o)
            => Math.Max(1, (int)Math.Round(o.Affordances.Where(a => a.Type == AffordanceType.Production).Select(a => a.Satisfaction).DefaultIfEmpty(1.0).First()));
    }
}
