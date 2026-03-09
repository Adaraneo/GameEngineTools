// InteractionScene.cs
// Copyright (c) 50PSoftware

using GameEngineTools;
using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Sleep;
using GameEngineTools.Characters.GameObjects;
using GameEngineTools.Extensions;
using GameEngineTools.FileSystem;
using GameEngineTools.World.Core.Time;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.DependencyInjection;
using NPC = GameEngineTools.Characters.GameObjects.NPC;

namespace GameSandbox.Scenes
{
    /// <summary>
    /// Obecná sandbox scéna pro simulaci interakcí mezi postavami.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scéna sama o sobě neví NIC o tom, jaké interakce mají proběhnout.
    /// Veškerý "scénář" (co se stane v jaký okamžik) předáváš zvenku přes
    /// <see cref="InteractionSceneOptions.OnTick"/> callback.
    /// </para>
    /// <para>
    /// Scéna se stará pouze o:
    /// <list type="bullet">
    ///   <item>Správné pořadí tickování (NPC → hráč)</item>
    ///   <item>Přeposílání výstupů mezi postavami (InteractionOutcome, ReachOut)</item>
    ///   <item>Sleep handling dle zvolené strategie</item>
    ///   <item>Export výsledků po skončení simulace</item>
    /// </list>
    /// </para>
    /// <para>
    /// Příklad použití:
    /// <code>
    /// var scene = new InteractionScene(runtime, gameTimePath, new InteractionSceneOptions
    /// {
    ///     Player          = player,
    ///     Npc             = significantOther,
    ///     SimulationYears = 2,
    ///     OnTick = (now, p, npc) =>
    ///     {
    ///         if (now.Day is 2 or 6 or 12)
    ///             npc.ReceiveEvent(new InteractionProposed(now, p.Id, npc.Id, SpeechAct.SmallTalk, "Ahoj!"));
    ///     },
    ///     PlayerSleepHandling = SleepHandling.Manual,
    ///     OnSleepPrompt = _ => {
    ///         Console.WriteLine("Jít spát? (A/N)");
    ///         return Console.ReadKey(true).Key == ConsoleKey.A;
    ///     }
    /// });
    /// await scene.RunAsync();
    /// </code>
    /// </para>
    /// </remarks>
    internal sealed class InteractionScene
    {
        #region Privátní pole

        /// <summary>Herní runtime — DI kontejner, clock, manager.</summary>
        private readonly GameEngineToolsRuntimeHandle _runtime;

        /// <summary>Systémové hodiny — ruční posun herního času.</summary>
        private readonly SystemClock _clock;

        /// <summary>Generátor/importér/exportér postav.</summary>
        private readonly GeneratedFile _gf;

        /// <summary>Cesta k souboru s uloženým herním časem.</summary>
        private readonly string _gameTimePath;

        /// <summary>Konfigurace scény předaná zvenku.</summary>
        private readonly InteractionSceneOptions _options;

        #endregion

        #region Konstruktor

        /// <summary>
        /// Vytvoří novou instanci <see cref="InteractionScene"/>.
        /// </summary>
        /// <param name="runtime">Běžící herní runtime.</param>
        /// <param name="gameTimePath">Cesta pro persistenci herního času.</param>
        /// <param name="options">
        /// Konfigurace scény — účastníci, délka simulace, scénář a sleep handling.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Pokud je <paramref name="options"/> null.
        /// </exception>
        public InteractionScene(
            GameEngineToolsRuntimeHandle runtime,
            string gameTimePath,
            InteractionSceneOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            _runtime      = runtime;
            _clock        = (SystemClock)runtime.Clock;
            _gf           = (GeneratedFile)runtime.Services.GetRequiredService<IGeneratedFile>();
            _gameTimePath = gameTimePath;
            _options      = options;
        }

        #endregion

        #region Veřejné metody

        /// <summary>
        /// Spustí scénu — vypíše hlavičku, simuluje čas a exportuje výsledky.
        /// </summary>
        public async Task RunAsync()
        {
            var playerPerson = _options.Player.Person;
            var npcPerson = _options.Npc.Person;

            PrintHeader();
            PressAnyKeyToContinue();

            await SimulateAsync(playerPerson, npcPerson);

            Export();
        }

