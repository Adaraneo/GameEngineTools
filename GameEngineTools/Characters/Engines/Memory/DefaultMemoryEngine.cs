// DefaultMemoryEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Memory
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using static ActionNames;

    /// <summary>
    /// Výchozí implementace paměťového enginu.
    ///
    /// Implementuje tři kognitivně-realistické principy:
    /// <list type="bullet">
    ///   <item><b>Ebbinghausova křivka zapomínání</b> — exponenciální pokles síly paměti,
    ///         nikoli lineární. Čerstvé paměti se rozpadají rychleji než dobře zakotvené.</item>
    ///   <item><b>Konsolidace navázaná na spánek</b> — posílení nejsalientnějších epizod se
    ///         triggeruje událostí <see cref="SleepEvents.SleepEnded"/>, nikoli uplynutím 24h.</item>
    ///   <item><b>Reinforcement (spacing effect)</b> — opakovaný zážitek stejného druhu nekreuje
    ///         duplicitní záznamy, ale posiluje stávající epizodu a aktualizuje její timestamp.</item>
    /// </list>
    /// </summary>
    internal sealed class DefaultMemoryEngine : IMemoryEngine
    {
        #region Stav a konfigurace

        /// <summary>Aktuální stav paměti — seznam epizod a sémantické fakty.</summary>
        public MemoryIndex State { get; private set; }

        /// <summary>Konfigurace enginu (míra zapomínání, boost, práh prořezání).</summary>
        public MemoryConfig Config { get; }

        #endregion

        #region Privátní pole

        private readonly ILogger _log;

        #endregion

        #region Konstruktor

        /// <summary>
        /// Vytvoří instanci <see cref="DefaultMemoryEngine"/>.
        /// </summary>
        /// <param name="cfg">Konfigurace injektovaná přes Options pattern.</param>
        /// <param name="loggerFactory">Továrna na logger — umožňuje scope per postava.</param>
        public DefaultMemoryEngine(IOptions<MemoryConfig> cfg, ILoggerFactory loggerFactory)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger<DefaultMemoryEngine>();

            // Inicializuj prázdný stav — žádné vzpomínky, žádná sémantika
            State = new MemoryIndex(
                new List<EpisodicMemory>(),
                new Dictionary<string, SemanticFact>());
        }

        #endregion

        #region Veřejné API

        /// <summary>
        /// Zakóduje novou epizodu do paměti.
        ///
        /// Pokud epizoda se stejným klíčem <c>What</c> již existuje a je stále silná
        /// (nad prahem prořezání), aplikuje <b>reinforcement</b> — posílí stávající záznam
        /// a aktualizuje jeho timestamp. Tím se modeluje spacing effect:
        /// opakovaný zážitek upevňuje paměť, místo aby plodil duplicity.
        /// </summary>
        /// <param name="episode">Epizoda k zakódování.</param>
        /// <param name="ctx">Kontext postavy (ID, snapshot).</param>
        /// <param name="outbox">Výstupní fronta doménových událostí.</param>
        public void Encode(EpisodicMemory episode, IHumanContext ctx, IEventCollector outbox)
        {
            using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultMemoryEngine))))
            {
                var episodes = State.Episodes.ToList();

                // --- REINFORCEMENT (spacing effect) ---
                // Hledáme existující epizodu se stejným What, která ještě "žije"
                var existingIndex = episodes.FindIndex(
                    e => e.What == episode.What && e.Strength >= Config.PruneThreshold);

                if (existingIndex >= 0)
                {
                    // Posilujeme stávající paměť místo vytváření nového záznamu.
                    // Logika: opakovaný zážitek upevňuje stopu, ale neclampuje nad 1.0.
                    var existing = episodes[existingIndex];
                    var reinforced = existing with
                    {
                        Strength = Math.Min(1.0, existing.Strength + Config.ReinforcementBoost),
                        // Aktualizuj timestamp — "naposledy se to stalo teď"
                        When = episode.When
                    };

                    episodes[existingIndex] = reinforced;
                    State = new MemoryIndex(episodes, State.Semantics);

                    _log.MemoryEncoded(
                        ctx.Id.Value.ToString(),
                        episode.What,
                        reinforced.Strength,
                        reinforced.Emotion.ToString());

                    // Vyzvaň událost i pro reinforcement — Strength je aktualizovaná
                    outbox.Add(new MemoryEncoded(episode.When, ctx.Id, existing.Id, reinforced.Strength, episode.What));
                    return;
                }

                // --- NOVÁ EPIZODA ---
                episodes.Add(episode);
                State = new MemoryIndex(episodes, State.Semantics);

                _log.MemoryEncoded(
                    ctx.Id.Value.ToString(),
                    episode.What,
                    episode.Salience,
                    episode.Emotion.ToString());

                outbox.Add(new MemoryEncoded(episode.When, ctx.Id, episode.Id, episode.Strength, episode.What));
            }
        }

        /// <summary>
        /// Vrátí vzpomínky splňující zadaný predikát.
        /// Používá se z BehaviorEngine pro ovlivnění rozhodování postavy.
        /// </summary>
        /// <param name="predicate">Filtrační podmínka.</param>
        /// <returns>Filtrovaný seznam epizod (read-only snapshot).</returns>
        public IReadOnlyList<EpisodicMemory> Recall(Func<EpisodicMemory, bool> predicate)
            => State.Episodes.Where(predicate).ToList();

        #endregion

        #region Handle — zpracování doménových událostí

        /// <summary>
        /// Reaguje na doménové události z ostatních enginů.
        ///
        /// Zakódované typy událostí:
        /// <list type="bullet">
        ///   <item><see cref="Behavior.ActionCommitted"/> — každá provedená akce se ukládá.</item>
        ///   <item><see cref="InteractionOutcome"/> — výsledky interakcí (přijetí/odmítnutí).</item>
        ///   <item><see cref="Relationships.MicroPositive"/> / <see cref="Relationships.MicroNegative"/> — mikrointerakce.</item>
        ///   <item><see cref="SleepEvents.SleepEnded"/> — triggeruje konsolidaci paměti po spánku.</item>
        /// </list>
        /// </summary>
        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            switch (@event)
            {
                case Behavior.ActionCommitted ac:
                    Encode(
                        new EpisodicMemory(
                            Guid.NewGuid(),
                            ac.OccurredAt,
                            $"Action:{ac.ActionName}",
                            SalienceForAction(ac.ActionName, ctx),
                            EmotionFor(ac.ActionName, ctx.Snapshot.Psychology.Valence),
                            Strength: Config.BaseEncoding),
                        ctx,
                        outbox);
                    break;

                case InteractionOutcome io:
                    var tag = $"Interaction:{io.From}->{io.To}:{io.Reason}";
                    var salience = 0.7 + (io.Accepted ? 0.2 : 0.0);
                    Encode(
                        new EpisodicMemory(
                            Guid.NewGuid(),
                            io.OccurredAt,
                            tag,
                            salience,
                            io.Accepted ? EmotionalTag.Positive : EmotionalTag.Negative,
                            Strength: Config.BaseEncoding + 0.2),
                        ctx,
                        outbox);
                    break;

                // Mikrokladná interakce (např. pozdrav, pomoc)
                case Relationships.MicroPositive mp:
                    Encode(
                        new EpisodicMemory(
                            Guid.NewGuid(),
                            mp.OccurredAt,
                            $"Micro+:{mp.A}->{mp.B}:{mp.What}",
                            0.6,
                            EmotionalTag.Positive,
                            Strength: Config.BaseEncoding),
                        ctx,
                        outbox);
                    break;

                // Mikrozáporná interakce (např. urážka, ignorování)
                case Relationships.MicroNegative mn:
                    Encode(
                        new EpisodicMemory(
                            Guid.NewGuid(),
                            mn.OccurredAt,
                            $"Micro-:{mn.A}->{mn.B}:{mn.What}",
                            0.6,
                            EmotionalTag.Negative,
                            Strength: Config.BaseEncoding),
                        ctx,
                        outbox);
                    break;

                case SleepEnded se:
                    ConsolidateMemories(se.OccurredAt, ctx, outbox);
                    break;
            }
        }

        #endregion

        #region Tick — zapomínání (Ebbinghausova křivka)

        /// <summary>
        /// Volá se každý herní tick. Aplikuje exponenciální pokles síly paměti
        /// a prořezává vzpomínky pod prahem.
        ///
        /// <b>Proč exponenciální, ne lineární?</b>
        /// Ebbinghausova křivka zapomínání ukazuje, že paměti se rozpadají
        /// nejrychleji krátce po zakódování a pak stále pomaleji. Exponenciální
        /// funkce <c>e^(-k*t)</c> toto chování přesně modeluje:
        /// silná paměť (Strength=1.0) se rozpadá pomalu,
        /// slabá (Strength=0.1) zmizí rychle.
        /// </summary>
        /// <param name="now">Aktuální herní čas.</param>
        /// <param name="dt">Délka ticku.</param>
        /// <param name="ctx">Kontext postavy.</param>
        /// <param name="outbox">Výstupní fronta událostí.</param>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            var hours = Math.Max(0, dt.TotalHours);

            var episodes = State.Episodes.ToList();

            for (int i = 0; i < episodes.Count; i++)
            {
                var e = episodes[i];

                var emotionMod = e.Emotion switch
                {
                    EmotionalTag.Negative => Config.EmotionDecayMod,
                    EmotionalTag.Mixed => Config.EmotionDecayMod + 0.2,
                    EmotionalTag.Positive => Config.EmotionDecayMod + 0.3,
                    _ => 1.0
                };

                var decayFactor = Math.Exp(-Config.ForgettingRate * emotionMod * (hours / 24.0));
                var newStrength = Math.Max(0.0, e.Strength * decayFactor);

                episodes[i] = e with { Strength = newStrength };
            }

            // Prořezání — odstraní epizody pod prahem, aby paměť nerostla donekonečna
            episodes = episodes
                .Where(e => e.Strength >= Config.PruneThreshold)
                .ToList();

            State = new MemoryIndex(episodes, State.Semantics);
        }

        #endregion

        #region Obnovení stavu

        /// <summary>
        /// Obnoví stav enginu ze snapshotu (např. při načítání uložené hry).
        /// </summary>
        /// <param name="state">Předchozí stav paměti.</param>
        public void RestoreState(MemoryIndex state) => State = state;

        #endregion

        #region Privátní metody

        /// <summary>
        /// Konsoliduje paměti po skončení spánku.
        ///
        /// Neurovědní základ: REM fáze spánku posiluje epizodické paměti s vysokou
        /// salience — tedy zážitky, které byly emocionálně nebo situačně důležité.
        /// Implementace posiluje 10 epizod s nejvyšší salience o <see cref="MemoryConfig.SleepConsolidationBoost"/>.
        /// </summary>
        /// <param name="at">Čas konce spánku.</param>
        /// <param name="ctx">Kontext postavy.</param>
        /// <param name="outbox">Výstupní fronta událostí.</param>
        private void ConsolidateMemories(WDateTime at, IHumanContext ctx, IEventCollector outbox)
        {
            var episodes = State.Episodes.ToList();

            // Vyber Top 10 epizod podle salience — ty REM fáze upevňuje nejvíc
            var toBoost = episodes
                .OrderByDescending(e => e.Salience)
                .Take(10)
                .Select(e => e with
                {
                    Strength = Math.Min(1.0, e.Strength + Config.SleepConsolidationBoost)
                })
                .ToList();

            // Merge zpět do seznamu (Dictionary zaručí O(1) lookup per epizoda)
            var lookup = episodes.ToDictionary(e => e.Id);
            foreach (var boosted in toBoost)
            {
                lookup[boosted.Id] = boosted;
            }

            State = new MemoryIndex(lookup.Values.ToList(), State.Semantics);

            outbox.Add(new MemoryConsolidated(at, ctx.Id, toBoost.Count));

            using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultMemoryEngine))))
            {
                _log.MemoryConsolidated(ctx.Id.Value.ToString(), toBoost.Count);
            }
        }

        /// <summary>
        /// Určí salience (důležitost) epizody na základě typu akce.
        /// Intimní a sociální akce mají vyšší salience — jsou emocionálně výraznější.
        /// </summary>
        /// <param name="actionName">Název akce z <see cref="ActionNames"/>.</param>
        /// <param name="ctx">Kontext postavy (pro budoucí rozšíření).</param>
        /// <returns>Hodnota salience v rozsahu 0.0–1.0.</returns>
        private static double SalienceForAction(string actionName, IHumanContext ctx)
            => actionName switch
            {
                InviteIntimacy => 0.9,  // Nejvyšší — intimní interakce jsou výrazné
                ReachOut       => 0.6,  // Sociální kontakt
                Eat            => 0.5,  // Biologická potřeba
                Sleep          => 0.4,  // Rutina — nízká salience
                Drink          => 0.3,  // Rutina
                _              => 0.5   // Výchozí pro neznámé akce
            };

        /// <summary>
        /// Přiřadí emoci epizodě na základě typu akce nebo aktuální valence postavy.
        /// Pokud akce nemá pevnou emoci, rozhoduje psychologická valence ze snapshotu.
        /// </summary>
        /// <param name="actionName">Název akce.</param>
        /// <param name="valence">Aktuální psychologická valence postavy (-1.0 až 1.0).</param>
        /// <returns>Emocionální tag pro epizodu.</returns>
        private static EmotionalTag EmotionFor(string actionName, double valence)
            => actionName switch
            {
                InviteIntimacy or ReachOut or SelfCare => EmotionalTag.Positive,
                Flee           or Fight                => EmotionalTag.Negative,
                _ => valence switch
                {
                    > 0.05  => EmotionalTag.Positive,
                    < -0.05 => EmotionalTag.Negative,
                    _       => EmotionalTag.Neutral
                }
            };

        #endregion
    }
}
