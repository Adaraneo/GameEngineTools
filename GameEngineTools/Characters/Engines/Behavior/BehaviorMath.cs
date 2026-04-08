// BehaviorMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.World.Utils.Time;
    using static ActionNames;

    internal static class BehaviorMath
    {
        internal static double Util(double need, double weight) => need * (0.5 + weight);
        internal static double Clamp01p(double v) => Math.Clamp(v, 0, 100);
        internal static Dictionary<string, double> UpdateCooldowns(IReadOnlyDictionary<string, double>? cooldowns, double hours) => (cooldowns ?? new Dictionary<string, double>()).ToDictionary(kv => kv.Key, kv => Math.Max(0, kv.Value - hours));
        internal static double CooldownFor(IReadOnlyDictionary<string, double> cooldowns, string action) => cooldowns.TryGetValue(action, out var v) ? v : 0;
        internal static BehaviorState ComputeNeedState(IHumanContext ctx, IReadOnlyDictionary<string, double> cooldowns, BehaviorState state)
        {
            var ph = ctx.Snapshot.Physiology; var ps = ctx.Snapshot.Psychology; var rel = ctx.Snapshot.Relationships;
            return state with
            {
                NeedRest = Clamp01p(20 + 6 * ph.SleepDebtHours + (100 - ph.Energy) * 0.5 + ps.Stress * 0.2),
                NeedFood = Clamp01p(ph.Hunger),
                NeedWater = Clamp01p(ph.Thirst),
                NeedBelonging = Clamp01p(70 - MeanCloseness(rel) + Math.Max(0, -ps.Valence * 15) - CooldownFor(cooldowns, ReachOut) * 15),
                NeedCompetence = Clamp01p(50 + (ctx.Personality.Motivation.Competence - 0.5) * 80 - ps.Stress * 0.2),
                NeedIntimacy = ComputeIntimacyNeed(ctx, ph, rel, ps) - CooldownFor(cooldowns, InviteIntimacy) * 20
            };
        }
        internal static double ComputeSelfCareNeed(PhysiologyState ph) => Clamp01p(ph.Pain * 0.7 + ph.ImmuneLoad * 0.3);
        internal static double MeanCloseness(RelationshipState rs) { if (rs.Edges is null || rs.Edges.Count == 0) return 50; double sum = 0; int n = 0; foreach (var e in rs.Edges.Values) { sum += e.Closeness; n++; } return sum / n; }
        internal static double ComputeIntimacyNeed(IHumanContext ctx, PhysiologyState ph, RelationshipState rel, PsychologyState ps)
        {
            var topAttraction = 0.0; foreach (var e in rel.Edges.Values) topAttraction = Math.Max(topAttraction, e.Attraction);
            var stressPenalty = Math.Max(0, ps.Stress - 50) * 0.3;
            return Math.Clamp(35.0 * (0.5 + ctx.Personality.Motivation.Sexuality) + 0.6 * topAttraction + 25 * ((ph.Cycle?.LibidoMod ?? 1.0) - 1.0) - stressPenalty, 0, 100);
        }
        internal static double ComputeChronoBonus(WDateTime now, Chronotype chronotype) { var peakHour = chronotype switch { Chronotype.Lark => 8.0, Chronotype.Owl => 20.0, _ => 13.0 }; var distance = Math.Abs(now.Hour - peakHour); return distance > 6.0 ? 0.0 : Math.Max(0.0, 15.0 * (1.0 - distance / 6.0)); }
        internal static double ProductiveSurfaceMultiplier(SurfaceKind kind) => kind switch { SurfaceKind.Work => 1.00, SurfaceKind.Private => 0.78, SurfaceKind.Public => 0.52, SurfaceKind.Social => 0.38, SurfaceKind.Rest => 0.32, SurfaceKind.Unknown => 1.00, _ => 0.60 };
        internal static double SocialSurfaceMultiplier(SurfaceKind kind) => kind switch { SurfaceKind.Social => 1.00, SurfaceKind.Public => 0.75, SurfaceKind.Work => 0.45, SurfaceKind.Private => 0.60, SurfaceKind.Rest => 0.35, SurfaceKind.Unknown => 1.00, _ => 0.60 };
        internal static double PrivateSurfaceMultiplier(SurfaceKind kind) => kind switch { SurfaceKind.Private => 1.00, SurfaceKind.Rest => 0.90, SurfaceKind.Work => 0.50, SurfaceKind.Social => 0.20, SurfaceKind.Unknown => 1.00, _ => 0.60 };
        internal static double RestSurfaceMultiplier(SurfaceKind kind) => kind switch { SurfaceKind.Private => 1.00, SurfaceKind.Rest => 0.95, _ => 0.5 };
    }
}
