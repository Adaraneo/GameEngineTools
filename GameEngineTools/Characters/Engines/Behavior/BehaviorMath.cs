// BehaviorMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using static ActionNames;

    /// <summary>
    /// Shared deterministic formulas reused across need, modifier, sleep, and orchestration code.
    /// </summary>
    internal static class BehaviorMath
    {
        #region Generic helpers

        internal static double Util(double need, double weight) => need * (0.5 + weight);

        internal static double Clamp01p(double v) => Math.Clamp(v, 0, 100);

        internal static Dictionary<string, double> UpdateCooldowns(IReadOnlyDictionary<string, double>? cooldowns, double hours) => (cooldowns ?? new Dictionary<string, double>()).ToDictionary(kv => kv.Key, kv => Math.Max(0, kv.Value - hours));

        internal static double CooldownFor(IReadOnlyDictionary<string, double> cooldowns, string action) => cooldowns.TryGetValue(action, out var v) ? v : 0;

        #endregion Generic helpers

        #region Need formulas

        internal static BehaviorState ComputeNeedState(IHumanContext ctx, IReadOnlyDictionary<string, double> cooldowns, BehaviorState state)
        {
            var ph = ctx.Snapshot.Physiology;
            var ps = ctx.Snapshot.Psychology;
            var rel = ctx.Snapshot.Relationships;

            return state with
            {
                NeedRest = Clamp01p(20 + 6 * ph.SleepDebtHours + (100 - ph.Energy) * 0.5 + ps.Stress * 0.2),
                NeedFood = Clamp01p(ph.Hunger),
                NeedWater = Clamp01p(ph.Thirst),
                // B2: Extraversion modulates social belonging pressure.
                // Introverts have lower baseline social-need, extraverts higher.
                // Additive bias centred on E=0.5: ±20 points at extremes.
                // E=0.5 → no change (backward compatible with all existing tests).
                NeedBelonging = Clamp01p(
                    70 - MeanCloseness(rel)
                    + Math.Max(0, -ps.Valence * 15)
                    - CooldownFor(cooldowns, ReachOut) * 15
                    + (ctx.Personality.BigFive.Extraversion - 0.5) * 20.0),
                NeedCompetence = Clamp01p(50 + (ctx.Personality.Motivation.Competence - 0.5) * 80 - ps.Stress * 0.2),
                NeedIntimacy = ComputeIntimacyNeed(ctx, ph, rel, ps) - CooldownFor(cooldowns, InviteIntimacy) * 20
            };
        }

        internal static double ComputeSelfCareNeed(PhysiologyState ph) => Clamp01p(ph.Pain * 0.7 + ph.ImmuneLoad * 0.3);

        internal static double MeanCloseness(RelationshipState rs)
        {
            if (rs.Edges is null || rs.Edges.Count == 0) return 50;

            // Nejsilnější vztah má největší váhu (quality over quantity)
            var sorted = rs.Edges.Values
                .Select(e => e.Closeness)
                .OrderByDescending(c => c)
                .ToList();

            double weightedSum = 0;
            double totalWeight = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                // Exponenciálně klesající váhy: 1.0, 0.5, 0.25, 0.125...
                var weight = Math.Pow(0.5, i);
                weightedSum += sorted[i] * weight;
                totalWeight += weight;
            }

            return weightedSum / totalWeight;
        }

        internal static double ComputeIntimacyNeed(IHumanContext ctx, PhysiologyState ph, RelationshipState rel, PsychologyState ps)
        {
            // Intimacy need now looks for the strongest contextually viable target
            // rather than trusting the legacy aggregate attraction field alone.
            var topAttraction = ComputeTopIntimacyPotential(rel);

            var stressPenalty = ps.Stress switch
            {
                > 80 => 60.0,
                > 60 => Math.Max(0, ps.Stress - 60) * 1.5,
                > 50 => Math.Max(0, ps.Stress - 50) * 0.3,
                _ => 0.0
            };
            return Math.Clamp(35.0 * (0.5 + ctx.Personality.Motivation.Sexuality) + 0.6 * topAttraction + 40 * ((ph.Cycle?.LibidoMod ?? 1.0) - 1.0) - stressPenalty, 0, 100);
        }

        /// <summary>
        /// Computes the highest intimacy potential among all current relationship targets.
        /// Sexual interest leads, romantic interest follows, and comfort/closeness gate the result.
        /// </summary>
        internal static double ComputeTopIntimacyPotential(RelationshipState rel)
        {
            var top = 0.0;

            foreach (var e in rel.Edges.Values)
            {
                var basePotential =
                    (e.SexualInterest * 0.50) +
                    (e.IntimateAffinity * 0.25) +
                    (e.Comfort * 0.15) +
                    (e.Closeness * 0.10);

                double gate = 1.0;

                if (e.Comfort < 35)
                {
                    gate *= 0.35;
                }

                if (e.Closeness < 20)
                {
                    gate *= 0.45;
                }

                if (e.Familiarity < 10)
                {
                    gate *= 0.70;
                }

                var potential = basePotential * gate;
                top = Math.Max(top, potential);
            }

            return top;
        }

        #endregion Need formulas

        #region Time-of-day shaping

        internal static double ComputeChronoBonus(WDateTime now, Chronotype chronotype)
        {
            var peakHour = chronotype switch
            {
                Chronotype.Lark => 8.0,
                Chronotype.Owl => 20.0,
                _ => 13.0
            };

            var distance = Math.Abs(now.Hour - peakHour);
            return distance > 6.0 ? 0.0 : Math.Max(0.0, 15.0 * (1.0 - distance / 6.0));
        }

        #endregion Time-of-day shaping

        #region Surface multipliers

        internal static double ProductiveSurfaceMultiplier(SurfaceKind kind) => kind switch { SurfaceKind.Work => 1.00, SurfaceKind.Private => 0.78, SurfaceKind.Public => 0.52, SurfaceKind.Social => 0.38, SurfaceKind.Rest => 0.32, SurfaceKind.Unknown => 1.00, _ => 0.60 };

        internal static double SocialSurfaceMultiplier(SurfaceKind kind) => kind switch { SurfaceKind.Social => 1.00, SurfaceKind.Public => 0.75, SurfaceKind.Work => 0.45, SurfaceKind.Private => 0.60, SurfaceKind.Rest => 0.35, SurfaceKind.Unknown => 1.00, _ => 0.60 };

        internal static double PrivateSurfaceMultiplier(SurfaceKind kind) => kind switch { SurfaceKind.Private => 1.00, SurfaceKind.Rest => 0.90, SurfaceKind.Work => 0.50, SurfaceKind.Social => 0.20, SurfaceKind.Unknown => 1.00, _ => 0.60 };

        internal static double RestSurfaceMultiplier(SurfaceKind kind) => kind switch { SurfaceKind.Private => 1.00, SurfaceKind.Rest => 0.95, _ => 0.5 };

        #endregion Surface multipliers
    }
}
