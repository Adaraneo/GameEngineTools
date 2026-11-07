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

            // Age: Gompertz curve — exponential rise past NaturalMortalityGompertzStart.
            if (ageYears >= cfg.NaturalMortalityGompertzStart)
            {
                var yearsAbove = ageYears - cfg.NaturalMortalityGompertzStart;
                risk += cfg.NaturalMortalityAgeBaseline * Math.Exp(cfg.NaturalMortalityGompertzScale * yearsAbove);
            }

            // AllostaticLoad: chronic HPA burden — linear risk factor above the threshold,
            // with an acute-decompensation spike above the spike threshold.
            if (s.AllostaticLoad > cfg.NaturalMortalityAlloThreshold)
            {
                risk += cfg.NaturalMortalityAlloWeight * (s.AllostaticLoad - cfg.NaturalMortalityAlloThreshold);
                if (s.AllostaticLoad > cfg.NaturalMortalityAlloSpikeThreshold)
                    risk += cfg.NaturalMortalityAlloWeight * cfg.NaturalMortalityAlloSpikeMultiplier
                            * (s.AllostaticLoad - cfg.NaturalMortalityAlloSpikeThreshold);
            }

            // ImmuneLoad: systemic infection — same linear + acute-spike shape.
            if (s.ImmuneLoad > cfg.NaturalMortalityImmuneThreshold)
            {
                risk += cfg.NaturalMortalityImmuneWeight * (s.ImmuneLoad - cfg.NaturalMortalityImmuneThreshold);
                if (s.ImmuneLoad > cfg.NaturalMortalityImmuneSpikeThreshold)
                    risk += cfg.NaturalMortalityImmuneWeight * cfg.NaturalMortalityImmuneSpikeMultiplier
                            * (s.ImmuneLoad - cfg.NaturalMortalityImmuneSpikeThreshold);
            }

            // Dehydration: terminal thirst kills within days, independent of hunger.
            if (s.Thirst >= cfg.NaturalMortalityDehydrationThreshold)
                risk += cfg.NaturalMortalityDehydrationRisk;

            // Starvation: terminal hunger kills within weeks.
            if (s.Hunger >= cfg.NaturalMortalityStarvationThreshold)
                risk += cfg.NaturalMortalityStarvationRisk;

            // Exhaustion: extreme energy depletion with sustained sleep debt.
            if (s.Energy <= cfg.NaturalMortalityExhaustionEnergyMax && s.SleepDebtHours >= cfg.NaturalMortalityExhaustionSleepDebtMin)
                risk += cfg.NaturalMortalityExhaustionRisk;

            // BoneDensity: fragility-fracture (osteoporotic) mortality.
            if (s.Aging is { } bone && bone.BoneDensity < cfg.NaturalMortalityBoneFragilityThreshold)
                risk += cfg.NaturalMortalityBoneFragilityWeight
                        * (cfg.NaturalMortalityBoneFragilityThreshold - bone.BoneDensity) / cfg.NaturalMortalityBoneFragilityThreshold;

            // MuscleMassFraction: sarcopenic frailty.
            if (s.Aging is { } a && a.MuscleMassFraction < cfg.SarcopeniaMuscleMin * 1.2)
                risk += cfg.NaturalMortalitySarcopeniaWeight * (cfg.SarcopeniaMuscleMin * 1.2 - a.MuscleMassFraction);

            return Math.Clamp(risk, 0, cfg.NaturalMortalityMaxRiskPerHour);
        }

        /// <summary>
        /// Resolves the <see cref="DeathCause"/> from the risk source contributing most to the hourly hazard.
        /// Age, bone and muscle frailty map to <see cref="DeathCause.OldAge"/>; allostatic and immune load to
        /// <see cref="DeathCause.SystemicFailure"/>.
        /// </summary>
        internal static DeathCause ResolveCause(PhysiologyState s, int ageYears, PhysiologyConfig cfg)
        {
            var cause = DeathCause.OldAge;
            var best = AgeFrailtyRisk(s, ageYears, cfg);

            void Consider(DeathCause candidate, double risk)
            {
                if (risk <= best) return;
                best = risk;
                cause = candidate;
            }

            Consider(DeathCause.SystemicFailure, SystemicRisk(s, cfg));
            Consider(DeathCause.Starvation, s.Hunger >= cfg.NaturalMortalityStarvationThreshold ? cfg.NaturalMortalityStarvationRisk : 0.0);
            Consider(DeathCause.Dehydration, s.Thirst >= cfg.NaturalMortalityDehydrationThreshold ? cfg.NaturalMortalityDehydrationRisk : 0.0);
            Consider(DeathCause.Exhaustion,
                s.Energy <= cfg.NaturalMortalityExhaustionEnergyMax && s.SleepDebtHours >= cfg.NaturalMortalityExhaustionSleepDebtMin
                    ? cfg.NaturalMortalityExhaustionRisk : 0.0);
            return cause;
        }

        private static double AgeFrailtyRisk(PhysiologyState s, int ageYears, PhysiologyConfig cfg)
        {
            var risk = 0.0;
            if (ageYears >= cfg.NaturalMortalityGompertzStart)
                risk += cfg.NaturalMortalityAgeBaseline * Math.Exp(cfg.NaturalMortalityGompertzScale * (ageYears - cfg.NaturalMortalityGompertzStart));
            if (s.Aging is { } bone && bone.BoneDensity < cfg.NaturalMortalityBoneFragilityThreshold)
                risk += cfg.NaturalMortalityBoneFragilityWeight
                        * (cfg.NaturalMortalityBoneFragilityThreshold - bone.BoneDensity) / cfg.NaturalMortalityBoneFragilityThreshold;
            if (s.Aging is { } a && a.MuscleMassFraction < cfg.SarcopeniaMuscleMin * 1.2)
                risk += cfg.NaturalMortalitySarcopeniaWeight * (cfg.SarcopeniaMuscleMin * 1.2 - a.MuscleMassFraction);
            return risk;
        }

        private static double SystemicRisk(PhysiologyState s, PhysiologyConfig cfg)
        {
            var risk = 0.0;
            if (s.AllostaticLoad > cfg.NaturalMortalityAlloThreshold)
            {
                risk += cfg.NaturalMortalityAlloWeight * (s.AllostaticLoad - cfg.NaturalMortalityAlloThreshold);
                if (s.AllostaticLoad > cfg.NaturalMortalityAlloSpikeThreshold)
                    risk += cfg.NaturalMortalityAlloWeight * cfg.NaturalMortalityAlloSpikeMultiplier
                            * (s.AllostaticLoad - cfg.NaturalMortalityAlloSpikeThreshold);
            }
            if (s.ImmuneLoad > cfg.NaturalMortalityImmuneThreshold)
            {
                risk += cfg.NaturalMortalityImmuneWeight * (s.ImmuneLoad - cfg.NaturalMortalityImmuneThreshold);
                if (s.ImmuneLoad > cfg.NaturalMortalityImmuneSpikeThreshold)
                    risk += cfg.NaturalMortalityImmuneWeight * cfg.NaturalMortalityImmuneSpikeMultiplier
                            * (s.ImmuneLoad - cfg.NaturalMortalityImmuneSpikeThreshold);
            }
            return risk;
        }
    }
}
