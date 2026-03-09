// See https://aka.ms/new-console-template for more information
using GameEngineTools;
using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Sleep;
using GameEngineTools.Characters.Generation;
using GameEngineTools.Config;
using GameEngineTools.Extensions;
using GameEngineTools.FileSystem;
using GameEngineTools.World.Core.Calendars;
using GameEngineTools.World.Core.Time;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices.ComTypes;
using NPC = GameEngineTools.Characters.GameObjects.NPC;
using TFSC = GameEngineTools.Constants.TestFSConstatns;

const int maxHealth = 100;

var gameTimePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "GameTime.txt");

var spec = GameEngineToolsRuntime.LoadSpec();

var defaultTicks = spec.Calendar.DaysFromDate(1, 1, 1) * spec.TicksPerDay;

var initTicks = File.Exists(gameTimePath) && long.TryParse(File.ReadAllText(gameTimePath), out var saved)
    ? saved
    : defaultTicks;

var initNow = new WDateTime(initTicks);

await using var runtime = await GameEngineToolsRuntime.StartAsync(initNow, consoleLogs: false, generatedFileOptions: new GeneratedFileOptions
{
    PlayerDirectory = TFSC.player,
    NPCDirectory = TFSC.NPCs
}, timescale: 1);

var gf = (GeneratedFile)runtime.Services.GetRequiredService<IGeneratedFile>();
var manager = (GameEngineToolsManager)runtime.GameEngineToolsManager;
var clock = (SystemClock)runtime.Clock;

clock.SetNow(WDateTime.New(WDateOnly.New(50, 1, 1)));

var player = gf.ImportPC(new FileInfo(Directory.GetFiles(gf.PlayerDirectory).First()).Name);
manager.NPPCs.Add(player);

clock.SetNow((initTicks == defaultTicks) ? WDateTime.New(player.Person.Identity.BirthDate.AddYears(16)) : new WDateTime(initTicks));

foreach (var filename in Directory.GetFiles(gf.NPCDirectory))
{
    manager.NPPCs.Add(gf.ImportNPC(new FileInfo(filename).Name));
}
var playerPerson = player.Person;

var significantOther = manager.NPPCs.First(x => x is NPC);
var significantOtherPerson = significantOther.Person;

#region Families

//var families = manager.NPPCs.Where(x => x is NPC && x.Person != significantOtherPerson);

//foreach (var familyMember in families)
//{
//    Console.WriteLine(familyMember.PrintInfo(false) + "\n");
//}

//PrintRelationshipInfo(families.ToArray());

#endregion

//FireScene();

//PrintSymbolicRelationsInfoAccordingToPlayer(manager.NPPCs.ToArray());
//PrintSymbolicRelationsInfo(families.ToArray());

Console.WriteLine("Now: {0}", clock.Now.ToString());
Console.WriteLine("Player: {0}", player.PrintInfo(true));
Console.WriteLine("SignificantOther: {0}", significantOther.PrintInfo(true));

Console.WriteLine("==========================================================");

PressAnyKeyToContinueM();
//clock.Start();

var dt = WTimeSpan.FromHours(0.5);
var endTime = clock.Now.AddYears(2);

