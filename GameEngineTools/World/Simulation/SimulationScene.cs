// SimulationScene.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Simulation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Narrative;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Obecná simulační scéna pro libovolný počet postav.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Zodpovědnosti scény:</b>
    /// <list type="bullet">
    ///   <item>Správné pořadí tickování postav (v pořadí <see cref="SimulationSceneOptions.Characters"/>)</item>
    ///   <item>Automatické routování <see cref="InteractionOutcome"/> zpět k iniciátorovi</item>
    ///   <item>Zpracování sleep promptů dle strategie per-postava</item>
    ///   <item>Posun herních hodin o <see cref="SimulationSceneOptions.ClockAdvance"/> každou iteraci</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Co scéna záměrně NEDĚLÁ:</b>
    /// <list type="bullet">
    ///   <item>Neřeší routing <c>ReachOut → InteractionProposed</c> — to je scénář volající vrstvy</item>
    ///   <item>Neexportuje data, nevypisuje hlavičky — to patří do GameSandbox / Unity</item>
    ///   <item>Nezná rozdíl mezi hráčem a NPC — všechny postavy jsou rovnocenné</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Pořadí kroků v každé iteraci:</b>
    /// <code>
    /// 1. OnTick callback      — scénář + ReachOut routing (vidí LastOutbox z předchozího ticku)
    /// 2. Tick každé postavy   — enginy přepočítají stav
    /// 3. Route outcomes       — InteractionOutcome → iniciátor dostane odpověď
    /// 4. Sleep prompty        — per-postava dle SleepPromptHandlers
    /// 5. ClockAdvance         — hodiny posunuty vpřed
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class SimulationScene
    {
        #region Privátní pole

        /// <summary>Herní hodiny — vyžaduje <c>Advance()</c> pro posun simulačního času.</summary>
        private readonly SystemClock _clock;

        /// <summary>Konfigurace scény předaná zvenku.</summary>
        private readonly SimulationSceneOptions _options;

        private readonly ICognitiveResolutionLevelRuntime _lodRuntime;

        #endregion Privátní pole

        #region Konstruktor

        /// <summary>
        /// Vytvoří novou instanci <see cref="SimulationScene"/>.
        /// </summary>
        /// <param name="clock">
        /// Herní hodiny. Musí být <see cref="SystemClock"/> —
        /// scéna potřebuje <c>Advance()</c> pro řízení simulačního času.
        /// </param>
        /// <param name="options">Konfigurace scény — postavy, časování, handlery.</param>
        /// <exception cref="ArgumentNullException">
        /// Pokud je <paramref name="clock"/> nebo <paramref name="options"/> null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Pokud <see cref="SimulationSceneOptions.Characters"/> je prázdný seznam.
        /// </exception>
        public SimulationScene(SystemClock clock, SimulationSceneOptions options, ICognitiveResolutionLevelRuntime characterLodRuntime)
        {
            ArgumentNullException.ThrowIfNull(clock);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(characterLodRuntime);

            if (options.Characters.Count == 0)
                throw new ArgumentException(
                    "SimulationScene vyžaduje alespoň jednu postavu v Characters.", nameof(options));

            _clock = clock;
            _options = options;
            _lodRuntime = characterLodRuntime;
        }

        #endregion Konstruktor

        #region Veřejné API

        /// <summary>
        /// Spustí simulaci asynchronně.
        /// Skončí po uplynutí <see cref="SimulationSceneOptions.SimulationYears"/>.
        /// </summary>
        /// <returns>Dokončená task po skončení simulace.</returns>
        public Task RunAsync()
            => SimulateAsync();

        #endregion Veřejné API

        #region Simulační smyčka

        /// <summary>
        /// Hlavní simulační smyčka — iteruje dokud neuplyne nastavený počet herních let.
        /// </summary>
        private Task SimulateAsync()
        {
            var startTime = _clock.Now;
            var endTime = _clock.Now.AddYears(_options.SimulationYears);
            var chars = _options.Characters;
            var macroStep = _options.TickStep;
            var internalSubstep = ResolveInternalSubstep(macroStep);

            while (_clock.Now < endTime)
            {
                var macroRemaining = macroStep;

                while (macroRemaining > WTimeSpan.Zero && _clock.Now < endTime)
                {
                    var remainingToEnd = endTime - _clock.Now;
                    var dt = WTimeSpan.Min(internalSubstep, WTimeSpan.Min(macroRemaining, remainingToEnd));
                    SimulateSingleStep(startTime, chars, dt);
                    macroRemaining -= dt;
                }
            }

            return Task.CompletedTask;
        }

        private void SimulateSingleStep(WDateTime startTime, IReadOnlyList<IHuman> chars, WTimeSpan dt)
        {
            ApplyCharacterLods(chars);

            var now = _clock.Now;

            _options.LocationService?.DispatchContextEvents(now, chars, forceAll: now == startTime);

            // ── Krok 1: OnTick callback ─────────────────────────────────────────────
            // Zavolej scénář / ReachOut routing.
            // POZOR: LastOutbox každé postavy je zde stále z PŘEDCHOZÍHO ticku —
            // proto je to správné místo pro detekci ReachOut a routování.
            _options.OnTick?.Invoke(now, chars);

            // ── Krok 2: Tick všech postav + okamžité routování outcomes ────────────
            foreach (var character in chars)
            {
                character.Tick(now, dt);
                RouteOutcomes(character, chars);
            }

            // --- Narrative scan ----
            if (_options.NarrativeFormatter is { } formatter && _options.OnNarrative is { } onNarrative)
            {
                var resolver = _options.ResolveCharacter ?? (id => new NarrativeCharacterInfo(id.Value.ToString(), SexBiology.Unknown));

                foreach (var character in chars)
                {
                    foreach (var ev in character.LastOutbox)
                    {
                        var entry = formatter.Format(ev, resolver);
                        if (entry is not null)
                        {
                            onNarrative(entry);
                        }
                    }
                }
            }

            // ── Krok 3: Sleep prompty ───────────────────────────────────────────────
            foreach (var character in chars)
            {
                HandleSleepPrompt(now, character);
            }

            // ── Krok 4: Posun hodin ────────────────────────────────────────────────
            _clock.Advance(dt);
        }

        private WTimeSpan ResolveInternalSubstep(WTimeSpan macroStep)
        {
            var configured = _options.InternalSubstep;
            if (configured is null || configured.Value <= WTimeSpan.Zero)
            {
                return macroStep;
            }

            return WTimeSpan.Min(configured.Value, macroStep);
        }

        #endregion Simulační smyčka

        #region Routování výstupů

        /// <summary>
        /// Routuje <see cref="InteractionOutcome"/> z outboxu odesílatele
        /// zpět k iniciátorovi interakce.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Proč routujeme outcome zpět k <c>From</c>?<br/>
        /// <c>InteractionEngine</c> postavy B zpracuje <c>InteractionProposed</c> od A
        /// a vydá <c>InteractionOutcome</c> v outboxu B.
        /// Aby A věděla, zda byla přijata nebo odmítnuta, musíme outcome doručit A.
        /// </para>
        /// <para>
        /// <see cref="IHuman.ReceiveEvent"/> je synchronní enqueue —
        /// A zpracuje outcome na začátku svého <em>příštího</em> ticku.
        /// </para>
        /// </remarks>
        /// <param name="sender">Postava, jejíž outbox právě prohledáváme.</param>
        /// <param name="all">Všechny postavy ve scéně pro lookup iniciátora.</param>
        private static void RouteOutcomes(IHuman sender, IReadOnlyList<IHuman> all)
        {
            foreach (var outcome in sender.LastOutbox.OfType<InteractionOutcome>())
            {
                // Najdi iniciátora — toho kdo původně odeslal InteractionProposed
                var initiator = all.FirstOrDefault(c => c.Id == outcome.From);
                initiator?.ReceiveEvent(outcome);
            }

            // Touch outcome
            foreach (var outcome in sender.LastOutbox.OfType<TouchOutcome>())
            {
                var initiator = all.FirstOrDefault(c => c.Id == outcome.From);
                initiator?.ReceiveEvent(outcome);
            }

            foreach (var outcome in sender.LastOutbox.OfType<SexualEncounterOutcome>())
            {
                var initiator = all.FirstOrDefault(c => c.Id == outcome.From);
                initiator?.ReceiveEvent(outcome);
            }
        }

        #endregion Routování výstupů

        #region Sleep handling

        /// <summary>
        /// Zpracuje sleep prompt postavy dle nakonfigurované strategie.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Strategie dle <see cref="SimulationSceneOptions.SleepPromptHandlers"/>:
        /// <list type="bullet">
        ///   <item>
        ///     Handler nalezen pro postavu → zavolá callback, výsledek (<c>bool</c>)
        ///     rozhodne zda potvrdit nebo odmítnout.
        ///   </item>
        ///   <item>
        ///     Handler nenalezen → automatické potvrzení (NPC default).
        ///   </item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <param name="now">Aktuální herní čas.</param>
        /// <param name="character">Postava, jejíž outbox prohledáváme.</param>
        private void HandleSleepPrompt(WDateTime now, IHuman character)
        {
            var prompt = character.LastOutbox.OfType<SleepPromptRequested>().FirstOrDefault();
            if (prompt == null)
                return;

            // Zjisti, zda má tato postava vlastní handler
            var shouldSleep = true; // výchozí: auto-potvrdit (NPC chování)

            if (_options.SleepPromptHandlers != null
                && _options.SleepPromptHandlers.TryGetValue(character.Id, out var handler))
            {
                shouldSleep = handler(prompt);
            }

            if (shouldSleep)
            {
                // Potvrď spánek — engine sám vypočítá délku na základě dluhu
                character.ReceiveEvent(new SleepConfirmed(
                    OccurredAt: now,
                    Human: prompt.Human,
                    PlannedWakeUp: default));
            }
            else
            {
                // Odmítni spánek — engine zaloguje penalizaci
                character.ReceiveEvent(new SleepDeclined(
                    OccurredAt: now,
                    Human: prompt.Human,
                    character.Snapshot.Behavior.SleepDeclineCount));
            }
        }

        #endregion Sleep handling

        #region Private Helpers

        private void ApplyCharacterLods(IReadOnlyList<IHuman> characters)
        {
            foreach (var c in characters)
            {
                var lod = _options.ResolveCharacterLod?.Invoke(c) ?? _options.DefaultCharacterLod;
                _lodRuntime.Set(c.Id, lod);
            }
        }

        #endregion
    }
}