        #endregion

        #region Simulační smyčka

        /// <summary>
        /// Hlavní simulační smyčka — iteruje tickem dokud neuplyne <see cref="InteractionSceneOptions.SimulationYears"/> herních let.
        /// </summary>
        /// <param name="playerPerson">Orchestrovaná osoba hráče.</param>
        /// <param name="npcPerson">Orchestrovaná osoba NPC.</param>
        private Task SimulateAsync(IHuman playerPerson, IHuman npcPerson)
        {
            var endTime = _clock.Now.AddYears(_options.SimulationYears);

            while (_clock.Now < endTime)
            {
                var now = _clock.Now;

                // 1. Zavolej uživatelský scénář — tam jsou všechny plánované eventy
                _options.OnTick?.Invoke(now, playerPerson, npcPerson);

                // 2. Tick NPC → výstupy přeposli hráči
                TickNpc(now, _options.TickStep, playerPerson, npcPerson);

                // 3. Tick hráče → výstupy přeposli NPC
                TickPlayer(now, _options.TickStep, playerPerson, npcPerson);

                // 4. Sleep handling dle zvolené strategie
                HandleNpcSleepPrompt(now, npcPerson);
                HandlePlayerSleepPrompt(now, playerPerson);

                // 5. Posuň hodiny o nakonfigurovaný krok
                _clock.Advance(_options.ClockAdvance);
                Console.WriteLine("now: {0}", _clock.Now.ToString());
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Tickuje NPC a přeposílá jeho výstupy hráči.
        /// </summary>
        /// <param name="now">Aktuální herní čas.</param>
        /// <param name="dt">Délka herního kroku.</param>
        /// <param name="playerPerson">Příjemce výstupů NPC.</param>
        /// <param name="npcPerson">NPC ke tickování.</param>
        private static void TickNpc(WDateTime now, WTimeSpan dt, IHuman playerPerson, IHuman npcPerson)
        {
            npcPerson.Tick(now, dt);

            // NPC samo iniciuje ReachOut → přepošleme hráči jako SmallTalk
            var reachOut = npcPerson.LastOutbox
                .OfType<ActionCommitted>()
                .FirstOrDefault(a => a.ActionName == "ReachOut");

            if (reachOut != null)
            {
                playerPerson.ReceiveEvent(new InteractionProposed(
                    OccurredAt: now,
                    From:       npcPerson.Id,
                    To:         playerPerson.Id,
                    Act:        SpeechAct.SmallTalk,
                    Content:    "Ehm... Ahoj"));
            }

            // Výsledek interakce NPC → hráč
            var outcome = npcPerson.LastOutbox.OfType<InteractionOutcome>().FirstOrDefault();
            if (outcome != null)
                playerPerson.ReceiveEvent(outcome);
        }

        /// <summary>
        /// Tickuje hráče a přeposílá jeho výstupy NPC.
        /// </summary>
        /// <param name="now">Aktuální herní čas.</param>
        /// <param name="dt">Délka herního kroku.</param>
        /// <param name="playerPerson">Hráč ke tickování.</param>
        /// <param name="npcPerson">Příjemce výstupů hráče.</param>
        private static void TickPlayer(WDateTime now, WTimeSpan dt, IHuman playerPerson, IHuman npcPerson)
        {
            playerPerson.Tick(now, dt);

            // Výsledek interakce hráče → NPC
            var playerOutcome = playerPerson.LastOutbox.OfType<InteractionOutcome>().FirstOrDefault();
            if (playerOutcome != null)
                npcPerson.ReceiveEvent(playerOutcome);

            // Hráčův ReachOut → NPC dostane SmallTalk, okamžitě tickneme
            var playerReachOut = playerPerson.LastOutbox
                .OfType<ActionCommitted>()
                .FirstOrDefault(a => a.ActionName == "ReachOut");

            if (playerReachOut != null)
            {
                npcPerson.ReceiveEvent(new InteractionProposed(
                    OccurredAt: now,
                    From:       playerPerson.Id,
                    To:         npcPerson.Id,
                    Act:        SpeechAct.SmallTalk,
                    Content:    "Ehm... ahoj."));

                npcPerson.Tick(now, dt);

                var npcOutcome = npcPerson.LastOutbox.OfType<InteractionOutcome>().FirstOrDefault();
                if (npcOutcome != null)
                    playerPerson.ReceiveEvent(npcOutcome);
            }
        }

        /// <summary>
        /// Zpracuje sleep prompt NPC — vždy automaticky potvrdí.
        /// </summary>
        /// <param name="now">Aktuální herní čas.</param>
        /// <param name="npcPerson">NPC jehož sleep prompt zpracováváme.</param>
        private static void HandleNpcSleepPrompt(WDateTime now, IHuman npcPerson)
        {
            var prompt = npcPerson.LastOutbox.OfType<SleepPromptRequested>().FirstOrDefault();
            if (prompt == null) return;

            // NPC nemá UI — spánek vždy potvrdíme, engine vypočítá délku sám (default)
            npcPerson.ReceiveEvent(new SleepConfirmed(
                OccurredAt:    now,
                Human:         prompt.Human,
                PlannedWakeUp: default));
        }

        /// <summary>
        /// Zpracuje sleep prompt hráče dle <see cref="InteractionSceneOptions.PlayerSleepHandling"/>.
        /// </summary>
        /// <param name="now">Aktuální herní čas.</param>
        /// <param name="playerPerson">Hráč jehož sleep prompt zpracováváme.</param>
        private void HandlePlayerSleepPrompt(WDateTime now, IHuman playerPerson)
        {
            var prompt = playerPerson.LastOutbox.OfType<SleepPromptRequested>().FirstOrDefault();
            if (prompt == null) return;

            IDomainEvent response;

            if (_options.PlayerSleepHandling == SleepHandling.Manual && _options.OnSleepPrompt != null)
            {
                // Předáme rozhodnutí volajícímu — ten má UI, konzoli, co chce
                var goToSleep = _options.OnSleepPrompt(prompt);

                response = goToSleep
                    ? new SleepConfirmed(now, prompt.Human, default)
                    : new SleepDeclined(now, prompt.Human, DeclineCount: playerPerson.Snapshot.Behavior.SleepDeclineCount);
            }
            else
            {
                // Auto režim — potvrdíme automaticky (sandbox bez UI)
                response = new SleepConfirmed(now, prompt.Human, default);
            }

            playerPerson.ReceiveEvent(response);
        }

        #endregion

        #region Export a výstup

        /// <summary>
        /// Exportuje obě postavy, vypíše výsledky do konzole a persistuje herní čas.
        /// </summary>
        private void Export()
        {
            PressAnyKeyToContinue(clear: true);

            _gf.Export((PC)_options.Player);
            _gf.Export((NPC)_options.Npc);

            Console.WriteLine(_options.Player.PrintInfo(false));
            Console.WriteLine(_options.Npc.PrintInfo(false));

            PressAnyKeyToContinue();

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            File.WriteAllText(
                Path.Combine(desktop, $"player.{_options.Player.Person.Id.Value}.log.txt"),
                _options.Player.PrintInfo(false));

            File.WriteAllText(
                Path.Combine(desktop, $"npc.{_options.Npc.Person.Id.Value}.log.txt"),
                _options.Npc.PrintInfo(false));

            File.WriteAllText(_gameTimePath, _clock.Now.WorldTicks.ToString());
        }

        #endregion

        #region Konzolové utility

        /// <summary>Vypíše hlavičku — aktuální čas a info o postavách.</summary>
        private void PrintHeader()
        {
            Console.WriteLine("Now: {0}", _clock.Now.ToString());
            Console.WriteLine("Player: {0}", _options.Player.PrintInfo(true));
            Console.WriteLine("Npc: {0}", _options.Npc.PrintInfo(true));
            Console.WriteLine("==========================================================");
        }

        /// <summary>Pozastaví běh a čeká na stisk klávesy.</summary>
        /// <param name="clear">Pokud <c>true</c>, vymaže konzoli po stisku.</param>
        private static void PressAnyKeyToContinue(bool clear = false)
        {
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
            if (clear) Console.Clear();
        }

        #endregion
    }
}
