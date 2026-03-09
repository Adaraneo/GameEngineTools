// DefaultBehaviorEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using System;
    using System.Collections.Generic;
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using static Behavior.ActionNames;

    /// <summary>
    /// Výchozí implementace behaviorálního enginu.
    /// Přepočítává potřeby postavy, vybírá akci pomocí utility funkce
    /// a řídí životní cyklus spánkové session (<see cref="ISleepSession"/>).
    /// </summary>
    /// <remarks>
    /// Spánek je záměrně vyřazen z candidates listu a řešen samostatně —
    /// viz architektonické rozhodnutí v <see cref="CheckSleepPrompt"/>.
    /// </remarks>
    internal sealed class DefaultBehaviorEngine : IBehaviorEngine
    {
        #region Privátní pole

        private readonly ILogger _log;
        private readonly SleepConfig _sleepCfg;
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Aktivní spánková session. <c>null</c> pokud postava nespí.
        /// Session je runtime objekt — není součástí <see cref="BehaviorState"/>
        /// (záměrně, viz poznámku u <see cref="RestoreState"/>).
        /// </summary>
        private ISleepSession? _activeSession;

        #endregion

        #region Veřejné vlastnosti

        /// <inheritdoc/>
        public BehaviorState State { get; private set; }

        /// <inheritdoc/>
        public BehaviorConfig Config { get; }

        #endregion

        #region Konstruktor

        /// <summary>
        /// Vytvoří engine s výchozími hodnotami potřeb.
        /// </summary>
        public DefaultBehaviorEngine(
            IOptions<BehaviorConfig> cfg,
            IOptions<SleepConfig> sleepCfg,
            ILoggerFactory loggerFactory)
        {
            Config         = cfg.Value;
            _sleepCfg      = sleepCfg.Value;
            _loggerFactory = loggerFactory;
            _log           = loggerFactory.CreateLogger("Characters.Behavior");

            State = new BehaviorState(
                NeedRest: 40, NeedFood: 30, NeedWater: 25,
                NeedBelonging: 50, NeedCompetence: 50, NeedIntimacy: 35,
                CurrentPlan: null,
                Cooldowns: new Dictionary<string, double>());
        }

        #endregion

        #region IEngine — Tick

        /// <inheritdoc/>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            var h = Math.Max(0, dt.TotalHours);

            // --- Aktivní session: tickuj spánek, nic jiného nedělej ---
            if (_activeSession is { IsActive: true })
            {
                _activeSession.Tick(now, dt, ctx, outbox);

                // Session mohla skončit přirozeně nebo noční můrou v tomto ticku
                if (!_activeSession.IsActive)
                    OnSleepSessionEnded(now, ctx);

                return;
            }

            // --- Cooldowny ---
            var updatedCooldowns = (State.Cooldowns ?? new Dictionary<string, double>())
                .ToDictionary(kv => kv.Key, kv => Math.Max(0, kv.Value - h));

            // --- Snapshoty ---
            var ph  = ctx.Snapshot.Physiology;
            var ps  = ctx.Snapshot.Psychology;
            var rel = ctx.Snapshot.Relationships;

            // --- Potřeby ---
            var needRest     = Clamp01p(20 + 6 * ph.SleepDebtHours + (100 - ph.Energy) * 0.5 + ps.Stress * 0.2);
            var needFood     = Clamp01p(ph.Hunger);
            var needWater    = Clamp01p(ph.Thirst);
            var needBel      = Clamp01p(70 - MeanCloseness(rel) + Math.Max(0, -ps.Valence * 15) - CooldownFor(updatedCooldowns, ReachOut) * 5);
            var needComp     = Clamp01p(50 + (ctx.Personality.Motivation.Competence - 0.5) * 80 - ps.Stress * 0.2);
            var needInti     = ComputeIntimacyNeed(ctx, ph, rel, ps) - CooldownFor(updatedCooldowns, InviteIntimacy) * 8;
            var needSelfCare = Clamp01p(ph.Pain * 0.7 + ph.ImmuneLoad * 0.3);

            // --- Sleep prompt logika (mimo candidates) ---
            if (CheckSleepPrompt(now, h, needRest, ps.Stress, ctx, outbox, updatedCooldowns))
            {
                // Engine čeká na odpověď — žádný jiný výběr
                State = State with
                {
                    NeedRest = needRest, NeedFood = needFood, NeedWater = needWater,
                    NeedBelonging = needBel, NeedCompetence = needComp, NeedIntimacy = needInti,
                    Cooldowns = updatedCooldowns
                };
                return;
            }

            // --- Výběr akce z candidates ---
            SelectAction(now, ctx, outbox,
                needRest, needFood, needWater, needBel, needComp, needInti, needSelfCare,
                updatedCooldowns);
        }

        #endregion

        #region IEngine — Handle

        /// <inheritdoc/>
        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            switch (@event)
            {
                // --- Spánek: hráč/systém potvrdil ---
                case SleepConfirmed sc:
                    OnSleepConfirmed(sc, ctx, outbox);
                    break;

                // --- Spánek: hráč odmítl ---
                case SleepDeclined sd:
                    OnSleepDeclined(sd, ctx);
                    break;

                // --- Spánek: externí přerušení (přepad, bolest...) ---
                case SleepInterrupted si when _activeSession is { IsActive: true }:
                    _activeSession.Interrupt(si.OccurredAt, si.Cause, ctx, outbox);
                    break;

                // --- Ostatní cooldowny ---
                case ActionCommitted ac when ac.ActionName == ReachOut:
                    SetCooldown(ReachOut, 4);
                    break;

                case ActionCommitted ac when ac.ActionName == InviteIntimacy:
                    SetCooldown(InviteIntimacy, 6);
                    break;
            }
        }

        #endregion

        #region Sleep prompt — interní logika

        /// <summary>
        /// Zkontroluje, zda má engine vyslat sleep prompt nebo aplikovat grace penalizaci.
        /// </summary>
        /// <returns>
        /// <c>true</c> pokud engine přechází do stavu čekání na odpověď hráče/systému.
        /// V tom případě <see cref="Tick"/> přeskočí výběr akce z candidates.
        /// </returns>
        private bool CheckSleepPrompt(
            WDateTime now,
            double h,
            double needRest,
            double stress,
            IHumanContext ctx,
            IEventCollector outbox,
            Dictionary<string, double> cooldowns)
        {
            // --- Čekáme na odpověď hráče — nic neděláme ---
            if (State.WaitingForSleepConfirmation)
                return true;

            // --- Grace perioda běží: aplikuj narůstající penalizaci ---
            if (State.SleepGraceExpiresAt.HasValue && now < State.SleepGraceExpiresAt.Value)
            {
                var penaltyMultiplier = 1.0 + State.SleepDeclineCount * 0.5; // roste s odmítnutími
                _log.LogDebug("[Behavior] {Id} odmítá spánek — penalizace ×{Mult:F1} (odmítnutí #{Count}).",
                    ctx.Id, penaltyMultiplier, State.SleepDeclineCount);
                return false; // candidates mohou fungovat, jen s rostoucím stresem
            }

            // --- Grace vypršela nebo neexistuje: zkontroluj threshold ---
            var sleepCooldown = CooldownFor(cooldowns, Sleep);
            if (needRest >= _sleepCfg.SleepPromptThreshold && sleepCooldown <= 0)
            {
                outbox.Add(new SleepPromptRequested(now, ctx.Id, needRest));
                State = State with { WaitingForSleepConfirmation = true, SleepGraceExpiresAt = null };

                _log.LogInformation("[Behavior] {Id} — sleep prompt vyslán (NeedRest={Need:F1}).", ctx.Id, needRest);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Zpracuje potvrzení spánku — vytvoří a spustí novou <see cref="DefaultSleepSession"/>.
        /// Pokud <see cref="SleepEvents.SleepConfirmed.PlannedWakeUp"/> není nastaveno,
        /// vypočítá délku z <see cref="BehaviorConfig.BaseSleepHours"/> + spánkový dluh.
        /// </summary>
        private void OnSleepConfirmed(SleepConfirmed sc, IHumanContext ctx, IEventCollector outbox)
        {
            var session = new DefaultSleepSession(_sleepCfg, _loggerFactory, ctx.Random);

            var sleepHours = Math.Clamp(
                Config.BaseSleepHours + ctx.Snapshot.Physiology.SleepDebtHours * 0.5,
                Config.MinSleepHours,
                Config.MaxSleepHours);

            var plannedWakeUp = sc.PlannedWakeUp != default
                ? sc.PlannedWakeUp
                : sc.OccurredAt + WTimeSpan.FromHours(sleepHours);

            session.Begin(sc.OccurredAt, plannedWakeUp, ctx, outbox, sc.Companion, sc.SharedType);

            _activeSession = session;
            State = State with
            {
                WaitingForSleepConfirmation = false,
                SleepGraceExpiresAt = null,
                SleepDeclineCount = 0,
                CurrentPlan = new PlannedAction(Sleep, sc.OccurredAt, WTimeSpan.FromHours(sleepHours), 100)
            };

            _log.LogInformation("[Behavior] {Id} — spánek zahájen. Délka: {Hours:F1}h.", ctx.Id, sleepHours);
        }

        /// <summary>
        /// Zpracuje odmítnutí spánku hráčem.
        /// Grace perioda se zkracuje s každým dalším odmítnutím (min. 1 hodina).
        /// </summary>
        private void OnSleepDeclined(SleepDeclined sd, IHumanContext ctx)
        {
            var newDeclineCount = State.SleepDeclineCount + 1;
            var graceHours      = Math.Max(1.0, _sleepCfg.SleepGraceHours / newDeclineCount);
            var graceExpiry     = sd.OccurredAt + WTimeSpan.FromHours(graceHours);

            State = State with
            {
                WaitingForSleepConfirmation = false,
                SleepDeclineCount           = newDeclineCount,
                SleepGraceExpiresAt         = graceExpiry
            };

            _log.LogWarning("[Behavior] {Id} — spánek odmítnut (#{Count}), grace: {Grace:F1}h.",
                ctx.Id, newDeclineCount, graceHours);
        }

        /// <summary>
        /// Uklidí stav po ukončení session (přirozené i přerušené).
        /// Nastaví sleep cooldown, aby postava nešla spát okamžitě znovu.
        /// </summary>
        private void OnSleepSessionEnded(WDateTime now, IHumanContext ctx)
        {
            SetCooldown(Sleep, Config.SleepCooldownHours);
            State          = State with { CurrentPlan = null };
            _activeSession = null;

            _log.LogInformation("[Behavior] {Id} — sleep session ukončena, cooldown {Cooldown}h.",
                ctx.Id, Config.SleepCooldownHours);
        }

        #endregion

        #region Výběr akce (utility)

        /// <summary>
        /// Sestaví seznam kandidátů, aplikuje setrvačnost a vybere akci s nejvyšší utilitou.
        /// </summary>
        /// <remarks>
        /// Sleep zde záměrně chybí — je řešen přes <see cref="CheckSleepPrompt"/>.
        /// </remarks>
        private void SelectAction(
            WDateTime now,
            IHumanContext ctx,
            IEventCollector outbox,
            double needRest, double needFood, double needWater,
            double needBel, double needComp, double needInti, double needSelfCare,
            Dictionary<string, double> updatedCooldowns)
        {
            var candidates = new List<(string Name, double Utility, WTimeSpan Dur)>
            {
                (Eat,            Util(needFood,     1.2),                                    WTimeSpan.FromMinutes(30)),
                (Drink,          Util(needWater,    1.1),                                    WTimeSpan.FromMinutes(10)),
                (ReachOut,       Util(needBel,      ctx.Personality.Motivation.Affiliation), WTimeSpan.FromHours(1.0)),
                (Work,           Util(needComp,     ctx.Personality.Motivation.Competence),  WTimeSpan.FromHours(2.0)),
                (Create,         Util(needComp,     ctx.Personality.Motivation.Curiosity),   WTimeSpan.FromHours(1.5)),
                (SelfCare,       Util(needSelfCare, 0.5),                                    WTimeSpan.FromHours(0.5)),
                (InviteIntimacy, Util(needInti,     ctx.Personality.Motivation.Sexuality),   WTimeSpan.FromHours(1.0)),
                (Idle,           Util(10,           0.3),                                    WTimeSpan.FromMinutes(30))
            };

            // Běžící akce ještě neskončila — ponech ji
            if (State.CurrentPlan is { } running)
            {
                var elapsed = now - running.Start;
                if (elapsed < running.ExpectedDuration)
                {
                    State = State with { CurrentPlan = running };
                    _log.LogDebug("[Behavior] Akce '{Action}' stále běží, zbývá {Remaining}.",
                        running.Name, running.ExpectedDuration - elapsed);
                    return;
                }
            }

            // Setrvačnost: zvýhodní dokončenou produktivní akci pro opakování
            if (State.CurrentPlan is { } cp)
            {
                var inertiaEligible = new HashSet<string> { Work, Create, ReachOut };
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (inertiaEligible.Contains(candidates[i].Name) && candidates[i].Name == cp.Name)
                        candidates[i] = (cp.Name, candidates[i].Utility * (1.0 + Config.InertiaWeight), candidates[i].Dur);
                }
            }

            candidates.Sort((a, b) => b.Utility.CompareTo(a.Utility));
            var chosen = candidates[0];
            var plan   = new PlannedAction(chosen.Name, now, chosen.Dur, chosen.Utility);

            outbox.Add(new ActionProposed(now, ctx.Id, chosen.Name, chosen.Utility));
            outbox.Add(new ActionCommitted(now, ctx.Id, chosen.Name, chosen.Dur));

            State = State with { CurrentPlan = plan, Cooldowns = updatedCooldowns };
            _log.LogInformation("[Behavior] Nová akce: '{Action}' (utility={Utility:F2}, trvání={Duration}).",
                chosen.Name, chosen.Utility, chosen.Dur);
        }

        #endregion

        #region Pomocné metody

        /// <summary>Nastaví nebo přepíše cooldown pro danou akci.</summary>
        private void SetCooldown(string action, double hours)
        {
            var dict = new Dictionary<string, double>(State.Cooldowns ?? new Dictionary<string, double>());
            dict[action] = hours;
            State = State with { Cooldowns = dict };
            _log.LogDebug("[COOLDOWN] {Action}: {Hours}h.", action, hours);
        }

        /// <summary>Vrátí zbývající cooldown pro akci, nebo 0 pokud cooldown neexistuje.</summary>
        private static double CooldownFor(IReadOnlyDictionary<string, double> cd, string action)
            => cd.TryGetValue(action, out var v) ? v : 0;

        /// <summary>Vypočítá utility akce: potřeba × (0.5 + váha motivace).</summary>
        private static double Util(double need, double weight) => need * (0.5 + weight);

        /// <summary>Clamp na rozsah 0–100.</summary>
        private static double Clamp01p(double v) => Math.Clamp(v, 0, 100);

        /// <summary>
        /// Průměrná blízkost všech vztahů postavy.
        /// Pokud postava nemá žádné vztahy, vrátí 50 jako neutrální baseline.
        /// </summary>
        private static double MeanCloseness(Relationships.RelationshipState rs)
        {
            if (rs.Edges is null || rs.Edges.Count == 0) return 50;
            double sum = 0; int n = 0;
            foreach (var e in rs.Edges.Values) { sum += e.Closeness; n++; }
            return sum / n;
        }

        /// <summary>
        /// Vypočítá potřebu intimity na základě libida, přitažlivosti a stresu.
        /// Modulace libida pochází z <see cref="Physiology.MenstrualCycleState"/>.
        /// </summary>
        private static double ComputeIntimacyNeed(
            IHumanContext ctx,
            Physiology.PhysiologyState ph,
            Relationships.RelationshipState rel,
            Psychology.PsychologyState ps)
        {
            var baseNeed      = 35.0;
            var libido        = ph.Cycle?.LibidoMod ?? 1.0;
            var topAttraction = TopAttraction(rel);
            var trait         = 0.5 + ctx.Personality.Motivation.Sexuality;
            var stressPenalty = Math.Max(0, ps.Stress - 50) * 0.3;

            return Math.Clamp(
                baseNeed * trait + 0.6 * topAttraction + 25 * (libido - 1.0) - stressPenalty,
                0, 100);

            static double TopAttraction(Relationships.RelationshipState rs)
            {
                double top = 0;
                foreach (var e in rs.Edges.Values)
                    top = Math.Max(top, e.Attraction);
                return top;
            }
        }

        #endregion

        #region RestoreState

        /// <inheritdoc/>
        /// <remarks>
        /// Po restore je <c>_activeSession</c> vždy <c>null</c> — session je runtime objekt
        /// a nelze ji deserializovat ze stavu. Postava jednoduše nezačne hned spát znovu,
        /// protože sleep cooldown bude aktivní z uloženého stavu.
        /// </remarks>
        public void RestoreState(BehaviorState state)
        {
            State          = state;
            _activeSession = null;
        }

        #endregion
    }
}
