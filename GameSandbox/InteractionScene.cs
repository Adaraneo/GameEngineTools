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
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.DependencyInjection;
using NPC = GameEngineTools.Characters.GameObjects.NPC;

namespace GameSandbox.Scenes
{
    /// <summary>
    /// Sandbox scéna simulující interakce mezi hráčem a NPC v průběhu herního času.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tato scéna obaluje simulační smyčku, která v každém ticku:
    /// <list type="number">
    ///   <item>Rozesílá plánované herní eventy (SmallTalk, Validation...)</item>
    ///   <item>Tickuje NPC a hráče (engine zpracuje eventy a posune stavy)</item>
    ///   <item>Přeposílá výstupy (InteractionOutcome, ReachOut) mezi postavami</item>
    ///   <item>Zpracovává sleep prompty (NPC automaticky, hráč zatím taky)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Po skončení simulace exportuje stav obou postav do souborů a na plochu.
    /// </para>
    /// </remarks>
    internal sealed class InteractionScene
    {
        #region Privátní pole

        /// <summary>Herní runtime — drží DI kontejner, clock, manager postav.</summary>
        private readonly GameEngineToolsRuntimeHandle _runtime;

        /// <summary>Systémové hodiny — umožňují ručně posouvat herní čas.</summary>
        private readonly SystemClock _clock;

        /// <summary>Správce postav — eviduje všechny NPC a hráče.</summary>
        private readonly GameEngineToolsManager _manager;

        /// <summary>Generátor a importér/exportér postav.</summary>
        private readonly GeneratedFile _gf;

        /// <summary>Cesta k souboru s uloženým herním časem na ploše.</summary>
        private readonly string _gameTimePath;

        #endregion Privátní pole

        #region Konstruktor

        /// <summary>
        /// Vytvoří novou instanci <see cref="InteractionScene"/>.
        /// </summary>
        /// <param name="runtime">Běžící herní runtime s nakonfigurovanými službami.</param>
        /// <param name="gameTimePath">Cesta k souboru pro persistenci herního času.</param>
        public InteractionScene(GameEngineToolsRuntimeHandle runtime, string gameTimePath)
        {
            _runtime = runtime;
            _clock = (SystemClock)runtime.Clock;
            _manager = (GameEngineToolsManager)runtime.GameEngineToolsManager;
            _gf = (GeneratedFile)runtime.Services.GetRequiredService<IGeneratedFile>();
            _gameTimePath = gameTimePath;
        }

        #endregion Konstruktor

        #region Veřejné metody

        /// <summary>
        /// Spustí celou interakční scénu — načte postavy, simuluje čas a exportuje výsledky.
        /// </summary>
        public async Task RunAsync()
        {
            var (player, significantOther) = LoadCharacters();

            PrintHeader(player, significantOther);
            PressAnyKeyToContinue();

            await SimulateAsync(player, significantOther);

            Export(player, significantOther);
        }

        #endregion Veřejné metody

        #region Načítání postav

        /// <summary>
        /// Načte hráčskou postavu a prvního NPC z disku do manageru.
        /// </summary>
        /// <returns>
        /// Tuple obsahující hráče a jeho "significant other" NPC.
        /// </returns>
        private (CharacterBase player, CharacterBase significantOther) LoadCharacters()
        {
            // Načteme hráče z jeho adresáře a přidáme do manageru
            var player = _gf.ImportPC(new FileInfo(Directory.GetFiles(_gf.PlayerDirectory).First()).Name);
            _manager.NPPCs.Add(player);

            // Nastavíme herní čas na 16. narozeniny hráče (pokud spouštíme poprvé)
            // jinak obnovíme uložený čas
            var savedTicks = File.Exists(_gameTimePath) && long.TryParse(File.ReadAllText(_gameTimePath), out var t)
                ? t
                : (long?)null;

            _clock.SetNow(savedTicks.HasValue
                ? new WDateTime(savedTicks.Value)
                : WDateTime.New(player.Person.Identity.BirthDate.AddYears(16)));

            // Načteme všechna NPC z jejich adresáře
            foreach (var filename in Directory.GetFiles(_gf.NPCDirectory))
                _manager.NPPCs.Add(_gf.ImportNPC(new FileInfo(filename).Name));

            var significantOther = _manager.NPPCs.First(x => x is NPC);

            return (player, significantOther);
        }

        #endregion Načítání postav

        #region Simulační smyčka

