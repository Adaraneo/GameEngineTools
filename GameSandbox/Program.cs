// See https://aka.ms/new-console-template for more information
using System.Text;
using GameEngineTools;
using GameEngineTools.Characters.GameObjects;
using GameEngineTools.Characters.Generation;
using GameEngineTools.Extensions;
using GameEngineTools.FileSystem;
using GameEngineTools.World.Core.Time;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.DependencyInjection;
using NPC = GameEngineTools.Characters.GameObjects.NPC;
using TFSC = GameEngineTools.Constants.TestFSConstatns;

const int maxHealth = 100;

await using var runtime = await GameEngineToolsRuntime.StartAsync(HumanBlueprintSpec.Default(WDateOnly.FromParts(1312,1,1)), generatedFileOptions: new GeneratedFileOptions
{
    PlayerDirectory = TFSC.player,
    NPCDirectory = TFSC.NPCs
}, timescale: 0.005);
var gf = (GeneratedFile)runtime.Services.GetRequiredService<IGeneratedFile>();

var manager = runtime.GameEngineToolsManager as GameEngineToolsManager;
var clock = (SystemClock)runtime.Clock;

var player = gf.ImportPC(new FileInfo(Directory.GetFiles(gf.PlayerDirectory).First()).Name);
manager.NPPCs.Add(player);

foreach (var filename in Directory.GetFiles(gf.NPCDirectory))
{
    manager.NPPCs.Add(gf.ImportNPC(new FileInfo(filename).Name));
}


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



Console.WriteLine("==========================================================");

PressAnyKeyToContinueM();
//clock.Start();




PressAnyKeyToContinueM(true);

gf.Export(player);
gf.Export((NPC)significantOther);

//clock.Stop();
Console.WriteLine(player.PrintInfo(false));

Console.WriteLine(significantOther.PrintInfo(false));

PressAnyKeyToContinueM();

void PressAnyKeyToContinueM(bool clear = false)
{
    Console.WriteLine("Press any key to continue...");
    Console.ReadKey();
    if (clear)
        Console.Clear();
}