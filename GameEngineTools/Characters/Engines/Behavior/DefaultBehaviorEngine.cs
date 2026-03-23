// DefaultBehaviorEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using System;
    using System.Collections.Generic;
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using static ActionNames;

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
        /// (záměrně, viz poznámka u <see cref="RestoreState"/>).
        /// </summary>
        private ISleepSession? _activeSession;

        #endregion Privátní pole

        #region Statické konstanty — kategorie akcí

        /// <summary>
        /// Akce způsobilé pro setrvačnostní boost.
        /// Pouze "dobrovolné" akce kde setrvačnost dává smysl — rutinní biologické
        /// potřeby (Eat, Drink, SelfCare) se záměrně vynechávají.
        ///
        /// <c>static readonly</c> místo <c>new HashSet</c> uvnitř Tick() —
        /// eliminuje zbytečnou alokaci na heapu při každém volání SelectAction().
        /// </summary>
        private static readonly HashSet<string> InertiaEligible = new HashSet<string> { Work, Create, ReachOut };

        /// <summary>
        /// Kategorie akcí pro výpočet NoveltyPenalty (cognitive switching cost).
        ///
        /// Přepnutí na akci ve STEJNÉ kategorii nemá switching cost — mozek zůstává
        /// ve stejném "módu". Přepnutí do jiné kategorie je kognitivně náročnější.
        ///
        /// Kategorie:
        /// <list type="bullet">
        ///   <item><b>Productive</b> — Work, Create (soustředěná tvorba/práce)</item>
        ///   <item><b>Social</b>     — ReachOut, InviteIntimacy (sociální interakce)</item>
        ///   <item><b>Biological</b> — Eat, Drink, SelfCare (tělesné potřeby)</item>
        ///   <item><b>Rest</b>       — Idle (pasivní odpočinek)</item>
        /// </list>
        /// </summary>
        private static readonly Dictionary<string, ActionCategory> ActionCategories =
            new Dictionary<string, ActionCategory>
            {
                { Work,           ActionCategory.Productive },
                { Create,         ActionCategory.Productive },
                { ReachOut,       ActionCategory.Social      },
                { InviteIntimacy, ActionCategory.Social      },
                { Eat,            ActionCategory.Biological  },
                { Drink,          ActionCategory.Biological  },
                { SelfCare,       ActionCategory.Biological  },
                { Idle,           ActionCategory.Rest        },
            };

        #endregion Statické konstanty — kategorie akcí

        #region Veřejné vlastnosti

        /// <inheritdoc/>
        public BehaviorState State { get; private set; }

        /// <inheritdoc/>
        public BehaviorConfig Config { get; }

        #endregion Veřejné vlastnosti

        #region Konstruktor

        /// <summary>
        /// Vytvoří engine s výchozími hodnotami potřeb.
        /// </summary>
        public DefaultBehaviorEngine(
            IOptions<BehaviorConfig> cfg,
            IOptions<SleepConfig> sleepCfg,
            ILoggerFactory loggerFactory)
        {
            Config = cfg.Value;
            _sleepCfg = sleepCfg.Value;
            _loggerFactory = loggerFactory;
            _log = loggerFactory.CreateLogger<DefaultBehaviorEngine>();

            State = new BehaviorState(
                NeedRest: 40, NeedFood: 30, NeedWater: 25,
                NeedBelonging: 50, NeedCompetence: 50, NeedIntimacy: 35,
                CurrentPlan: null,
                Cooldowns: new Dictionary<string, double>());
        }

        #endregion Konstruktor

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
            var ph = ctx.Snapshot.Physiology;
            var ps = ctx.Snapshot.Psychology;
            var rel = ctx.Snapshot.Relationships;

            // --- Potřeby ---
            var needRest = Clamp01p(20 + 6 * ph.SleepDebtHours + (100 - ph.Energy) * 0.5 + ps.Stress * 0.2);
            var needFood = Clamp01p(ph.Hunger);
            var needWater = Clamp01p(ph.Thirst);
            var needBel = Clamp01p(70 - MeanCloseness(rel) + Math.Max(0, -ps.Valence * 15) - CooldownFor(updatedCooldowns, ReachOut) * 15);
            var needComp = Clamp01p(50 + (ctx.Personality.Motivation.Competence - 0.5) * 80 - ps.Stress * 0.2);
            var needInti = ComputeIntimacyNeed(ctx, ph, rel, ps) - CooldownFor(updatedCooldowns, InviteIntimacy) * 20;
            var needSelfCare = Clamp01p(ph.Pain * 0.7 + ph.ImmuneLoad * 0.3);

            // --- Sleep prompt logika (mimo candidates) ---
            if (CheckSleepPrompt(now, h, needRest, ph.Energy, ps.Stress, ctx, outbox, updatedCooldowns))
            {
                // Engine čeká na odpověď — žádný jiný výběr
                State = State with
                {
                    NeedRest = needRest,
                    NeedFood = needFood,
                    NeedWater = needWater,
                    NeedBelonging = needBel,
                    NeedCompetence = needComp,
                    NeedIntimacy = needInti,
                    Cooldowns = updatedCooldowns
                };
                return;
            }

            // --- Výběr akce z candidates ---
            SelectAction(now, ctx, outbox,
                needRest, needFood, needWater, needBel, needComp, needInti, needSelfCare,
                updatedCooldowns);
        }

        #endregion IEngine — Tick

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
            }
        }

        #endregion IEngine — Handle

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
            double energy,
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
                _log.SleepGracePenalty(ctx.Id.Value.ToString(), penaltyMultiplier, State.SleepDeclineCount);

                return false; // candidates mohou fungovat, jen s rostoucím stresem
            }

            // --- Emergency: extrémní únava přebije vše včetně hladu a žízně ---
            var isEmergency = needRest >= _sleepCfg.EmergencyNeedRestThreshold
                           || energy <= _sleepCfg.EmergencyEnergyThreshold;

            // --- Biologický blok: střední únava + kritický hlad/žízeň → nejdřív jíst/pít ---
            // Pouze pokud to není emergency — hladový člověk při kolapsu stejně usne.
            if (!isEmergency)
            {
                var ph = ctx.Snapshot.Physiology;
                var blocked = ph.Thirst >= _sleepCfg.ThirstSleepBlockThreshold
                           || ph.Hunger >= _sleepCfg.HungerSleepBlockThreshold;

                if (blocked)
                {
                    _log.SleepBlockedByBiology(ctx.Id.Value.ToString(), ph.Hunger, ph.Thirst);
                    return false; // SelectAction převezme řízení → Eat/Drink vyhraje utility race
                }
            }

            // --- Standardní threshold + emergency bypass cooldownu ---
            var sleepCooldown = CooldownFor(cooldowns, Sleep);
            if (needRest >= _sleepCfg.SleepPromptThreshold && (sleepCooldown <= 0 || isEmergency))
            {
                outbox.Add(new SleepPromptRequested(now, ctx.Id, needRest));
                State = State with { WaitingForSleepConfirmation = true, SleepGraceExpiresAt = null };
                using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultBehaviorEngine))))
                {
                    if (isEmergency && sleepCooldown > 0)
                    {
                        _log.SleepPromptSent(ctx.Id.Value.ToString(), needRest);
                    }
                }

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

            using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultBehaviorEngine))))
            {
                _log.SleepStarted(ctx.Id.Value.ToString(), sleepHours);
            }
        }

        /// <summary>
        /// Zpracuje odmítnutí spánku hráčem.
        /// Grace perioda se zkracuje s každým dalším odmítnutím (min. 1 hodina).
        /// </summary>
        private void OnSleepDeclined(SleepDeclined sd, IHumanContext ctx)
        {
            var newDeclineCount = State.SleepDeclineCount + 1;
            var graceHours = Math.Max(1.0, _sleepCfg.SleepGraceHours / newDeclineCount);
            var graceExpiry = sd.OccurredAt + WTimeSpan.FromHours(graceHours);

            State = State with
            {
                WaitingForSleepConfirmation = false,
                SleepDeclineCount = newDeclineCount,
                SleepGraceExpiresAt = graceExpiry
            };

            using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultBehaviorEngine))))
            {
                _log.SleepDeclinedByPlayer(ctx.Id.Value.ToString(), newDeclineCount, graceHours);
            }
        }

        /// <summary>
        /// Uklidí stav po ukončení session (přirozené i přerušené).
        /// Nastaví sleep cooldown, aby postava nešla spát okamžitě znovu.
        /// </summary>
        private void OnSleepSessionEnded(WDateTime now, IHumanContext ctx)
        {
            SetCooldown(ctx.Id, Sleep, Config.SleepCooldownHours);
            State = State with { CurrentPlan = null };
            _activeSession = null;

            using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultBehaviorEngine))))
            {
                _log.SleepSessionEnded(ctx.Id.Value.ToString(), Config.SleepCooldownHours);
            }
        }

        #endregion Sleep prompt — interní logika

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
                    State = State with { CurrentPlan = running, Cooldowns = updatedCooldowns };
                    using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultBehaviorEngine))))
                    {
                        _log.BehaviorActionRunning(ctx.Id.Value.ToString(), running.Name, (running.ExpectedDuration - elapsed).ToString());
                    }

                    return;
                }
            }

            if (State.CurrentPlan is { } cp)
            {
                var currentCategory = GetCategory(cp.Name);

                for (int i = 0; i < candidates.Count; i++)
                {
                    var candidate = candidates[i];

                    // --- SETRVAČNOST ---
                    // Boost pro opakování stejné produktivní akce.
                    // Modeluje "flow state" — postava která pracuje chce pokračovat v práci.
                    if (candidates[i].Name == cp.Name && InertiaEligible.Contains(cp.Name))
                    {
                        candidates[i] = candidate with
                        {
                            Utility = candidate.Utility * (1.0 + Config.InertiaWeight)
                        };
                        continue;
                    }

                    // --- NOVELTY PENALTY (cognitive switching cost) ---
                    // Penalizuj přepnutí do jiné kognitivní kategorie.
                    // Biologické potřeby (Eat, Drink) jsou z penalizace vyjmuty —
                    // hlad a žízeň jsou urgentní a nesmí být uměle potlačeny.
                    var candidateCategory = GetCategory(candidate.Name);
                    if (candidateCategory != currentCategory && candidateCategory != ActionCategory.Biological)
                    {
                        candidates[i] = candidate with
                        {
                            Utility = candidate.Utility * (1.0 - Config.NoveltyPenalty)
                        };
                    }
                }
            }

            ApplyMemoryModifiers(candidates, ctx.Snapshot.Memory, now, ctx.Id, outbox);

            candidates.Sort((a, b) => b.Utility.CompareTo(a.Utility));
            var chosen = candidates[0];
            var plan = new PlannedAction(chosen.Name, now, chosen.Dur, chosen.Utility);

            outbox.Add(new ActionProposed(now, ctx.Id, chosen.Name, chosen.Utility));
            outbox.Add(new ActionCommitted(now, ctx.Id, chosen.Name, chosen.Dur));

            State = State with { CurrentPlan = plan, Cooldowns = updatedCooldowns };
            UpdateCooldownsForActions(ctx.Id, chosen.Name);

            using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultBehaviorEngine))))
            {
                _log.BehaviorActionChosen(ctx.Id.Value.ToString(), chosen.Name, chosen.Utility, chosen.Dur.ToString());
            }
        }

        #endregion Výběr akce (utility)

        #region Memory → Behavior modifikátory

        /// <summary>
        /// Upraví utility kandidátů na základě epizodické paměti postavy.
        ///
        /// Toto je hlavní propojení Memory → Behavior. Postava "si pamatuje"
        /// co se stalo a podle toho upraví sklon k určitým akcím.
        ///
        /// Implementované modifikátory:
        /// <list type="bullet">
        ///   <item>
        ///     <b>Sociální trauma</b> — čerstvé silné negativní vzpomínky na interakce
        ///     snižují chuť vyhledávat kontakt (<see cref="ActionNames.ReachOut"/>).
        ///   </item>
        ///   <item>
        ///     <b>Pozitivní sociální vzpomínky</b> — dobré interakce zvyšují sociální chuť.
        ///   </item>
        ///   <item>
        ///     <b>Intimní odmítnutí</b> — čerstvé odmítnutí intimního kontaktu penalizuje
        ///     <see cref="ActionNames.InviteIntimacy"/> — postava se "stydí" zkoušet znovu.
        ///   </item>
        ///   <item>
        ///     <b>Emocionální zátěž</b> — akumulace negativních vzpomínek (bez ohledu na typ)
        ///     zvyšuje potřebu péče o sebe (<see cref="ActionNames.SelfCare"/>).
        ///   </item>
        /// </list>
        /// </summary>
        /// <param name="candidates">
        ///   Seznam kandidátů (Name, Utility, Dur) modifikujeme Utility in-place.
        /// </param>
        /// <param name="memory">
        ///   Aktuální snapshot paměti z <see cref="IHumanContext.Snapshot"/>.
        ///   Read-only double-buffer zaručuje, že čteme stav z předchozího ticku.
        /// </param>
        /// <param name="now">Aktuální herní čas pro výpočet čerstvosti vzpomínek.</param>
        /// <param name="personId">DNA postavy.</param>
        /// <param name="outbox">Event outbox</param>
        private static void ApplyMemoryModifiers(
            List<(string Name, double Utility, WTimeSpan Dur)> candidates,
            MemoryIndex memory,
            WDateTime now,
            HumanId personId,
            IEventCollector outbox)
        {
            // Pracujeme s epizodami, které jsou stále "živé" (Strength > 0)
            var episodes = memory.Episodes;

            if (episodes.Count == 0)
                return; // Postava nemá žádné vzpomínky — nic neupravujeme

            // MODIFIKÁTOR 1: Sociální trauma
            var negativeInteractions = episodes
                .Where(e =>
                    e.What.StartsWith("Interaction:") &&
                    e.Emotion == EmotionalTag.Negative &&
                    e.Strength > 0.4)
                .ToList();

            foreach (var e in negativeInteractions)
            {
                outbox.Add(new MemoryRecalled(now, personId, e.Id));
            }

            if (negativeInteractions.Count > 0)
            {
                var penalty = Math.Min(0.40, negativeInteractions.Count * 0.10);
                ModifyUtility(candidates, ReachOut, multiplier: 1.0 - penalty);
            }

            // MODIFIKÁTOR 2: Pozitivní sociální vzpomínky
            var positiveInteractions = episodes
                .Where(e =>
                    e.What.StartsWith("Interaction:") &&
                    e.Emotion == EmotionalTag.Positive &&
                    e.Strength > 0.4)
                .ToList();

            foreach (var e in positiveInteractions)
            {
                outbox.Add(new MemoryRecalled(now, personId, e.Id));
            }

            if (positiveInteractions.Count > 0)
            {
                var boost = Math.Min(0.25, positiveInteractions.Count * 0.08);
                ModifyUtility(candidates, ReachOut, multiplier: 1.0 + boost);
            }

            // MODIFIKÁTOR 3: Intimní odmítnutí
            var rejectedIntimacy = episodes
                .Where(e =>
                    e.What.Contains("InviteIntimacy") &&
                    e.Emotion == EmotionalTag.Negative &&
                    e.Strength > 0.35)
                .ToList();

            foreach (var e in rejectedIntimacy)
            {
                outbox.Add(new MemoryRecalled(now, personId, e.Id));
            }

            if (rejectedIntimacy.Count > 0)
            {
                var penalty = Math.Min(0.55, rejectedIntimacy.Count * 0.20);
                ModifyUtility(candidates, InviteIntimacy, multiplier: 1.0 - penalty);
            }

            // MODIFIKÁTOR 4: Emocionální zátěž
            var negativeLoad = episodes
                .Where(e =>
                    e.Emotion == EmotionalTag.Negative &&
                    e.Strength > 0.3)
                .ToList();

            foreach (var e in negativeLoad)
            {
                outbox.Add(new MemoryRecalled(now, personId, e.Id));
            }

            var loadSum = negativeLoad.Sum(e => e.Strength);
            if (loadSum > 0.5)
            {
                var boost = Math.Min(0.35, loadSum * 0.08);
                ModifyUtility(candidates, SelfCare, multiplier: 1.0 + boost);
            }
        }

        /// <summary>
        /// Pomocná metoda — najde kandidáta podle jména a vynásobí jeho Utility daným koeficientem.
        ///
        /// Používá index místo LINQ, protože měníme strukturu v listu (value type tuple).
        /// <c>candidates[i] = candidates[i] with { ... }</c> na tuple nefunguje —
        /// musíme vytvořit novou tuple a přiřadit na index.
        /// </summary>
        /// <param name="candidates">Seznam kandidátů k modifikaci.</param>
        /// <param name="actionName">Název akce, jejíž utility měníme.</param>
        /// <param name="multiplier">Koeficient (např. 0.7 = -30 %, 1.25 = +25 %).</param>
        private static void ModifyUtility(
            List<(string Name, double Utility, WTimeSpan Dur)> candidates,
            string actionName,
            double multiplier)
        {
            // Tuple je value type — musíme pracovat přes index, ne foreach
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Name == actionName)
                {
                    // Clamp na 0 — utility nemůže být záporná
                    var newUtility = Math.Max(0.0, candidates[i].Utility * multiplier);
                    candidates[i] = (candidates[i].Name, newUtility, candidates[i].Dur);
                    return; // Každé jméno je v listu max jednou
                }
            }
        }

        #endregion Memory → Behavior modifikátory

        #region Pomocné metody

        /// <summary>
        /// Vrátí kognitivní kategorii akce pro výpočet NoveltyPenalty.
        /// Neznámé akce (např. budoucí rozšíření) dostávají <see cref="ActionCategory.Rest"/>
        /// jako bezpečný fallback, nebudou penalizovány ani boostovány.
        /// </summary>
        /// <param name="actionName">Název akce z <see cref="ActionNames"/>.</param>
        private static ActionCategory GetCategory(string actionName)
            => ActionCategories.TryGetValue(actionName, out var cat) ? cat : ActionCategory.Rest;

        /// <summary>Nastaví nebo přepíše cooldown pro danou akci.</summary>
        private void SetCooldown(HumanId owner, string action, double hours)
        {
            var dict = new Dictionary<string, double>(State.Cooldowns ?? new Dictionary<string, double>());
            dict[action] = hours;
            State = State with { Cooldowns = dict };
            _log.BehaviorCooldownSet(owner.Value.ToString(), action, hours);
        }

        /// <summary>Vrátí zbývající cooldown pro akci, nebo 0 pokud cooldown neexistuje.</summary>
        private static double CooldownFor(IReadOnlyDictionary<string, double> cd, string action)
            => cd.TryGetValue(action, out var v) ? v : 0;

        private void UpdateCooldownsForActions(HumanId owner, string chosen)
        {
            double hours = chosen switch
            {
                InviteIntimacy => 6,
                ReachOut => 4,
                _ => double.NaN
            };

            if (!double.IsNaN(hours))
            {
                SetCooldown(owner, chosen, hours);
            }
        }

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
            var baseNeed = 35.0;
            var libido = ph.Cycle?.LibidoMod ?? 1.0;
            var topAttraction = TopAttraction(rel);
            var trait = 0.5 + ctx.Personality.Motivation.Sexuality;
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

        #endregion Pomocné metody

        #region RestoreState

        /// <inheritdoc/>
        /// <remarks>
        /// Po restore je <c>_activeSession</c> vždy <c>null</c> — session je runtime objekt
        /// a nelze ji deserializovat ze stavu. Postava jednoduše nezačne hned spát znovu,
        /// protože sleep cooldown bude aktivní z uloženého stavu.
        /// </remarks>
        public void RestoreState(BehaviorState state)
        {
            State = state;
            _activeSession = null;
        }

        #endregion RestoreState
    }
}
