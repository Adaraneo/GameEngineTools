// DefaultInteractionEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interactions
{
    using System;
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Výchozí implementace <see cref="IInteractionEngine"/>.
    /// </summary>
    /// <remarks>
    /// Engine reaguje na dvě kategorie událostí:
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="ContextChanged"/> — aktualizuje prostředí interakce
    ///     (lokace, soukromí, hluk, dav).
    ///   </item>
    ///   <item>
    ///     <see cref="InteractionProposed"/> — vyhodnotí pravděpodobnost přijetí
    ///     na základě vztahů, psychiky a prostředí. Výsledkem je <see cref="InteractionOutcome"/>.
    ///   </item>
    /// </list>
    /// <para>
    /// <b>Proč <c>Tick()</c> nic nedělá?</b><br/>
    /// Interakce jsou reaktivní — spouštějí se vždy událostí (hráč nebo NPC iniciuje kontakt),
    /// nikoli uplynutím času. Engine nemá co "tikat" sám od sebe.
    /// </para>
    /// </remarks>
    internal sealed class DefaultInteractionEngine : IInteractionEngine
    {
        #region Stav a konfigurace

        /// <inheritdoc/>
        public InteractionSurface State { get; private set; }

        /// <inheritdoc/>
        public InteractionConfig Config { get; }

        #endregion Stav a konfigurace

        #region Privátní pole

        private readonly ILogger _log;

        #endregion Privátní pole

        #region Konstruktor

        /// <summary>
        /// Vytvoří instanci <see cref="DefaultInteractionEngine"/>.
        /// Počáteční stav prostředí je neutrální — neznámá lokace, střední hluk a dav.
        /// </summary>
        public DefaultInteractionEngine(IOptions<InteractionConfig> cfg, ILoggerFactory loggerFactory)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger<DefaultInteractionEngine>();

            // Bezpečný výchozí stav — než hra nastaví reálný kontext přes ContextChanged
            State = new InteractionSurface(Location: "Unknown", HasPrivacy: false, Noise: 0.5, Crowding: 0.5);
        }

        #endregion Konstruktor

        #region Tick

        /// <inheritdoc/>
        /// <remarks>Interakce jsou čistě reaktivní — bez aktivního prostředí se nic neděje.</remarks>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            // Bez aktivního prostředí neděláme nic periodického.
        }

        #endregion Tick

        #region Handle — zpracování doménových událostí

        /// <inheritdoc/>
        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            switch (@event)
            {
                case ContextChanged cc:
                    HandleContextChanged(cc, ctx);
                    break;

                case InteractionProposed p when p.To == ctx.Id:
                    HandleInteractionProposed(p, ctx, outbox);
                    break;

                case TouchAttempted t when t.To == ctx.Id:
                    HandleTouchAttempted(t, ctx, outbox);
                    break;
            }
        }

        private void HandleTouchAttempted(TouchAttempted attempted, IHumanContext ctx, IEventCollector outbox)
        {
            var rels = ctx.Snapshot.Relationships.Edges;
            rels.TryGetValue(attempted.From, out var edge);

            var closeness = edge?.Closeness ?? 0;
            var attraction = edge?.Attraction ?? 0;
            var comfort = edge?.Comfort ?? 0;
            var psych = ctx.Snapshot.Psychology;

            // Pravděpodobnost přijení závisí silně na úrovni dotyku
            var baseP = attempted.Level switch
            {
                TouchLevel.Light => 0.5 + closeness * 0.005,
                TouchLevel.Friendly => 0.3 + closeness * 0.007 + comfort * 0.003,
                TouchLevel.Intimate => 0.05 + closeness * 0.006 + attraction * 0.007,
                _ => 0
            };

            // Soukromí a psychika modulují
            baseP += State.HasPrivacy ? 0.05 : -0.05;
            baseP -= psych.Stress * 0.002;
            baseP += Math.Max(0, psych.Valence) * 0.05;

            var pAcc = Math.Clamp(baseP, 0, 0.95);
            var accepted = ctx.Random.Chance(pAcc);

            // Intimate bez dostatečné Closeness/Attraction vždy odmítnuto
            if (attempted.Level == TouchLevel.Intimate && (closeness < 60 || attraction < 55))
            {
                accepted = false;
            }

            outbox.Add(new TouchOutcome(attempted.OccurredAt, attempted.From, attempted.To, attempted.Level, accepted, accepted ? "accepted" : "declined"));
        }

        /// <summary>
        /// Aktualizuje prostředí postavy na základě nové lokace / kontextu.
        /// Hodnoty Noise a Crowding jsou clampovány na [0, 1].
        /// </summary>
        private void HandleContextChanged(ContextChanged cc, IHumanContext ctx)
        {
            using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultInteractionEngine))))
            {
                _log.InteractionContextChanged(ctx.Id.Value.ToString(), cc.Location, cc.Noise, cc.Crowding);
            }

            State = new InteractionSurface(
                Location: cc.Location,
                HasPrivacy: cc.HasPrivacy,
                Noise: Math.Clamp(cc.Noise, 0, 1),
                Crowding: Math.Clamp(cc.Crowding, 0, 1));
        }

        /// <summary>
        /// Vyhodnotí navrhovanou interakci a vyemituje <see cref="InteractionOutcome"/>.
        /// </summary>
        /// <remarks>
        /// Pravděpodobnost přijetí závisí na:
        /// <list type="bullet">
        ///   <item>Vztazích: Closeness, Comfort, Trust (čím blíž, tím ochotněji)</item>
        ///   <item>Psychice: Valence (dobrá nálada otevírá), Stress (stres uzavírá)</item>
        ///   <item>Prostředí: soukromí pomáhá, dav a hluk překáží</item>
        ///   <item>Misattribution: stres způsobuje chybné čtení záměrů</item>
        /// </list>
        /// <para>
        /// <b>Důležité:</b> <see cref="SpeechAct"/> se přenáší beze změny do <see cref="InteractionOutcome"/>,
        /// aby <c>RelationshipsEngine</c> věděl, jakou doménu aktualizovat.
        /// </para>
        /// </remarks>
        private void HandleInteractionProposed(InteractionProposed p, IHumanContext ctx, IEventCollector outbox)
        {
            var rels = ctx.Snapshot.Relationships.Edges;
            rels.TryGetValue(p.From, out var edge);

            // Vztahové vstupy — neznámý člověk dostane neutrální výchozí hodnoty
            var closeness = edge?.Closeness ?? 30;
            var comfort   = edge?.Comfort   ?? 30;
            var trust     = edge?.Trust     ?? 30;

            var psych = ctx.Snapshot.Psychology;

            // Základní pravděpodobnost přijetí — lineární kombinace vztahů a psychiky
            var baseP = 0.30
                        + 0.0025 * closeness
                        + 0.0020 * comfort
                        + 0.0020 * trust
                        + 0.10   * Math.Max(0, psych.Valence)
                        + (State.HasPrivacy ? 0.05 : 0)
                        - 0.05   * State.Crowding
                        - 0.0015 * psych.Stress;

            // Misattribution: vyšší stres → větší šance na špatné čtení záměru
            var misattrib = Config.MisattributionRateBase * (psych.Stress / 100.0);
            baseP -= misattrib;

            var pAcc     = Math.Clamp(baseP, 0.05, 0.95);
            var accepted = ctx.Random.Chance(pAcc);

            using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultInteractionEngine))))
            {
                _log.InteractionOutcomeDecided(
                    ctx.Id.Value.ToString(),
                    p.From.Value.ToString(),
                    p.To.Value.ToString(),
                    pAcc,
                    accepted ? "PŘIJATO" : "ODMÍTNUTO");
            }

            // Přenášíme p.Act — RelationshipsEngine ho potřebuje pro DomainBreakdown
            outbox.Add(new InteractionOutcome(
                OccurredAt: p.OccurredAt,
                From:        p.From,
                To:          p.To,
                Accepted:    accepted,
                Reason:      accepted ? "accepted" : "declined",
                Act:         p.Act));
        }

        #endregion Handle — zpracování doménových událostí

        #region RestoreState

        /// <inheritdoc/>
        public void RestoreState(InteractionSurface state) => State = state;

        #endregion RestoreState
    }
}
