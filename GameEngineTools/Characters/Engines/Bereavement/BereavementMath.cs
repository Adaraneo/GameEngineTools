// BereavementMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Bereavement
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Pure, stateless math for the bereavement subsystem: trajectory assignment, onset intensity,
    /// the DPM loss/restoration oscillation, and trajectory decay rates.
    /// </summary>
    internal static class BereavementMath
    {
        /// <summary>
        /// Samples a grief trajectory from the configured prevalences, shifting mass toward
        /// <see cref="GriefTrajectory.Prolonged"/> for violent/sudden losses and for anxiously-attached
        /// mourners (a small, partly non-replicated modifier).
        /// </summary>
        internal static GriefTrajectory AssignTrajectory(
            IRandomSource rng, BereavementConfig cfg, double attachmentAnxiety, bool violent)
        {
            var resilient = Math.Max(0.0, cfg.ResilientWeight);
            var moderate = Math.Max(0.0, cfg.ModerateStableWeight);
            var recovery = Math.Max(0.0, cfg.RecoveryWeight);
            var prolonged = Math.Max(0.0, cfg.ProlongedWeight);

            // Violent/sudden losses raise PGD risk ~5× (Djelantik 2020 vs Lundorff 2017).
            if (violent && cfg.PgdBaseRateNonViolent > 1e-6)
                prolonged *= cfg.PgdBaseRateViolent / cfg.PgdBaseRateNonViolent;

            // Attachment anxiety adds a small amount of prolonged weight.
            prolonged += Math.Clamp(attachmentAnxiety, 0.0, 1.0) * cfg.AnxietyProlongedWeight;

            var total = resilient + moderate + recovery + prolonged;
            if (total <= 0.0)
                return GriefTrajectory.Resilient;

            var roll = rng.NextUnit() * total;

            if (roll < resilient) return GriefTrajectory.Resilient;
            roll -= resilient;
            if (roll < moderate) return GriefTrajectory.ModerateStable;
            roll -= moderate;
            if (roll < recovery) return GriefTrajectory.Recovery;
            return GriefTrajectory.Prolonged;
        }

        /// <summary>Acute grief intensity (0..100) at onset, from the lost bond strength and kin role.</summary>
        internal static double OnsetIntensity(double bondStrength, KinRole kinRole, BereavementConfig cfg)
        {
            var intensity = Math.Max(0.0, bondStrength) * cfg.OnsetIntensityFromBond;

            intensity += kinRole switch
            {
                KinRole.Partner => cfg.PartnerKinIntensityBonus,
                KinRole.Child => cfg.ChildKinIntensityBonus,
                KinRole.Parent or KinRole.Sibling or KinRole.Grandparent or KinRole.Grandchild => cfg.CloseKinIntensityBonus,
                _ => 0.0
            };

            return Math.Clamp(intensity, 0.0, 100.0);
        }

        /// <summary>Trajectory-specific grief-intensity decay rate, in points per day.</summary>
        internal static double DecayPerDay(GriefTrajectory trajectory, BereavementConfig cfg) => trajectory switch
        {
            GriefTrajectory.Resilient => cfg.ResilientDecayPerDay,
            GriefTrajectory.ModerateStable => cfg.ModerateStableDecayPerDay,
            GriefTrajectory.Recovery => cfg.RecoveryDecayPerDay,
            GriefTrajectory.Prolonged => cfg.ProlongedDecayPerDay,
            _ => cfg.ResilientDecayPerDay
        };

        /// <summary>
        /// The widowhood mortality-hazard multiplier (≥1) implied by a bereavement state: elevated in the
        /// acute window after a partner loss, tapering through a tail window, then 1.0. Male survivors are
        /// scaled by the male factor. Returns 1.0 when there is no active partner loss.
        /// </summary>
        internal static double WidowhoodHazardMultiplier(
            BereavementState? state, SexBiology biology, WDateTime now, BereavementConfig cfg)
        {
            if (state is not { Losses.Count: > 0 })
                return 1.0;

            var multiplier = 1.0;
            foreach (var loss in state.Losses)
            {
                if (loss.KinRole != KinRole.Partner)
                    continue;

                var daysSince = Math.Max(0.0, WDateTime.Difference(now, loss.OnsetTime).TotalDays);
                double m;
                if (daysSince <= cfg.WidowhoodFirstWindowDays)
                    m = cfg.WidowhoodHazardFirst;
                else if (daysSince <= cfg.WidowhoodTailWindowDays)
                    m = cfg.WidowhoodHazardTail;
                else
                    continue;

                if (biology == SexBiology.Male)
                    m *= cfg.WidowhoodMaleFactor;

                multiplier = Math.Max(multiplier, m);
            }

            return multiplier;
        }

        /// <summary>
        /// The Dual-Process-Model oscillator value (LoRo weight) at a given elapsed time since onset.
        /// Oscillates between 0 and a slowly-declining envelope with period
        /// <see cref="BereavementConfig.DpmPeriodDays"/> — producing "waves of grief" rather than a
        /// monotonic decline. 1 = full loss-orientation, 0 = full restoration-orientation.
        /// </summary>
        internal static double LoRoOscillation(double daysSinceOnset, BereavementConfig cfg)
        {
            var envelope = Math.Max(0.0, 1.0 - cfg.RestorationGrowthPerDay * Math.Max(0.0, daysSinceOnset));
            if (envelope <= 0.0)
                return 0.0;

            var period = Math.Max(1e-3, cfg.DpmPeriodDays);
            // cos starts at +1 (acute loss-orientation immediately after onset), then oscillates.
            var phase = 0.5 + 0.5 * Math.Cos(2.0 * Math.PI * daysSinceOnset / period);
            return Math.Clamp(envelope * phase, 0.0, 1.0);
        }
    }
}
