// DefaultBehaviorEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using System;
    using System.Collections.Generic;
    using Characters.Core;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    internal sealed class DefaultBehaviorEngine : IBehaviorEngine
    {
        public BehaviorState State { get; private set; }
        public BehaviorConfig Config { get; }

        private readonly ILogger _log;
        private readonly WorldTimeContext _wtctx;

        public DefaultBehaviorEngine(IOptions<BehaviorConfig> cfg, ILoggerFactory loggerFactory, WorldTimeContext wtctx)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger("Characters.Behavior");
            _wtctx = wtctx;
            State = new BehaviorState(
                NeedRest: 40, NeedFood: 30, NeedWater: 25, NeedBelonging: 50, NeedCompetence: 50, NeedIntimacy: 35,
                CurrentPlan: null,
                Cooldowns: new Dictionary<string, double>());
        }

        private void SetCooldown(string action, double hours)
        {
            var dict = new Dictionary<string, double>(State.Cooldowns ?? new Dictionary<string, double>());
            dict[action] = hours;
            State = State with { Cooldowns = dict };
            _log.LogDebug("[COOLDOWN] new Cooldown for {Action}:{Hours}", action, hours);
        }

        private static double CooldownFor(IReadOnlyDictionary<string, double> cd, string action)
            => cd.TryGetValue(action, out var v) ? v : 0;

        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            var h = Math.Max(0, _wtctx.TotalHours(dt));

            var updatedCooldowns = (State.Cooldowns ?? new Dictionary<string, double>()).ToDictionary(kv => kv.Key, kv => Math.Max(0, kv.Value - h));

            var ph = ctx.Snapshot.Physiology;
            var ps = ctx.Snapshot.Psychology;
            var rel = ctx.Snapshot.Relationships;

            var belCd = CooldownFor(updatedCooldowns, "ReachOut");
            var intiCd = CooldownFor(updatedCooldowns, "InviteInimacy");

            // Přepočet potřeb z aktuálního stavu
            var needRest = Clamp01p(20 + 6 * ph.SleepDebtHours + (100 - ph.Energy) * 0.5 + ps.Stress * 0.2);
            var needFood = Clamp01p(ph.Hunger);
            var needWater = Clamp01p(ph.Thirst);
            var needBel = Clamp01p(70 - MeanCloseness(rel) + Math.Max(0, -ps.Valence * 15) - belCd * 5);
            var needComp = Clamp01p(50 + (ctx.Personality.Motivation.Competence - 0.5) * 80 - ps.Stress * 0.2);
            var needInti = ComputeIntimacyNeed(ctx, ph, rel, ps) - intiCd * 8;

            // Volba akce (utility = potřeba * váha motivace; setrvačnost + penalizace monotónnosti)
            var candidates = new List<(string Name, double Utility, WTimeSpan Dur)>
            {
                ("Sleep",     Util(needRest,     ctx.Personality.Motivation.Rest * 1.3),        _wtctx.Hours(Math.Max(1.0, ph.SleepDebtHours * 0.6))),
                ("Eat",       Util(needFood,     1.2),                                   _wtctx.Minutes(30)),
                ("Drink",     Util(needWater,    1.1),                                   _wtctx.Minutes(10)),
                ("ReachOut",  Util(needBel,      ctx.Personality.Motivation.Affiliation),_wtctx.Hours(1.0)),
                ("Work",      Util(needComp,     ctx.Personality.Motivation.Competence), _wtctx.Hours(2.0)),
                ("Create",    Util(needComp,     ctx.Personality.Motivation.Curiosity),  _wtctx.Hours(1.5)),
                ("SelfCare",  Util(50,           0.5),                                   _wtctx.Hours(0.5)),
                ("InviteIntimacy", Util(needInti, ctx.Personality.Motivation.Sexuality), _wtctx.Hours(1.0))
            };

            // Je aktuální akce stále rozdělaná? Pokud ano, nerozhodujeme se.
            if (State.CurrentPlan is { } running)
            {
                var elapsed = now - running.Start;
                if (elapsed < running.ExpectedDuration)
                {
                    State = new BehaviorState(needRest, needFood, needWater, needBel, needComp, needInti, running);
                    _log.LogDebug("[Behavior] Akce '{Action}' stále běží, zbývá {Remaining}.", running.Name, running.ExpectedDuration - elapsed);
                    return;
                }
            }

            //Akce je dokončena nebo žádná neexistuje -> vybereme novou
            // Setrvačnost: pokud jsme právě dokončili akci, lehce zvýhodníme stejnou volbu
            if (State.CurrentPlan is { } cp)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].Name == cp.Name)
                    {
                        candidates[i] = (cp.Name, candidates[i].Utility * (1.0 + Config.InertiaWeight), candidates[i].Dur);
                    }
                }
            }

            candidates.Sort((a, b) => b.Utility.CompareTo(a.Utility));
            var chosen = candidates[0];
            var plan = new PlannedAction(chosen.Name, now, chosen.Dur, chosen.Utility);

            outbox.Add(new ActionProposed(now, ctx.Id, chosen.Name, chosen.Utility));
            outbox.Add(new ActionCommitted(now, ctx.Id, chosen.Name, chosen.Dur));

            State = new BehaviorState(needRest, needFood, needWater, needBel, needComp, needInti, plan, updatedCooldowns);
            _log.LogInformation("[Behavior] Nová akce: '{Action}' (utility={Utility:F2}, trvání={Duration}).", chosen.Name, chosen.Utility, chosen.Dur);

            // ---- locals ----
            static double Util(double need, double weight) => (need * (0.5 + weight));
            static double MeanCloseness(Relationships.RelationshipState rs)
            {
                if (rs.Edges is null || rs.Edges.Count == 0)
                {
                    return 50;
                }

                double sum = 0; int n = 0;
                foreach (var e in rs.Edges.Values) { sum += e.Closeness; n++; }
                return sum / n;
            }
            static double Clamp01p(double v) => Math.Max(0, Math.Min(100, v));
        }

        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            switch (@event)
            {
                case ActionCommitted ac when ac.ActionName == "ReachOut":
                    SetCooldown("ReachOut", 4);
                    break;

                case ActionCommitted ac when ac.ActionName == "InviteIntimacy":
                    SetCooldown("InviteIntimacy", 6);
                    break;
            }
        }

        // --- helpers ---
        private static double ComputeIntimacyNeed(IHumanContext ctx, Physiology.PhysiologyState ph, Relationships.RelationshipState rel, Psychology.PsychologyState ps)
        {
            var baseNeed = 35.0;
            var libido = (ph.Cycle?.LibidoMod ?? 1.0);
            var social = TopAttraction(rel);
            var trait = 0.5 + ctx.Personality.Motivation.Sexuality; // 0.5..1.5
            var stressPenalty = Math.Max(0, ps.Stress - 50) * 0.3;
            return Math.Clamp(baseNeed * trait + 0.6 * social + 25 * (libido - 1.0) - stressPenalty, 0, 100);

            static double TopAttraction(Relationships.RelationshipState rs)
            {
                double top = 0;
                foreach (var e in rs.Edges.Values)
                {
                    top = Math.Max(top, e.Attraction);
                }

                return top;
            }
        }

        public void RestoreState(BehaviorState state) => State = state;
    }
}

