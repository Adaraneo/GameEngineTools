// WaterMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using System;

    /// <summary>
    /// Pure body-water balance: the deficit in litres, the thirst score as its view, the loss rate that falls as the kidneys conserve, and the lethal deficit.
    /// Design and sources: docs/research/body-water-design.md; every coefficient is an estimate, see <see cref="PhysiologyConfig"/>.
    /// </summary>
    internal static class WaterMath
    {
        /// <summary>The thirst score (0..100) that a deficit in litres shows as, when nothing is waiting in the stomach.</summary>
        internal static double ViewThirst(double deficitL, PhysiologyConfig cfg)
            => ViewThirst(deficitL, 0.0, cfg);

        /// <summary>The thirst score (0..100): the deficit less the water already drunk and still in the stomach (it relieves thirst before it is absorbed).</summary>
        internal static double ViewThirst(double deficitL, double gutL, PhysiologyConfig cfg)
            => Math.Clamp((deficitL - gutL) / cfg.LitresPerThirstPoint, 0.0, 100.0);

        /// <summary>The deficit expressed in thirst points, without the relief of what is still in the stomach: what sweating and the body's state depend on.</summary>
        internal static double DeficitPoints(double deficitL, PhysiologyConfig cfg)
            => Math.Clamp(deficitL / cfg.LitresPerThirstPoint, 0.0, 100.0);

        /// <summary>Litres that leave the stomach in a time, at most what is in it.</summary>
        internal static double Emptied(double gutL, double hours, PhysiologyConfig cfg)
            => Math.Min(Math.Max(0.0, gutL), Math.Max(0.0, cfg.WaterGastricEmptyingLitresPerHour) * hours);

        /// <summary>The deficit in litres: the stored one while the thirst score still matches it, else the score read as litres (someone else changed the score).</summary>
        internal static double Deficit(PhysiologyState s, PhysiologyConfig cfg)
        {
            if (s.WaterDeficitL is { } stored && Math.Abs(ViewThirst(stored, s.GutWaterL, cfg) - s.Thirst) <= 0.05)
                return stored;
            return s.Thirst * cfg.LitresPerThirstPoint + s.GutWaterL;
        }

        /// <summary>1 while the body still lets freely drunk water out as urine, falling to 0 as the deficit grows from the start to the full level.</summary>
        internal static double Conservation(double deficitL, PhysiologyConfig cfg)
        {
            var span = Math.Max(1e-6, cfg.WaterConservationFullLitres - cfg.WaterConservationStartLitres);
            return Math.Clamp(1.0 - (deficitL - cfg.WaterConservationStartLitres) / span, 0.0, 1.0);
        }

        /// <summary>Thirst points per hour lost without sweat: the obligatory loss plus the rest of the free rate, scaled by conservation.</summary>
        internal static double LossPointsPerHour(bool asleep, double deficitL, PhysiologyConfig cfg)
        {
            var free = asleep ? cfg.ThirstDriftSleepPerHour : cfg.ThirstDriftAwakePerHour;
            var obligatory = Math.Min(free, (asleep ? cfg.WaterObligatoryLossSleepLitresPerHour : cfg.WaterObligatoryLossLitresPerHour) / cfg.LitresPerThirstPoint);
            return obligatory + Conservation(deficitL, cfg) * (free - obligatory);
        }

        /// <summary>Deficit in litres at which death becomes possible: a share of the body mass (one litre of water is about one kilogram).</summary>
        internal static double LethalDeficitL(PhysiologyConfig cfg)
            => cfg.BodyMassKg * cfg.DehydrationLethalBodyMassPercent / 100.0;

        internal static bool IsLethal(PhysiologyState s, PhysiologyConfig cfg)
            => Deficit(s, cfg) >= LethalDeficitL(cfg);
    }
}
