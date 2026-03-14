// DefaultRelationshipsEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Výchozí implementace <see cref="IRelationshipsEngine"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Engine spravuje orientovaný graf vztahů postavy — každá hrana (<see cref="RelationshipEdge"/>)
    /// reprezentuje, jak táto postava vnímá konkrétního druhého člověka.
    /// Graf je tedy <b>asymetrický</b>: A může mít ráda B víc, než B má ráda A.
    /// </para>
    /// <para>
    /// <b>Tři vrstvy dynamiky:</b>
    /// <list type="number">
    ///   <item>
    ///     <b>Události</b> — okamžité skoky (MicroPositive, RepairAttempt, InteractionOutcome)
    ///   </item>
    ///   <item>
    ///     <b>Decay</b> — pomalé tíhnutí k neutrálním hodnotám (čas bez kontaktu)
    ///   </item>
    ///   <item>
    ///     <b>DomainBreakdown</b> — granulární obraz ČEHO si váží (humor, intelekt, hodnoty…)
    ///   </item>
    /// </list>
    /// </para>
    /// </remarks>
    internal sealed class DefaultRelationshipsEngine : IRelationshipsEngine
    {
        #region Stav a konfigurace

        /// <inheritdoc/>
        public RelationshipState State { get; private set; }

        /// <inheritdoc/>
        public RelationshipsConfig Config { get; }

        #endregion Stav a konfigurace

        #region Privátní pole

        private readonly ILogger _log;

        #endregion Privátní pole

        #region Konstruktor

        /// <summary>Vytvoří instanci enginu s prázdným grafem vztahů.</summary>
        public DefaultRelationshipsEngine(IOptions<RelationshipsConfig> cfg, ILoggerFactory loggerFactory)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger<DefaultRelationshipsEngine>();
            State = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>());
        }

        #endregion Konstruktor

        #region Handle — zpracování doménových událostí

        /// <inheritdoc/>
        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            var self = ctx.Id;

            switch (@event)
            {
                // ── První dojem ────────────────────────────────────────────────────────────
                case FirstImpressionFormed fi:
                    Upsert(self, fi.B, e => e with
                    {
                        // Lerp 70% směrem k prvnímu dojmu — ale nenahradí zcela,
                        // pokud postava druhého již trochu zná.
                        Like       = Lerp(e.Like, fi.Like, 0.7),
                        Attraction = Lerp(e.Attraction, fi.Attraction, 0.7),
                        Trust      = e.Trust <= 0 ? 45 : e.Trust,
                        Closeness  = Math.Max(e.Closeness, 10)
                    });
                    break;

                // ── Mikrokladná interakce (úsměv, kompliment, pomoc…) ──────────────────────
                case MicroPositive mp:
                    Upsert(self, mp.B, e => e with
                    {
                        Like      = Bump(e.Like,      +2.0),
                        Trust     = Bump(e.Trust,     +1.0),
                        Closeness = Bump(e.Closeness, +1.5),
                        Comfort   = Bump(e.Comfort,   +2.0)
                    });
                    break;

                // ── Mikronegativní interakce (kritika, ignorování, lhostejnost…) ───────────
                case MicroNegative mn:
                    Upsert(self, mn.B, e => e with
                    {
                        Like    = Bump(e.Like,    -2.5),
                        Trust   = Bump(e.Trust,   -2.0),
                        Comfort = Bump(e.Comfort, -2.0)
                    });
                    break;

                // ── Pokus o opravu vztahu ─────────────────────────────────────────────────
                case RepairAttempt ra:
                    Upsert(self, ra.B, e => e with
                    {
                        Trust     = Bump(e.Trust,     ra.Accepted ? +Config.RepairGain      : -Config.RupturePenalty),
                        Closeness = Bump(e.Closeness, ra.Accepted ? +Config.RepairGain * 0.5 : -Config.RupturePenalty * 0.4)
                    });
                    break;

                // ── Výsledek interakce — PŘIJATO ──────────────────────────────────────────
                case InteractionOutcome io when io.Accepted:
                {
                    var otherId = io.From == self ? io.To : io.From;
                    EnsureEdge(self, otherId);

                    Upsert(self, otherId, e => e with
                    {
                        Closeness = Bump(e.Closeness, +1.5),
                        Like      = Bump(e.Like,      +0.5),
                        Comfort   = Bump(e.Comfort,   +0.8),

                        Breakdown = ApplyDomainBoost(e.Breakdown, io.Act, accepted: true)
                    });
                    break;
                }

                // ── Výsledek interakce — ODMÍTNUTO ───────────────────────────────────────
                case InteractionOutcome io when !io.Accepted:
                {
                    var otherId2 = io.From == self ? io.To : io.From;
                    EnsureEdge(self, otherId2);

                    Upsert(self, otherId2, e => e with
                    {
                        // Mírný pokles Like a Comfort — sociální nepohodlí z odmítnutí
                        Like    = Bump(e.Like,    -1.5),
                        Comfort = Bump(e.Comfort, -2.0),

                        // Odmítnutí také zanechá stopu v doméně — ale menší než přijetí
                        Breakdown = ApplyDomainBoost(e.Breakdown, io.Act, accepted: false)
                    });
                    break;
                }
            }
        }

        #endregion Handle — zpracování doménových událostí

        #region Tick — časový decay

        /// <inheritdoc/>
        /// <remarks>
        /// Bez pravidelného kontaktu se vztahy tíhnou k neutrálním hodnotám (Ebbinghausův efekt).
        /// Každá dimenze má jiné tempo:
        /// <list type="bullet">
        ///   <item>Closeness — nejrychlejší decay (vzdálenost se rychle cítí)</item>
        ///   <item>Trust — pomalý decay (jednou získaná důvěra dlouho vydrží)</item>
        ///   <item>Attraction — pomalý, ale tíhne k 35 nebo 45 dle počáteční hodnoty</item>
        /// </list>
        /// </remarks>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            var days = Math.Max(0, dt.TotalDays);
            if (days == 0 || State.Edges.Count == 0)
                return;

            var dict  = new Dictionary<HumanId, RelationshipEdge>(State.Edges);
            var psych = ctx.Snapshot.Psychology;

            // Psychika moduluje decay: dobrá nálada mírně zlepšuje, stres poškozuje
            var valenceEffect = psych.Valence * 0.3 * days;
            var stressEffect  = psych.Stress  * 0.02 * days;

            foreach (var kv in State.Edges)
            {
                var e = kv.Value;
                var d = Config.DecayPerDay * days;

                dict[kv.Key] = e with
                {
                    Like       = Clamp(Approach(e.Like,       50,               d)       + valenceEffect - stressEffect),
                    Trust      = Clamp(Approach(e.Trust,      50,               d * 0.5)),
                    Attraction = Clamp(Approach(e.Attraction, e.Attraction > 50 ? 45 : 35, d * 0.4)),
                    Closeness  = Clamp(Approach(e.Closeness,  35,               d * 1.2)),
                    Respect    = Clamp(Approach(e.Respect,    55,               d * 0.3)),
                    Comfort    = Clamp(Approach(e.Comfort,    45,               d * 0.6) + valenceEffect * 0.5 - stressEffect * 0.5)
                    // Poznámka: Breakdown se v Tick() nemění — domény jsou fixovány zážitky,
                    // ne pouhým uplynutím času.
                };
            }

            State = new RelationshipState(dict);

            using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultRelationshipsEngine))))
            {
                _log.RelDecayApplied(ctx.Id.Value.ToString(), State.Edges.Count, days);
            }
        }

        #endregion Tick — časový decay

        #region RestoreState

        /// <inheritdoc/>
        public void RestoreState(RelationshipState state) => State = state;

        #endregion RestoreState

        #region Privátní metody — DomainBreakdown

        /// <summary>
        /// Aktualizuje <see cref="DomainBreakdown"/> na základě typu řečového aktu.
        /// </summary>
        /// <remarks>
        /// Každý <see cref="SpeechAct"/> ovlivňuje jiné domény:
        /// <list type="table">
        ///   <item><term>SmallTalk</term><description>Humor +1.5</description></item>
        ///   <item><term>Question</term><description>Intellect +2.0</description></item>
        ///   <item><term>SelfDisclosure</term><description>Values +2.0</description></item>
        ///   <item><term>Validation</term><description>Values +1.0</description></item>
        ///   <item><term>Humor</term><description>Humor +2.5</description></item>
        ///   <item><term>Meta</term><description>Intellect +1.0</description></item>
        ///   <item><term>Invite</term><description>Physical +0.5</description></item>
        ///   <item><term>Boundary</term><description>Values –1.0 (vymezení)</description></item>
        /// </list>
        /// Odmítnuté interakce mají efekt poloviční — zážitek je zaznamenán,
        /// ale slabší (postava se uzavřela).
        /// </remarks>
        /// <param name="bd">Stávající breakdown.</param>
        /// <param name="act">Typ aktu z <see cref="InteractionOutcome.Act"/>.</param>
        /// <param name="accepted">Zda byla interakce přijata.</param>
        private static DomainBreakdown ApplyDomainBoost(DomainBreakdown bd, SpeechAct act, bool accepted)
        {
            // Odmítnuté interakce mají poloviční efekt na domény
            var mul = accepted ? 1.0 : 0.5;

            return act switch
            {
                SpeechAct.SmallTalk      => bd with { Humor     = BumpD(bd.Humor,     +1.5 * mul) },
                SpeechAct.Question       => bd with { Intellect = BumpD(bd.Intellect, +2.0 * mul) },
                SpeechAct.SelfDisclosure => bd with { Values    = BumpD(bd.Values,    +2.0 * mul) },
                SpeechAct.Validation     => bd with { Values    = BumpD(bd.Values,    +1.0 * mul) },
                SpeechAct.Humor          => bd with { Humor     = BumpD(bd.Humor,     +2.5 * mul) },
                SpeechAct.Meta           => bd with { Intellect = BumpD(bd.Intellect, +1.0 * mul) },
                SpeechAct.Invite         => bd with { Physical  = BumpD(bd.Physical,  +0.5 * mul) },

                // Boundary — vymezení hranic mírně snižuje Values vnímání
                SpeechAct.Boundary       => bd with { Values    = BumpD(bd.Values,    -1.0 * mul) },

                _ => bd  // neznámý act → beze změny
            };
        }

        #endregion Privátní metody — DomainBreakdown

        #region Privátní metody — pomocné

        /// <summary>
        /// Vytvoří hranu pokud ještě neexistuje (bez jiné mutace).
        /// Odděleno od <see cref="Upsert"/> pro čitelnost switch větví.
        /// </summary>
        private void EnsureEdge(HumanId self, HumanId other)
        {
            if (!State.Edges.ContainsKey(other))
                Upsert(self, other, e => e);
        }

        /// <summary>
        /// Vloží nebo aktualizuje hranu v grafu vztahů.
        /// Pokud hrana neexistuje, inicializuje ji s neutrálními výchozími hodnotami.
        /// </summary>
        private void Upsert(HumanId self, HumanId other, Func<RelationshipEdge, RelationshipEdge> mut)
        {
            var dict = new Dictionary<HumanId, RelationshipEdge>(State.Edges);

            if (!dict.TryGetValue(other, out var e))
            {
                // Neutrální výchozí hodnoty pro neznámého člověka
                e = new RelationshipEdge(
                    A: self, B: other,
                    Like: 45, Trust: 45, Attraction: 35, Closeness: 10, Respect: 55, Comfort: 40,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50));

                using (_log.BeginScope(new CharacterLogScope(self.Value, nameof(DefaultRelationshipsEngine))))
                {
                    _log.RelEdgeCreated(self.Value.ToString(), self.Value.ToString(), other.Value.ToString());
                }
            }

            var updated = mut(e);

            using (_log.BeginScope(new CharacterLogScope(self.Value, nameof(DefaultRelationshipsEngine))))
            {
                _log.RelEdgeUpdated(
                    self.Value.ToString(), self.Value.ToString(), other.Value.ToString(),
                    updated.Like, updated.Trust, updated.Closeness,
                    updated.Attraction, updated.Comfort, updated.Respect);
            }

            dict[other] = updated;
            State = new RelationshipState(dict);
        }

        /// <summary>
        /// Posune hodnotu o <paramref name="by"/> a ořízne na [0, 100].
        /// Používá se pro hlavní dimenze vztahu (Like, Trust…).
        /// </summary>
        private static double Bump(double v, double by)  => Math.Max(0, Math.Min(100, v + by));

        /// <summary>
        /// Posune hodnotu DomainBreakdown o <paramref name="by"/> a ořízne na [0, 100].
        /// Odděleno od <see cref="Bump"/> pro explicitnost — domény mají stejný rozsah.
        /// </summary>
        private static double BumpD(double v, double by) => Math.Max(0, Math.Min(100, v + by));

        /// <summary>
        /// Lineární interpolace — používá se při prvním dojmu pro plynulý přechod.
        /// </summary>
        private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);

        /// <summary>Clamp na rozsah [0, 100].</summary>
        private static double Clamp(double v) => Math.Max(0, Math.Min(100, v));

        /// <summary>
        /// Tíhnutí hodnoty ke středové hodnotě — simuluje zapomínání bez kontaktu.
        /// Pokud je <paramref name="cur"/> pod targetem, hodnota roste, jinak klesá.
        /// </summary>
        private static double Approach(double cur, double target, double amount)
            => cur < target
                ? Math.Min(target, cur + amount)
                : Math.Max(target, cur - amount);

        #endregion Privátní metody — pomocné
    }
}
