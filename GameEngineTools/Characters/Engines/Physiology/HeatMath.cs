// HeatMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using System;

    /// <summary>
    /// Pure heat balance of one character: heat production, sweat, heat storage and the heatstroke hazard.
    /// One-node model (variant A in docs/research/thermoregulation.md); every coefficient is an estimate, see <see cref="PhysiologyConfig"/>.
    /// </summary>
    internal static class HeatMath
    {
        /// <summary>Heat production in load units: the activity factor (1 = rest) raised by the air temperature above the comfort point.</summary>
        internal static double HeatProduction(double activity, double airC, PhysiologyConfig cfg)
            => activity * (1.0 + cfg.HeatLoadPerDegreeAboveComfort * Math.Max(0.0, airC - cfg.HeatComfortCelsius));

        /// <summary>Sweat (litres per hour) the body would make for that heat production, capped at the physiological maximum.</summary>
        internal static double PotentialSweat(double production, PhysiologyConfig cfg)
            => Math.Min(cfg.SweatMaxLitresPerHour, cfg.SweatLitresPerHourPerLoadUnit * Math.Max(0.0, production - 1.0));

        /// <summary>Share of the sweat that still cools: 1 until the thirst start, falling linearly to 0 at the thirst end.</summary>
        internal static double CoolingEfficiency(double thirst, PhysiologyConfig cfg)
        {
            var span = Math.Max(1e-6, cfg.SweatFailThirstEnd - cfg.SweatFailThirstStart);
            return Math.Clamp(1.0 - (thirst - cfg.SweatFailThirstStart) / span, 0.0, 1.0);
        }

        /// <summary>Thirst points per hour added by sweating.</summary>
        internal static double SweatThirstPerHour(double potentialSweat, PhysiologyConfig cfg)
            => potentialSweat / cfg.LitresPerThirstPoint;

        /// <summary>Heat storage (°C) the body tends to: the heat production above rest that the sweat does not carry away.</summary>
        internal static double HeatLoadTarget(double production, double thirst, PhysiologyConfig cfg)
        {
            var excess = Math.Max(0.0, production - 1.0);
            if (excess <= 0.0) return 0.0;
            var cooled = cfg.SweatLitresPerHourPerLoadUnit > 0.0
                ? PotentialSweat(production, cfg) * CoolingEfficiency(thirst, cfg) / cfg.SweatLitresPerHourPerLoadUnit
                : 0.0;
            return Math.Max(0.0, excess - cooled);
        }

        /// <summary>Moves the stored heat toward its target over <paramref name="h"/> hours (exponential, time constant from the config).</summary>
        internal static double AdvanceHeatLoad(double heatLoad, double target, double h, PhysiologyConfig cfg)
        {
            var step = 1.0 - Math.Exp(-Math.Max(0.0, h) / Math.Max(1e-6, cfg.HeatLoadTimeConstantHours));
            return Math.Clamp(heatLoad + (target - heatLoad) * step, 0.0, cfg.HeatLoadMax);
        }

        /// <summary>Hourly heatstroke death risk from the core temperature excess (body temperature deviation plus stored heat); rises linearly from onset to full and with age over 50.</summary>
        internal static double HeatstrokeRisk(double coreExcessC, int ageYears, PhysiologyConfig cfg)
        {
            if (coreExcessC <= cfg.HeatstrokeOnsetC) return 0.0;
            var span = Math.Max(1e-6, cfg.HeatstrokeFullC - cfg.HeatstrokeOnsetC);
            var frac = Math.Min(1.0, (coreExcessC - cfg.HeatstrokeOnsetC) / span);
            var age = 1.0 + cfg.HeatstrokeAgeRiskPerYearOver50 * Math.Max(0, ageYears - 50);
            return cfg.HeatstrokeMaxRiskPerHour * frac * age;
        }
    }
}