while(clock.Now < endTime)
{
    var now = clock.Now;

    if (now.Day is 2 or 6 or 12)
    {
        var smallTalk = new InteractionProposed(now + WTimeSpan.FromMinutes(30), playerPerson.Id, significantOtherPerson.Id, SpeechAct.SmallTalk, "Ahoooj");
        significantOtherPerson.ReceiveEvent(smallTalk);
    }

    if (now.Day is 10)
    {
        var action = new InteractionProposed(now + WTimeSpan.FromMinutes(12), significantOtherPerson.Id, playerPerson.Id, SpeechAct.Validation, "Sluší ti to.");
        playerPerson.ReceiveEvent(action);
    }

    significantOtherPerson.Tick(now, dt);

    var reachOut = significantOtherPerson.LastOutbox.OfType<ActionCommitted>().FirstOrDefault(a => a.ActionName == "ReachOut");
    if (reachOut != null)
    {
        var initialized = new InteractionProposed(now, significantOtherPerson.Id, playerPerson.Id, SpeechAct.SmallTalk, "Ehm... Ahoj");
        playerPerson.ReceiveEvent(initialized);
    }

    var outcome = significantOtherPerson.LastOutbox.OfType<InteractionOutcome>().FirstOrDefault();
    if (outcome != null)
        playerPerson.ReceiveEvent(outcome);

    playerPerson.Tick(now, dt);

    // ── Outcome z playera → significantOther ──
    var playerOutcome = playerPerson.LastOutbox.OfType<InteractionOutcome>().FirstOrDefault();
    if (playerOutcome != null)
        significantOtherPerson.ReceiveEvent(playerOutcome);

    // ── Player's ReachOut → NPC ──
    var playerReachOut = playerPerson.LastOutbox
        .OfType<ActionCommitted>()
        .FirstOrDefault(a => a.ActionName == "ReachOut");

    if (playerReachOut != null)
    {
        var initiated = new InteractionProposed(now, playerPerson.Id, significantOtherPerson.Id, SpeechAct.SmallTalk, "Ehm... ahoj.");
        significantOtherPerson.ReceiveEvent(initiated);
        significantOtherPerson.Tick(now, dt);
        var npcOutcome = significantOtherPerson.LastOutbox.OfType<InteractionOutcome>().FirstOrDefault();
        if (npcOutcome != null)
            playerPerson.ReceiveEvent(npcOutcome);
    }

    // ── Sleep prompt handling ──────────────────────────────────────────────────
    // PC: hráč musí odpovědět, NPC: systém odpoví automaticky
    var sleepPrompt = significantOtherPerson.LastOutbox
        .OfType<SleepPromptRequested>()
        .FirstOrDefault();

    if (sleepPrompt != null)
    {
        // NPC → automaticky potvrdí (žádný UI prompt)
        var confirmed = new SleepConfirmed(
            OccurredAt: now,
            Human: sleepPrompt.Human,
            PlannedWakeUp: default);   // default = engine vypočítá délku sám

        significantOtherPerson.ReceiveEvent(confirmed);
    }

    // PC: hráč dostane konzolový prompt
    var playerSleepPrompt = playerPerson.LastOutbox
        .OfType<SleepPromptRequested>()
        .FirstOrDefault();

    if (playerSleepPrompt != null)
    {
        //Console.WriteLine($"\n[SPÁNEK] Postava je unavená (potřeba: {playerSleepPrompt.SleepNeed:F0}). Jít spát? (A/N)");
        //var key = Console.ReadKey(intercept: true).Key;

        //IDomainEvent response = key == ConsoleKey.A
        //    ? new SleepConfirmed(now, playerSleepPrompt.Human, default)
        //    : new SleepDeclined(now, playerSleepPrompt.Human, DeclineCount: playerPerson.Snapshot.Behavior.SleepDeclineCount);

        var response = new SleepConfirmed(now, playerSleepPrompt.Human, default);

        playerPerson.ReceiveEvent(response);
    }

    clock.Advance(WTimeSpan.FromHours(1));

    now = clock.Now;
    Console.WriteLine("now: {0}", clock.Now.ToString());
}

PressAnyKeyToContinueM(true);

gf.Export(player);
gf.Export((NPC)significantOther);

//clock.Stop();
Console.WriteLine(player.PrintInfo(false));

Console.WriteLine(significantOther.PrintInfo(false));

PressAnyKeyToContinueM();

File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"{nameof(player)}.{player.Person.Id.Value.ToString()}.log.txt"), player.PrintInfo(false));
File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"{nameof(significantOther)}.{significantOther.Person.Id.Value.ToString()}.log.txt"), significantOther.PrintInfo(false));
File.WriteAllText(gameTimePath, clock.Now.WorldTicks.ToString());

void PressAnyKeyToContinueM(bool clear = false)
{
    Console.WriteLine("Press any key to continue...");
    Console.ReadKey();
    if (clear)
        Console.Clear();
}