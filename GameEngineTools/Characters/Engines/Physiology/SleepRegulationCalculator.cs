// SleepRegulationCalculator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using System;

    /// <summary>
    /// Pure, side-effect-free implementation of the Borbély (1982) two-process sleep model and the
    /// Van Dongen (2003) cognitive dose-response. Process S is a saturating homeostatic pressure;
    /// Process C is a circadian threshold; sleep propensity is the <i>distance</i> of S above the
    /// C-modulated threshold (subtractive, not additive). Cognitive impairment is a <i>separate</i>
    /// accumulator that — unlike S — does not saturate, reproducing the homeostatic/behavioural
    /// dissociation under chronic restriction.
    /// </summary>
    /// <remarks>
    /// Modelled as static functions (mirroring <see cref="NaturalMortalityCalculator"/>) so the
    /// dynamics can be unit-tested exhaustively and reused by the engine without DI.
    /// Source: Borbély (1982); Daan, Beersma &amp; Borbély (1984); Van Dongen et al. (2003).
    /// </remarks>
    public static class SleepRegulationCalculator
    {
        #region Process S (homeostatic)

        /// <summary>
        /// Advances Process S while awake — a saturating exponential rise toward the upper asymptote
        /// with time constant <see cref="PhysiologyConfig.ProcessSBuildupTimeConstantHours"/>.
        /// </summary>
        /// <param name="s">Current Process S [0..1].</param>
        /// <param name="dtHours">Elapsed awake hours.</param>
        /// <param name="cfg">Physiology configuration.</param>
        /// <returns>The new Process S.</returns>
        public static double BuildupProcessS(double s, double dtHours, PhysiologyConfig cfg)
        {
            ArgumentNullException.ThrowIfNull(cfg);
            var a = cfg.ProcessSUpperAsymptote;
            return a - (a - s) * Math.Exp(-Math.Max(0, dtHours) / cfg.ProcessSBuildupTimeConstantHours);
        }

        /// <summary>
        /// Advances Process S while asleep — an exponential decay toward the lower asymptote with the
        /// (faster) time constant <see cref="PhysiologyConfig.ProcessSDecayTimeConstantHours"/>.
        /// </summary>
        /// <param name="s">Current Process S [0..1].</param>
        /// <param name="dtHours">Elapsed sleep hours.</param>
        /// <param name="cfg">Physiology configuration.</param>
        /// <returns>The new Process S.</returns>
        public static double DecayProcessS(double s, double dtHours, PhysiologyConfig cfg)
        {
            ArgumentNullException.ThrowIfNull(cfg);
            var a = cfg.ProcessSLowerAsymptote;
            return a + (s - a) * Math.Exp(-Math.Max(0, dtHours) / cfg.ProcessSDecayTimeConstantHours);
        }

        #endregion

        #region Sleep debt (derived from Process S)

        // A normal day is awake for two thirds of it, bounded to what a body tolerates (short days still
        // need ~8 h awake to build any pressure; very long days are not stayed awake for).
        private const double AwakeFractionOfDay = 2.0 / 3.0;
        private const double MinAwakeHours = 8.0;
        private const double MaxAwakeHours = 18.0;

        /// <summary>
        /// Process S level at the end of a normal waking day: the steady state of alternating
        /// <see cref="PhysiologyConfig.ProcessSBuildupTimeConstantHours"/> buildup while awake and
        /// <see cref="PhysiologyConfig.ProcessSDecayTimeConstantHours"/> decay while asleep, for a day of
        /// <paramref name="hoursPerDay"/> hours. Day length is configurable per world, so this is derived
        /// rather than a constant (≈0.62 for 24 h, ≈0.65 for 26 h); <see cref="PhysiologyConfig.ProcessSDebtFreeLevel"/> overrides it.
        /// </summary>
        /// <param name="cfg">Physiology configuration.</param>
        /// <param name="hoursPerDay">Hours in a world day.</param>
        /// <returns>The Process S level below which no sleep debt is counted.</returns>
        public static double DebtFreeLevel(PhysiologyConfig cfg, double hoursPerDay)
        {
            ArgumentNullException.ThrowIfNull(cfg);
            if (cfg.ProcessSDebtFreeLevel is { } fixedLevel)
                return fixedLevel;

            var awake = Math.Clamp(hoursPerDay * AwakeFractionOfDay, MinAwakeHours, MaxAwakeHours);
            var asleep = awake * (1.0 - AwakeFractionOfDay) / AwakeFractionOfDay;
            var rise = Math.Exp(-awake / cfg.ProcessSBuildupTimeConstantHours);
            var fall = Math.Exp(-asleep / cfg.ProcessSDecayTimeConstantHours);
            var upper = cfg.ProcessSUpperAsymptote;
            var lower = cfg.ProcessSLowerAsymptote;

            // Evening level S_e of the cycle S_e = U - (U - S_w)·rise, S_w = L + (S_e - L)·fall.
            return (upper - rise * (upper - lower) - rise * fall * lower) / (1.0 - rise * fall);
        }

        /// <summary>
        /// Sleep-debt hours implied by Process S: zero up to <see cref="DebtFreeLevel"/> (a normal waking
        /// day), rising linearly to <see cref="PhysiologyConfig.MaxSleepDebtHours"/> at the upper asymptote
        /// (prolonged wakefulness).
        /// </summary>
        /// <param name="processS">Current Process S [0..1].</param>
        /// <param name="cfg">Physiology configuration.</param>
        /// <param name="hoursPerDay">Hours in a world day.</param>
        /// <returns>Sleep debt in hours, in [0, <see cref="PhysiologyConfig.MaxSleepDebtHours"/>].</returns>
        public static double DebtHoursFromProcessS(double processS, PhysiologyConfig cfg, double hoursPerDay)
        {
            var free = DebtFreeLevel(cfg, hoursPerDay);
            var span = Math.Max(1e-6, cfg.ProcessSUpperAsymptote - free);
            return Math.Clamp((processS - free) / span, 0.0, 1.0) * cfg.MaxSleepDebtHours;
        }

        /// <summary>Inverse of <see cref="DebtHoursFromProcessS"/>; seeds Process S for characters restored with only a debt value.</summary>
        /// <param name="debtHours">Sleep debt in hours.</param>
        /// <param name="cfg">Physiology configuration.</param>
        /// <param name="hoursPerDay">Hours in a world day.</param>
        /// <returns>The Process S value that yields <paramref name="debtHours"/>.</returns>
        public static double ProcessSFromDebtHours(double debtHours, PhysiologyConfig cfg, double hoursPerDay)
        {
            var free = DebtFreeLevel(cfg, hoursPerDay);
            var fraction = Math.Clamp(debtHours / Math.Max(1.0, cfg.MaxSleepDebtHours), 0.0, 1.0);
            return Math.Clamp(free + fraction * (cfg.ProcessSUpperAsymptote - free), 0.0, 1.0);
        }

        #endregion

        #region Process C (circadian) + sleep propensity

        /// <summary>
        /// Returns the Process C circadian sleep <i>threshold</i> at the given hour of day. The
        /// threshold is highest in the afternoon (hardest to fall asleep) and lowest at the circadian
        /// trough at night (easiest), shifted by the character's circadian phase.
        /// </summary>
        /// <param name="hourOfDay">Local hour of day.</param>
        /// <param name="hoursPerDay">Hours per world day.</param>
        /// <param name="circadianPhaseShiftHours">The character's circadian phase offset (chronotype + jet lag).</param>
        /// <param name="cfg">Physiology configuration.</param>
        /// <returns>The circadian threshold value in the same scale as Process S.</returns>
        public static double CircadianThreshold(
            double hourOfDay, double hoursPerDay, double circadianPhaseShiftHours, PhysiologyConfig cfg)
        {
            ArgumentNullException.ThrowIfNull(cfg);
            var mid = (cfg.ProcessCUpperThreshold + cfg.ProcessCLowerThreshold) / 2.0;
            var amp = (cfg.ProcessCUpperThreshold - cfg.ProcessCLowerThreshold) / 2.0;
            var peakHour = cfg.ProcessCPeakHour * hoursPerDay / 24.0 + circadianPhaseShiftHours;
            var phase = (hourOfDay - peakHour) * 2.0 * Math.PI / hoursPerDay;
            return mid + amp * Math.Cos(phase);
        }

        /// <summary>
        /// Sleep propensity = distance of Process S above the Process C threshold. Positive values
        /// mean the homeostatic pressure exceeds the circadian gate — the character is sleepy.
        /// This threshold/subtractive form (not pure additive) is the defining feature of the
        /// two-process model.
        /// </summary>
        /// <param name="processS">Current Process S.</param>
        /// <param name="circadianThreshold">Current Process C threshold (see <see cref="CircadianThreshold"/>).</param>
        /// <returns>Sleep propensity (can be negative when the circadian gate dominates).</returns>
        public static double SleepPropensity(double processS, double circadianThreshold)
            => processS - circadianThreshold;

        /// <summary>
        /// Sleep propensity at a given moment: Process S minus the Process C threshold for the time of day.
        /// </summary>
        /// <param name="processS">Current Process S [0..1].</param>
        /// <param name="hourOfDay">Fractional world hour of the day.</param>
        /// <param name="hoursPerDay">Hours in a world day.</param>
        /// <param name="circadianPhaseShiftHours">The character's circadian phase offset (chronotype + jet lag).</param>
        /// <param name="cfg">Physiology configuration.</param>
        /// <returns>Sleep propensity (negative while the circadian gate dominates).</returns>
        public static double ComputeSleepPropensity(
            double processS, double hourOfDay, double hoursPerDay, double circadianPhaseShiftHours, PhysiologyConfig cfg)
            => SleepPropensity(processS, CircadianThreshold(hourOfDay, hoursPerDay, circadianPhaseShiftHours, cfg));

        #endregion

        #region Van Dongen cognitive deficit

        /// <summary>
        /// Advances the behavioural cognitive-performance deficit. While awake it accrues (faster when
        /// Process S is above the restriction threshold); while asleep it recovers. Crucially it has no
        /// asymptote, so chronic restriction (e.g. 6 h/night) produces monotonically growing impairment
        /// even after Process S has saturated. The 6 h-restriction ≈ 1-night-total-deprivation anchor
        /// from Van Dongen et al. (2003) governs the accrual/recovery balance.
        /// </summary>
        /// <param name="deficit">Current cognitive deficit [0..~1].</param>
        /// <param name="processS">Current Process S [0..1].</param>
        /// <param name="dtHours">Elapsed hours this tick.</param>
        /// <param name="asleep">True if the character is currently asleep.</param>
        /// <param name="cfg">Physiology configuration.</param>
        /// <returns>The new cognitive deficit (clamped to ≥ 0).</returns>
        public static double UpdateCognitiveDeficit(
            double deficit, double processS, double dtHours, bool asleep, PhysiologyConfig cfg)
        {
            ArgumentNullException.ThrowIfNull(cfg);
            var dt = Math.Max(0, dtHours);
            if (asleep)
                return Math.Max(0.0, deficit - cfg.CognitiveDeficitRecoveryPerSleepHour * dt);

            var restrictionFactor = 1.0 + Math.Max(0.0, processS - cfg.CognitiveDeficitRestrictionThreshold);
            return deficit + cfg.CognitiveDeficitAccumPerHour * restrictionFactor * dt;
        }

        #endregion
    }
}
