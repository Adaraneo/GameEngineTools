// See https://aka.ms/new-console-template for more information
using GameEngineTools;
using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Generation;
using GameEngineTools.Extensions;
using GameEngineTools.FileSystem;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.DependencyInjection;
using NPC = GameEngineTools.Characters.GameObjects.NPC;
using TFSC = GameEngineTools.Constants.TestFSConstatns;

const int maxHealth = 100;

var gamteTimePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "GameTime.txt");

WDateTime initNow = new WDateTime(1,1,1,0,0,0);
if (File.Exists(gamteTimePath))
{
    if (!WDateTime.TryParse(File.ReadAllText(gamteTimePath), out initNow))
    {
        throw new InvalidOperationException("Unable to parse WDateTime");
    }
}

await using var runtime = await GameEngineToolsRuntime.StartAsync(HumanBlueprintSpec.Default(initNow.DateOnly), initNow, generatedFileOptions: new GeneratedFileOptions
{
    PlayerDirectory = TFSC.player,
    NPCDirectory = TFSC.NPCs
}, timescale: 1);
var gf = (GeneratedFile)runtime.Services.GetRequiredService<IGeneratedFile>();

var manager = runtime.GameEngineToolsManager as GameEngineToolsManager;
var clock = (SystemClock)runtime.Clock;

var player = gf.ImportPC(new FileInfo(Directory.GetFiles(gf.PlayerDirectory).First()).Name);
manager.NPPCs.Add(player);

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

File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"{player.Person.ToString()}.log.txt"), player.PrintInfo(false));

Console.WriteLine("Now: {0}", clock.Now);
Console.WriteLine("Player: {0}", player.PrintInfo(true));
Console.WriteLine("SignificantOther: {0}", significantOther.PrintInfo(true));

Console.WriteLine("==========================================================");

PressAnyKeyToContinueM();
//clock.Start();

var now = clock.Now;
var dt = WTimeSpan.FromHours(0.5);

for (int d = 0; d < 30; d++)
{
    for (int i = 0; i < 24; i++)
    {
        if (d == 2 && i == 8)
        {
            var smallTalk = new InteractionProposed(now + WTimeSpan.FromMinutes(30), playerPerson.Id, significantOtherPerson.Id, SpeechAct.SmallTalk, "Ahoooj");
            significantOtherPerson.ReceiveEvent(smallTalk);
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

        now += dt;
        clock.SetNow(now);
    }
}

PressAnyKeyToContinueM(true);

gf.Export(player);
gf.Export((NPC)significantOther);

//clock.Stop();
Console.WriteLine(player.PrintInfo(false));

Console.WriteLine(significantOther.PrintInfo(false));

PressAnyKeyToContinueM();

File.WriteAllText(gamteTimePath, clock.Now.ToString());

void PressAnyKeyToContinueM(bool clear = false)
{
    Console.WriteLine("Press any key to continue...");
    Console.ReadKey();
    if (clear)
        Console.Clear();
}