        /// <summary>
        /// Hlavní simulační smyčka — iteruje po herních tickách po dobu 2 herních let.
        /// </summary>
        /// <param name="player">Hráčská postava.</param>
        /// <param name="significantOther">NPC s nímž probíhají interakce.</param>
        private Task SimulateAsync(CharacterBase player, CharacterBase significantOther)
        {
            var playerPerson = player.Person;
            var npcPerson = significantOther.Person;

            var dt = WTimeSpan.FromHours(0.5);
            var endTime = _clock.Now.AddYears(2);

            while (_clock.Now < endTime)
            {
                var now = _clock.Now;

                DispatchScheduledEvents(now, playerPerson, npcPerson);

                TickNpc(now, dt, playerPerson, npcPerson);

                TickPlayer(now, dt, playerPerson, npcPerson);

                HandleNpcSleepPrompt(now, npcPerson);

                HandlePlayerSleepPrompt(now, playerPerson);

                _clock.Advance(WTimeSpan.FromHours(1));
                Console.WriteLine("now: {0}", _clock.Now.ToString());
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Rozesílá eventy naplánované na konkrétní dny herního měsíce.
        /// </summary>
        /// <remarks>
        /// Toto je místo, kde definuješ "scénář" — co se stane v jaký den.
        /// Den 2, 6, 12 → hráč iniciuje SmallTalk s NPC.
        /// Den 10 → NPC pošle hráči Validation.
        /// </remarks>
        /// <param name="now">Aktuální herní čas.</param>
        /// <param name="playerPerson">Osoba hráče.</param>
        /// <param name="npcPerson">Osoba NPC.</param>
        private static void DispatchScheduledEvents(WDateTime now, IHuman playerPerson, IHuman npcPerson)
        {
            // Dny 2, 6, 12 v měsíci → hráč zahajuje small talk
            if (now.Day is 2 or 6 or 12)
            {
                var smallTalk = new InteractionProposed(
                    OccurredAt: now + WTimeSpan.FromMinutes(30),
                    From: playerPerson.Id,
                    To: npcPerson.Id,
                    Act: SpeechAct.SmallTalk,
                    Content: "Ahoooj");

                npcPerson.ReceiveEvent(smallTalk);
            }

            // Den 10 → NPC posílá hráči validaci
            if (now.Day is 10)
            {
                var validation = new InteractionProposed(
                    OccurredAt: now + WTimeSpan.FromMinutes(12),
                    From: npcPerson.Id,
                    To: playerPerson.Id,
                    Act: SpeechAct.Validation,
                    Content: "Sluší ti to.");

                playerPerson.ReceiveEvent(validation);
            }
        }

        /// <summary>
        /// Tickuje NPC a přeposílá jeho výstupy hráči.
        /// </summary>
        /// <param name="now">Aktuální herní čas.</param>
        /// <param name="dt">Délka herního kroku.</param>
        /// <param name="playerPerson">Osoba hráče — příjemce výstupů NPC.</param>
        /// <param name="npcPerson">Osoba NPC — tickuje jako první.</param>
        private static void TickNpc(WDateTime now, WTimeSpan dt, IHuman playerPerson, IHuman npcPerson)
        {
            npcPerson.Tick(now, dt);

            // Pokud NPC samo od sebe zahájilo ReachOut → přepošleme hráči jako SmallTalk
            var reachOut = npcPerson.LastOutbox
                .OfType<ActionCommitted>()
                .FirstOrDefault(a => a.ActionName == "ReachOut");

            if (reachOut != null)
            {
                var initiated = new InteractionProposed(
                    OccurredAt: now,
                    From: npcPerson.Id,
                    To: playerPerson.Id,
                    Act: SpeechAct.SmallTalk,
                    Content: "Ehm... Ahoj");

                playerPerson.ReceiveEvent(initiated);
            }

            // Výsledek interakce z NPC → přepošleme hráči
            var outcome = npcPerson.LastOutbox.OfType<InteractionOutcome>().FirstOrDefault();
            if (outcome != null)
                playerPerson.ReceiveEvent(outcome);
        }

        /// <summary>
        /// Tickuje hráče a přeposílá jeho výstupy NPC.
        /// </summary>
        /// <param name="now">Aktuální herní čas.</param>
        /// <param name="dt">Délka herního kroku.</param>
        /// <param name="playerPerson">Osoba hráče — tickuje jako druhý.</param>
        /// <param name="npcPerson">Osoba NPC — příjemce výstupů hráče.</param>
        private static void TickPlayer(WDateTime now, WTimeSpan dt, IHuman playerPerson, IHuman npcPerson)
        {
            playerPerson.Tick(now, dt);

            // Výsledek interakce z hráče → přepošleme NPC
            var playerOutcome = playerPerson.LastOutbox.OfType<InteractionOutcome>().FirstOrDefault();
            if (playerOutcome != null)
                npcPerson.ReceiveEvent(playerOutcome);

            // Hráčův ReachOut → NPC dostane SmallTalk a hned ho tickneme
            var playerReachOut = playerPerson.LastOutbox
                .OfType<ActionCommitted>()
                .FirstOrDefault(a => a.ActionName == "ReachOut");

            if (playerReachOut != null)
            {
                var initiated = new InteractionProposed(
                    OccurredAt: now,
                    From: playerPerson.Id,
                    To: npcPerson.Id,
                    Act: SpeechAct.SmallTalk,
                    Content: "Ehm... ahoj.");

                npcPerson.ReceiveEvent(initiated);
                npcPerson.Tick(now, dt);

                // Reakce NPC na iniciaci od hráče → vrátíme hráči
                var npcOutcome = npcPerson.LastOutbox.OfType<InteractionOutcome>().FirstOrDefault();
                if (npcOutcome != null)
                    playerPerson.ReceiveEvent(npcOutcome);
            }
        }

        /// <summary>
        /// Zpracuje sleep prompt pro NPC — NPC spánek vždy automaticky potvrdí.
        /// </summary>
        /// <param name="now">Aktuální herní čas.</param>
        /// <param name="npcPerson">Osoba NPC.</param>
        private static void HandleNpcSleepPrompt(WDateTime now, IHuman npcPerson)
        {
            var sleepPrompt = npcPerson.LastOutbox
                .OfType<SleepPromptRequested>()
                .FirstOrDefault();

            if (sleepPrompt == null) return;

            // NPC nemá UI — spánek vždy automaticky potvrdíme
            // default PlannedWakeUp = engine vypočítá délku sám
            var confirmed = new SleepConfirmed(
                OccurredAt: now,
                Human: sleepPrompt.Human,
                PlannedWakeUp: default);

            npcPerson.ReceiveEvent(confirmed);
        }

        /// <summary>
        /// Zpracuje sleep prompt pro hráče.
        /// </summary>
        /// <remarks>
        /// Zatím automaticky potvrzuje jako NPC.
        /// Až budeš mít UI, odkomentuj konzolový prompt níže a připoj svůj input systém.
        /// </remarks>
        /// <param name="now">Aktuální herní čas.</param>
        /// <param name="playerPerson">Osoba hráče.</param>
        private static void HandlePlayerSleepPrompt(WDateTime now, IHuman playerPerson)
        {
            var sleepPrompt = playerPerson.LastOutbox
                .OfType<SleepPromptRequested>()
                .FirstOrDefault();

            if (sleepPrompt == null) return;

            // TODO: až budeš mít UI, nahraď toto skutečným vstupem hráče:
            //
            // Console.WriteLine($"\n[SPÁNEK] Postava je unavená. Jít spát? (A/N)");
            // var key = Console.ReadKey(intercept: true).Key;
            // IDomainEvent response = key == ConsoleKey.A
            //     ? new SleepConfirmed(now, sleepPrompt.Human, default)
            //     : new SleepDeclined(now, sleepPrompt.Human, DeclineCount: playerPerson.Snapshot.Behavior.SleepDeclineCount);

            var response = new SleepConfirmed(now, sleepPrompt.Human, default);
            playerPerson.ReceiveEvent(response);
        }

        #endregion Simulační smyčka

        #region Export a výstup

        /// <summary>
        /// Exportuje postavy na disk, vypíše výsledky do konzole a uloží herní čas.
        /// </summary>
        /// <param name="player">Hráčská postava k exportu.</param>
        /// <param name="significantOther">NPC k exportu.</param>
        private void Export(CharacterBase player, CharacterBase significantOther)
        {
            PressAnyKeyToContinue(clear: true);

            _gf.Export((PC)player);
            _gf.Export((NPC)significantOther);

            Console.WriteLine(player.PrintInfo(false));
            Console.WriteLine(significantOther.PrintInfo(false));

            PressAnyKeyToContinue();

            // Uloží logy na plochu pro snadnou analýzu
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            File.WriteAllText(
                Path.Combine(desktop, $"player.{player.Person.Id.Value}.log.txt"),
                player.PrintInfo(false));

            File.WriteAllText(
                Path.Combine(desktop, $"significantOther.{significantOther.Person.Id.Value}.log.txt"),
                significantOther.PrintInfo(false));

            // Persistuje herní čas pro příští spuštění
            File.WriteAllText(_gameTimePath, _clock.Now.WorldTicks.ToString());
        }

        #endregion Export a výstup

        #region Konzolové utility

        /// <summary>
        /// Vypíše hlavičku scény — aktuální čas a základní info o postavách.
        /// </summary>
        /// <param name="player">Hráčská postava.</param>
        /// <param name="significantOther">NPC.</param>
        private void PrintHeader(CharacterBase player, CharacterBase significantOther)
        {
            Console.WriteLine("Now: {0}", _clock.Now.ToString());
            Console.WriteLine("Player: {0}", player.PrintInfo(true));
            Console.WriteLine("SignificantOther: {0}", significantOther.PrintInfo(true));
            Console.WriteLine("==========================================================");
        }

        /// <summary>
        /// Pozastaví běh a čeká na stisk klávesy.
        /// </summary>
        /// <param name="clear">Pokud <c>true</c>, vymaže konzoli po stisku klávesy.</param>
        private static void PressAnyKeyToContinue(bool clear = false)
        {
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
            if (clear)
                Console.Clear();
        }

        #endregion Konzolové utility
    }
}
