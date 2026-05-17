// NaturalMortalityCalculator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using System;

    /// <summary>
    /// Pure static calculator for natural mortality risk.
    /// No DI — all inputs come from <see cref="PhysiologyState"/> and <see cref="PhysiologyConfig"/>.
    /// </summary>
    internal static class NaturalMortalityCalculator
    {
        /// <summary>
        /// Computes the hourly probability of natural death given the current physiological state.
        /// </summary>
        /// <param name="s">Current physiology state.</param>
        /// <param name="ageYears">Character age in game years.</param>
        /// <param name="cfg">Engine configuration supplying mortality curve parameters.</param>
        /// <returns>Hourly mortality risk in [0, <see cref="PhysiologyConfig.NaturalMortalityMaxRiskPerHour"/>].</returns>
        internal static double ComputeHourlyRisk(PhysiologyState s, int ageYears, PhysiologyConfig cfg)
        {
            var risk = 0.0;

            // Age: Gompertz curve — exponential rise past NaturalMortalityGompertzStart
            if (ageYears >= cfg.NaturalMortalityGompertzStart)
            {
                var yearsAbove = ageYears - cfg.NaturalMortalityGompertzStart;
                risk += 0.0001 * Math.Exp(cfg.NaturalMortalityGompertzScale * yearsAbove);
            }

            // AllostaticLoad: linear above 70; dramatic spike above 90
            if (s.AllostaticLoad > 90)
                risk += cfg.NaturalMortalityAlloWeight * (s.AllostaticLoad - 70) * 3.0;
            else if (s.AllostaticLoad > 70)
                risk += cfg.NaturalMortalityAlloWeight * (s.AllostaticLoad - 70);

            // ImmuneLoad: systemic failure above 75
            if (s.ImmuneLoad > 75)
                risk += cfg.NaturalMortalityImmuneWeight * (s.ImmuneLoad - 75);

            // Starvation: terminal hunger + thirst → direct mortality contribution
            if (s.Hunger >= 95 && s.Thirst >= 95)
                risk += 0.0004;

            // Exhaustion: extreme energy depletion with sleep debt
            if (s.Energy < 5 && s.SleepDebtHours >= 48)
                risk += 0.0005;

            // BoneDensity: fragility fracture risk (osteoporotic mortality)
            if (s.Aging is { BoneDensity: < 0.25 } bone)
                risk += 0.0002 * (0.25 - bone.BoneDensity) / 0.25;

            // MuscleMassFraction: sarcopenic frailty
            if (s.Aging is { } a && a.MuscleMassFraction < cfg.SarcopeniaMuscleMin * 1.2)
                risk += 0.0001 * (cfg.SarcopeniaMuscleMin * 1.2 - a.MuscleMassFraction);

            return Math.Clamp(risk, 0, cfg.NaturalMortalityMaxRiskPerHour);
        }

        /// <summary>
        /// Resolves the most appropriate <see cref="DeathCause"/> from the physiological state.
        /// Priority order: <see cref="DeathCause.Starvation"/> → <see cref="DeathCause.Exhaustion"/>
        /// → <see cref="DeathCause.SystemicFailure"/> → <see cref="DeathCause.OldAge"/>.
        /// </summary>
        internal static DeathCause ResolveCause(PhysiologyState s, int ageYears)
        {
            if (s.Hunger >= 95 && s.Thirst >= 95)
                return DeathCause.Starvation;

            if (s.Energy < 2 && s.SleepDebtHours >= 48)
                return DeathCause.Exhaustion;

            if (s.AllostaticLoad >= 90 || s.ImmuneLoad >= 85)
                return DeathCause.SystemicFailure;

            return DeathCause.OldAge;
        }
    }
}
