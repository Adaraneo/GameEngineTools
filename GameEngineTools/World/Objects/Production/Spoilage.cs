// Spoilage.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Production
{
    using GameEngineTools.World.Utils.Time;
    using GameEngineTools.World.Work;

    /// <summary>
    /// Computes food freshness lazily from <c>WorldObject.ProducedAt</c> and the spoilage time of the object type (<c>spoilageHours</c> in <c>objecttypes.json</c>).
    /// A simplified linear model, not Arrhenius kinetics: fidelity beyond believability is out of scope. The rates are design picks (docs/research/food-economy-research-findings.md section 3).
    /// </summary>
    public static class Spoilage
    {
        /// <summary>
        /// Freshness in [0..1] at <paramref name="now"/>: 1.0 when just produced, 0.0 when spoiled.
        /// Objects without a <c>ProducedAt</c>, or of a type that does not spoil, never spoil (returns 1.0).
        /// </summary>
        public static double Freshness(WorldObject obj, WDateTime now, WorkCatalog catalog)
        {
            if (obj.ProducedAt is not { } producedAt)
                return 1.0;
            var ratePerHour = catalog.SpoilagePerHour(obj.TypeId);
            if (ratePerHour <= 0)
                return 1.0;

            var hours = (now - producedAt).TotalHours;
            if (hours <= 0) return 1.0;
            var freshness = 1.0 - hours * ratePerHour;
            return freshness < 0 ? 0 : freshness > 1 ? 1 : freshness;
        }

        /// <summary>True when the object has spoiled (freshness at or below zero) and is no longer edible.</summary>
        public static bool IsSpoiled(WorldObject obj, WDateTime now, WorkCatalog catalog)
            => Freshness(obj, now, catalog) <= 0.0;
    }
}
